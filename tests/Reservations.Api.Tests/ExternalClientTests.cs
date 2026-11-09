using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Reservations.Api.Tests;

public sealed class ExternalClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.NotFound, 400)]
    [InlineData(HttpStatusCode.InternalServerError, 503)]
    public async Task RoomLookupUsesAssetsHttpApi(HttpStatusCode status, int? expectedError)
    {
        var room = Guid.NewGuid();
        using var handler = new StubHandler(request =>
        {
            Assert.Equal($"/api/v3/assets/rooms/{room}", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(status);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://assets/api/v3/assets/") };
        var client = new AssetsClient(http);
        if (expectedError is not null)
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => client.RequireRoomAsync(room, CancellationToken.None));
            Assert.Equal(expectedError, error.StatusCode);
        }
        else
        {
            await client.RequireRoomAsync(room, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("{\"live\":true}", true)]
    [InlineData("{\"live\":false}", false)]
    [InlineData("{}", false)]
    [InlineData("invalid-json", false)]
    [InlineData("[]", false)]
    [InlineData("null", false)]
    [InlineData("true", false)]
    [InlineData("\"healthy\"", false)]
    public async Task AssetsReadinessUsesLivenessNotRecursiveReadiness(string body, bool expected)
    {
        using var handler = new StubHandler(request =>
        {
            Assert.Equal("/api/v3/assets/health/live", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://assets/api/v3/assets/") };
        Assert.Equal(expected, await new AssetsClient(http).IsLiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InternalJwksEndpointLoadsPublicKeysWithExternalIssuer()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var document = JsonSerializer.Serialize(new { keys = new[] { new {
            kty = "RSA", kid = "rotating-key", use = "sig", alg = "RS256",
            n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!)
        } } });
        const string address = "http://keycloak/auth/realms/biletado/protocol/openid-connect/certs";
        var configuration = await new JwksConfigurationRetriever("https://public.example/auth/realms/biletado")
            .GetConfigurationAsync(address, new StubDocumentRetriever(address, document), CancellationToken.None);
        Assert.Equal("https://public.example/auth/realms/biletado", configuration.Issuer);
        Assert.Equal("rotating-key", Assert.Single(configuration.SigningKeys).KeyId);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class StubDocumentRetriever(string expectedAddress, string document) : IDocumentRetriever
    {
        public Task<string> GetDocumentAsync(string address, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedAddress, address);
            return Task.FromResult(document);
        }
    }
}