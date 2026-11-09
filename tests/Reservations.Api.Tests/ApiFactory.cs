using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Reservations.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly RSA rsa = RSA.Create(2048);
    public TestAssets Assets { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Authority", "https://tests.invalid");
        builder.UseSetting("Logging:LogLevel:Default", "Critical");
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION");
        builder.UseSetting("Database:Initialize", (connectionString is not null).ToString());
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Reservations", connectionString);
        }
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAssetsClient>();
            services.AddSingleton<IAssetsClient>(Assets);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = "https://tests.invalid" };
                configuration.SigningKeys.Add(new RsaSecurityKey(rsa) { KeyId = "test-key" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.ValidIssuer = configuration.Issuer;
                options.TokenValidationParameters.ValidAudience = "tests";
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            });
        });
    }

    public string Token(bool expired = false, bool wrongIssuer = false, bool wrongAudience = false, bool wrongSignature = false)
    {
        using var otherRsa = RSA.Create(2048);
        var key = new RsaSecurityKey(wrongSignature ? otherRsa : rsa) { KeyId = "test-key" };
        var token = new JwtSecurityToken(wrongIssuer ? "https://wrong.invalid" : "https://tests.invalid",
            wrongAudience ? "wrong" : "tests", [new Claim("sub", "test-user")], DateTime.UtcNow.AddHours(-1),
            expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            rsa.Dispose();
        }
    }
}

public sealed class TestAssets : IAssetsClient
{
    public Guid MissingRoom { get; } = Guid.NewGuid();
    public bool Live { get; set; } = true;
    public Task RequireRoomAsync(Guid roomId, CancellationToken cancellationToken)
    {
        if (roomId == MissingRoom)
        {
            throw new ApiException(400, "room_not_found", "Room not found.");
        }
        return Task.CompletedTask;
    }
    public Task<bool> IsLiveAsync(CancellationToken cancellationToken) => Task.FromResult(Live);
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION")))
        {
            Skip = "Set POSTGRES_TEST_CONNECTION to run against a dedicated PostgreSQL test database.";
        }
    }
}