using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Reservations.Api.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Root = "/api/v4/reservations";

    [Theory]
    [InlineData("/status")]
    [InlineData("/health/live")]
    [InlineData("/api/v4/reservations/status")]
    [InlineData("/api/v4/reservations/health/live")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task PublicOperationalEndpointsDoNotRequireLogin(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/status/auth")]
    [InlineData("/reservations")]
    [InlineData("/api/v4/reservations/reservations")]
    public async Task ProtectedEndpointsRejectMissingTokens(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task InvalidSignedTokensAreRejected(bool expired, bool issuer, bool audience, bool signature)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(expired, issuer, audience, signature));
        using var response = await client.GetAsync($"{Root}/status/auth");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidSignedTokenIsAccepted()
    {
        using var client = AuthenticatedClient();
        using var response = await client.GetAsync($"{Root}/status/auth");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/v4/reservations/reservations")]
    [InlineData("PUT", "/api/v4/reservations/reservations/22222222-2222-2222-2222-222222222222")]
    [InlineData("DELETE", "/api/v4/reservations/reservations/22222222-2222-2222-2222-222222222222")]
    public async Task WritesCannotBypassAuthentication(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{", "invalid_json")]
    [InlineData("{}", "schema_violation")]
    [InlineData("null", "schema_violation")]
    [InlineData("{\"name\":\"Max\",\"name\":\"Anna\"}", "schema_violation")]
    public async Task InvalidPayloadProducesSpecifiedError(string payload, string code)
    {
        using var client = AuthenticatedClient();
        using var response = await client.PostAsync($"{Root}/reservations", new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [PostgresFact]
    public async Task ReservationLifecycleIncludesDeletionRestoreAndETags()
    {
        using var client = AuthenticatedClient();
        var payload = Payload(Guid.NewGuid());
        using var created = await client.PostAsJsonAsync($"{Root}/reservations", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        var reservation = await created.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(reservation);
        var path = $"{Root}/reservations/{reservation.Id}";
        var publicPath = $"{Root}/public-reservations/{reservation.Id}";
        using var anonymous = factory.CreateClient();
        using var visible = await anonymous.GetAsync(publicPath);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);

        using var staleRequest = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(payload) };
        staleRequest.Headers.TryAddWithoutValidation("If-Match", "\"stale\"");
        using var stale = await client.SendAsync(staleRequest);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        using var replaceRequest = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(payload) };
        replaceRequest.Headers.IfMatch.Add(created.Headers.ETag!);
        using var replaced = await client.SendAsync(replaceRequest);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.NotEqual(created.Headers.ETag, replaced.Headers.ETag);

        using var deleted = await client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var hidden = await anonymous.GetAsync(publicPath);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var protectedDeleted = await client.GetAsync(path);
        Assert.Contains("deleted_at", await protectedDeleted.Content.ReadAsStringAsync());
        using var noRestore = await client.PutAsJsonAsync(path, payload);
        Assert.Equal(HttpStatusCode.Conflict, noRestore.StatusCode);
        var restore = new { payload.name, payload.from, payload.to, payload.room_id, deleted_at = (string?)null };
        using var restored = await client.PutAsJsonAsync(path, restore);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var permanent = await client.DeleteAsync($"{path}?permanent=true");
        Assert.Equal(HttpStatusCode.NoContent, permanent.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentOverlappingRequestsCannotDoubleBook()
    {
        using var client = AuthenticatedClient();
        var payload = Payload(Guid.NewGuid());
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync($"{Root}/reservations", payload)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [PostgresFact]
    public async Task AdjacentReservationsAndInclusiveFiltersWork()
    {
        using var client = AuthenticatedClient();
        var room = Guid.NewGuid();
        using var first = await client.PostAsJsonAsync($"{Root}/reservations", Payload(room));
        using var adjacent = await client.PostAsJsonAsync($"{Root}/reservations", new { name = "Second reservation", from = "2026-11-03", to = "2026-11-05", room_id = room });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);
        using var filtered = await client.GetAsync($"{Root}/public-reservations?room_id={room}&before=2026-11-01&after=2026-11-03");
        using var document = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        Assert.Single(document.RootElement.GetProperty("reservations").EnumerateArray());
    }

    [PostgresFact]
    public async Task MissingRoomAndReadinessFailuresAreReported()
    {
        using var client = AuthenticatedClient();
        using var missing = await client.PostAsJsonAsync($"{Root}/reservations", Payload(factory.Assets.MissingRoom));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        factory.Assets.Live = false;
        try
        {
            using var ready = await client.GetAsync($"{Root}/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            using var live = await client.GetAsync($"{Root}/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally
        {
            factory.Assets.Live = true;
        }
    }

    [PostgresFact]
    public async Task ConcurrentUpdatesWithSameETagAllowOnlyOneWriter()
    {
        using var client = AuthenticatedClient();
        var payload = Payload(Guid.NewGuid());
        using var created = await client.PostAsJsonAsync($"{Root}/reservations", payload);
        var reservation = await created.Content.ReadFromJsonAsync<ReservationResponse>();
        var path = $"{Root}/reservations/{reservation!.Id}";
        var requests = Enumerable.Range(0, 2).Select(_ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(payload) };
            request.Headers.IfMatch.Add(created.Headers.ETag!);
            return request;
        }).ToArray();
        var responses = await Task.WhenAll(requests.Select(request => client.SendAsync(request)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        foreach (var response in responses) response.Dispose();
        foreach (var request in requests) request.Dispose();
    }

    [PostgresFact]
    public async Task DeletedReservationsDoNotBlockRoomsButRestoreCannotOverlap()
    {
        using var client = AuthenticatedClient();
        var payload = Payload(Guid.NewGuid());
        var id = Guid.NewGuid();
        var path = $"{Root}/reservations/{id}";
        using var created = await client.PutAsJsonAsync(path, payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var deleted = await client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var replacement = await client.PostAsJsonAsync($"{Root}/reservations", payload);
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        using var restore = await client.PutAsJsonAsync(path, new { payload.name, payload.from, payload.to, payload.room_id, deleted_at = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
        using var publicList = await client.GetAsync($"{Root}/public-reservations?room_id={payload.room_id}&include_deleted=true");
        using var publicDocument = JsonDocument.Parse(await publicList.Content.ReadAsStringAsync());
        Assert.Single(publicDocument.RootElement.GetProperty("reservations").EnumerateArray());
        using var privateList = await client.GetAsync($"{Root}/reservations?room_id={payload.room_id}&include_deleted=true");
        using var privateDocument = JsonDocument.Parse(await privateList.Content.ReadAsStringAsync());
        Assert.Equal(2, privateDocument.RootElement.GetProperty("reservations").GetArrayLength());
        using var staleDelete = new HttpRequestMessage(HttpMethod.Delete, path);
        staleDelete.Headers.TryAddWithoutValidation("If-Match", "\"stale\"");
        using var rejected = await client.SendAsync(staleDelete);
        Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
    }

    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        return client;
    }

    private sealed record TestPayload(string name, string from, string to, Guid room_id);
    private static TestPayload Payload(Guid room) => new("Test reservation", "2026-11-01", "2026-11-03", room);
}