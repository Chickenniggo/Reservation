using System.Text.Json.Serialization;

namespace Reservations.Api;

/// <summary>A reservation with an optional soft-deletion timestamp.</summary>
public sealed record ReservationResponse(
    Guid Id,
    string Name,
    string? Comment,
    DateOnly From,
    DateOnly To,
    [property: JsonPropertyName("room_id")] Guid RoomId,
    [property: JsonPropertyName("deleted_at")] DateTimeOffset? DeletedAt)
{
    [JsonIgnore]
    public Guid Version { get; init; } = Guid.NewGuid();
    [JsonIgnore]
    public string ETag => $"\"{Version:D}\"";
}

public sealed record ReservationFilter(bool IncludeDeleted, Guid? RoomId, DateOnly? Before, DateOnly? After);

public interface IReservationStore
{
    Task<IReadOnlyList<ReservationResponse>> ListAsync(ReservationFilter filter, CancellationToken cancellationToken);
    Task<ReservationResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<(ReservationResponse Reservation, bool Created)> SaveAsync(Guid id, ReservationInput input, bool createOnly, string? ifMatch, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, bool permanent, string? ifMatch, CancellationToken cancellationToken);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}