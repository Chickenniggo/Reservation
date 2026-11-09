using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Reservations.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
	var realm = builder.Configuration["KEYCLOAK_REALM"] ?? "biletado";
	var host = builder.Configuration["KEYCLOAK_HOST"] ?? "localhost:9090";
	var authority = builder.Configuration["Jwt:Authority"] ?? $"http://{host}/auth/realms/{realm}";
	options.Authority = authority;
	options.MetadataAddress = builder.Configuration["Jwt:MetadataAddress"] ?? $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
	options.RequireHttpsMetadata = builder.Configuration.GetValue("Jwt:RequireHttpsMetadata", true);
	options.MapInboundClaims = false;
	if (builder.Configuration["Jwt:JwksUri"] is { Length: > 0 } jwksUri)
	{
		options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(jwksUri,
			new JwksConfigurationRetriever(builder.Configuration["Jwt:Issuer"] ?? authority),
			new HttpDocumentRetriever { RequireHttps = options.RequireHttpsMetadata });
	}
	options.TokenValidationParameters = new TokenValidationParameters
	{
		ValidateIssuer = true,
		ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? authority,
		ValidateAudience = true,
		ValidAudience = builder.Configuration["Jwt:Audience"] ?? "account",
		ValidateLifetime = true,
		RequireExpirationTime = true,
		RequireSignedTokens = true,
		ValidateIssuerSigningKey = true,
		ClockSkew = TimeSpan.FromSeconds(30),
		ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
	};
	options.Events = new JwtBearerEvents
	{
		OnChallenge = async context =>
		{
			context.HandleResponse();
			context.Response.Headers.WWWAuthenticate = "Bearer";
			context.Response.StatusCode = 401;
			await context.Response.WriteAsJsonAsync(new { errors = new[] { new { code = "not_authorized", message = "A valid bearer token is required." } } });
		}
	};
});
var connectionString = builder.Configuration.GetConnectionString("Reservations") ?? new NpgsqlConnectionStringBuilder
{
	Host = builder.Configuration["POSTGRES_RESERVATIONS_HOST"] ?? "localhost",
	Port = builder.Configuration.GetValue("POSTGRES_RESERVATIONS_PORT", 5432),
	Database = builder.Configuration["POSTGRES_RESERVATIONS_DBNAME"] ?? "reservations_v4",
	Username = builder.Configuration["POSTGRES_RESERVATIONS_USER"] ?? "postgres",
	Password = builder.Configuration["POSTGRES_RESERVATIONS_PASSWORD"] ?? "postgres",
	MaxAutoPrepare = 20,
	Timeout = 5,
	CommandTimeout = 10
}.ConnectionString;
var connectionSettings = new NpgsqlConnectionStringBuilder(connectionString);
if (!connectionSettings.ShouldSerialize("Max Auto Prepare"))
{
	connectionSettings.MaxAutoPrepare = 20;
}
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionSettings.ConnectionString));
builder.Services.AddSingleton<IReservationStore, PostgresReservationStore>();
builder.Services.AddHttpClient<IAssetsClient, AssetsClient>(client =>
{
	var host = builder.Configuration["ASSETS_API_HOST"] ?? "http://localhost:9090";
	client.BaseAddress = new Uri(builder.Configuration["Assets:BaseUrl"] ?? $"{host.TrimEnd('/')}/api/v3/assets/");
	client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Assets:TimeoutSeconds", 5));
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
	policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:9090"])
		.AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("ETag", "Location");
}));

var app = builder.Build();
app.Use(async (context, next) =>
{
	try
	{
		await next(context);
	}
	catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
	{
		context.Abort();
	}
	catch (Exception exception)
	{
		var error = exception as ApiException;
		app.Logger.Log(error is null ? LogLevel.Error : LogLevel.Debug, exception, "Request failed: {Path}", context.Request.Path);
		context.Response.StatusCode = error?.StatusCode ?? (exception is NpgsqlException ? 503 : 500);
		await context.Response.WriteAsJsonAsync(new { errors = new[] { new { code = error?.Code ?? "schema_violation", message = error?.Message ?? "The service could not process the request." } } }, context.RequestAborted);
	}
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();
foreach (var prefix in new[] { "/api/v4/reservations", "" })
{
	app.MapReservations(prefix);
	app.MapGet($"{prefix}/status", () => TypedResults.Ok(new { authors = builder.Configuration.GetSection("Authors").Get<string[]>() ?? ["TODO"], api_version = "4.0.0" }));
	app.MapGet($"{prefix}/status/auth", () => TypedResults.NoContent()).RequireAuthorization();
	app.MapGet($"{prefix}/health/live", () => TypedResults.Ok(new { live = true }));
	app.MapGet($"{prefix}/health/ready", async (IReservationStore store, IAssetsClient assets, CancellationToken cancellationToken) =>
	{
		var database = await store.IsReadyAsync(cancellationToken);
		var live = await assets.IsLiveAsync(cancellationToken);
		return database && live ? (IResult)TypedResults.Ok(new { ready = true, assets = new { live } })
			: TypedResults.Json(new { errors = new[] { new { code = "room_not_found", message = "A runtime dependency is unavailable." } } }, statusCode: 503);
	});
	app.MapGet($"{prefix}/health", async (IReservationStore store, IAssetsClient assets, CancellationToken cancellationToken) =>
	{
		var database = await store.IsReadyAsync(cancellationToken);
		var live = await assets.IsLiveAsync(cancellationToken);
		return TypedResults.Ok(new { live = true, ready = database && live, assets = new { live } });
	});
}
if (app.Configuration.GetValue("Database:Initialize", true))
{
	await DatabaseInitializer.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
}
app.Run();

public partial class Program;