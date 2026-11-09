using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Reservations.Api;

/// <summary>Input for creating or replacing a reservation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReservationInput
{
    public required string Name { get; init; }
    public string? Comment { get; init; }
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    [JsonPropertyName("room_id")]
    public required Guid RoomId { get; init; }
    [JsonPropertyName("deleted_at")]
    public JsonElement DeletedAt { get; init; }

    public bool Restore => DeletedAt.ValueKind == JsonValueKind.Null;

    public void Validate(bool replacement = false)
    {
        const string word = @"[a-zA-ZäöüÄÖÜßĉĝĥĵŝŭĈĜĤIĴŜŬ0-9-.]+";
        if (Name is null || !Regex.IsMatch(Name, $@"\A{word}( {word})*\z", RegexOptions.None, TimeSpan.FromSeconds(1))
            || RoomId == Guid.Empty || From == default || To == default
            || (Comment is not null && (Comment.Length > 2048
                || !Regex.IsMatch(Comment, $@"\A({word}( {word})*\n?)*\z", RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1))))
            || (DeletedAt.ValueKind != JsonValueKind.Undefined && (!replacement || !Restore)))
        {
            throw new ApiException(400, "schema_violation", "The request does not match the reservation schema.");
        }

        if (From >= To)
        {
            throw new ApiException(400, "invalid_date_range", "from must be strictly before to.");
        }
    }
}

public sealed class ApiException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}