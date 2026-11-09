using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace Reservations.Api;

public static class ReservationEndpoints
{
    public static void MapReservations(this WebApplication app, string prefix)
    {
        var publicGroup = app.MapGroup($"{prefix}/public-reservations").WithTags("Public reservations");
        publicGroup.MapGet("", (HttpContext context, IReservationStore store, CancellationToken cancellationToken) => ListAsync(context, store, false, cancellationToken));
        publicGroup.MapGet("/{id}", (string id, IReservationStore store, HttpContext context, CancellationToken cancellationToken) => GetAsync(id, store, context, false, cancellationToken));

        var group = app.MapGroup($"{prefix}/reservations").WithTags("Reservations").RequireAuthorization();
        group.MapGet("", (HttpContext context, IReservationStore store, CancellationToken cancellationToken) => ListAsync(context, store, true, cancellationToken));
        group.MapGet("/{id}", (string id, IReservationStore store, HttpContext context, CancellationToken cancellationToken) => GetAsync(id, store, context, true, cancellationToken));
        group.MapPost("", async Task<IResult> (HttpContext context, IReservationStore store, IAssetsClient assets, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var input = await ReadInputAsync(context, false, cancellationToken);
            await assets.RequireRoomAsync(input.RoomId, cancellationToken);
            var result = await store.SaveAsync(Guid.NewGuid(), input, true, null, cancellationToken);
            Audit(loggerFactory, "create", result.Reservation.Id, context.User);
            context.Response.Headers.ETag = result.Reservation.ETag;
            return TypedResults.Created($"{context.Request.Scheme}://{context.Request.Host}{prefix}/reservations/{result.Reservation.Id:D}", result.Reservation);
        }).Accepts<ReservationInput>("application/json").Produces<ReservationResponse>(201);
        group.MapPut("/{id}", async Task<IResult> (string id, HttpContext context, IReservationStore store, IAssetsClient assets, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var reservationId = ParseId(id);
            var input = await ReadInputAsync(context, true, cancellationToken);
            await assets.RequireRoomAsync(input.RoomId, cancellationToken);
            var result = await store.SaveAsync(reservationId, input, false, IfMatch(context), cancellationToken);
            Audit(loggerFactory, input.Restore ? "restore" : "replace", reservationId, context.User);
            context.Response.Headers.ETag = result.Reservation.ETag;
            return result.Created
                ? TypedResults.Created($"{context.Request.Scheme}://{context.Request.Host}{prefix}/reservations/{reservationId:D}", result.Reservation)
                : TypedResults.Ok(result.Reservation);
        }).Accepts<ReservationInput>("application/json").Produces<ReservationResponse>().Produces<ReservationResponse>(201);
        group.MapDelete("/{id}", async Task<IResult> (string id, HttpContext context, IReservationStore store, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var reservationId = ParseId(id);
            var permanent = IsTrue(context.Request.Query["permanent"]);
            await store.DeleteAsync(reservationId, permanent, IfMatch(context), cancellationToken);
            Audit(loggerFactory, permanent ? "delete-permanent" : "delete", reservationId, context.User);
            return TypedResults.NoContent();
        });
    }

    private static async Task<IResult> ListAsync(HttpContext context, IReservationStore store, bool authenticated, CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        var room = query.ContainsKey("room_id") ? ParseId(query["room_id"].ToString()) : (Guid?)null;
        var filter = new ReservationFilter(authenticated && IsTrue(query["include_deleted"]), room,
            ParseDate(query["before"], query.ContainsKey("before")), ParseDate(query["after"], query.ContainsKey("after")));
        return TypedResults.Ok(new { reservations = await store.ListAsync(filter, cancellationToken) });
    }

    private static async Task<IResult> GetAsync(string id, IReservationStore store, HttpContext context, bool authenticated, CancellationToken cancellationToken)
    {
        var reservation = await store.GetAsync(ParseId(id), cancellationToken);
        if (reservation is null || !authenticated && reservation.DeletedAt is not null)
        {
            throw new ApiException(404, "schema_violation", "Reservation not found.");
        }
        if (authenticated)
        {
            context.Response.Headers.ETag = reservation.ETag;
        }
        return TypedResults.Ok(reservation);
    }

    private static async Task<ReservationInput> ReadInputAsync(HttpContext context, bool replacement, CancellationToken cancellationToken)
    {
        if (!context.Request.HasJsonContentType())
        {
            throw new ApiException(415, "schema_violation", "Content-Type must be application/json.");
        }
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new ApiException(400, "invalid_json", "The request body is not valid JSON.");
        }
        using (document)
        {
            try
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || document.RootElement.EnumerateObject().GroupBy(property => property.Name).Any(group => group.Count() > 1)
                    || document.RootElement.TryGetProperty("comment", out var comment) && comment.ValueKind == JsonValueKind.Null)
                {
                    throw new JsonException();
                }
                var input = document.Deserialize<ReservationInput>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false })
                    ?? throw new JsonException();
                input.Validate(replacement);
                return input;
            }
            catch (JsonException)
            {
                throw new ApiException(400, "schema_violation", "The request body does not match the reservation schema.");
            }
        }
    }

    private static Guid ParseId(string value) => Guid.TryParseExact(value, "D", out var id) && value == value.ToLowerInvariant()
        ? id : throw new ApiException(400, "schema_violation", "Expected a lowercase UUID.");

    private static DateOnly? ParseDate(string? value, bool present) => !present ? null
        : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new ApiException(400, "schema_violation", "Expected a date in YYYY-MM-DD format.");

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    private static string? IfMatch(HttpContext context) => context.Request.Headers.TryGetValue("If-Match", out var value) ? value.ToString() : null;

    private static void Audit(ILoggerFactory loggerFactory, string operation, Guid id, ClaimsPrincipal user) =>
        loggerFactory.CreateLogger("Reservations.Audit").LogInformation("Operation {Operation} ObjectType {ObjectType} ObjectId {ObjectId} UserId {UserId}",
            operation, "reservation", id, user.FindFirstValue("sub") ?? "unknown");
}