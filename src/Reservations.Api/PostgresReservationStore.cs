using Npgsql;
using NpgsqlTypes;

namespace Reservations.Api;

public sealed class PostgresReservationStore(NpgsqlDataSource dataSource) : IReservationStore
{
    private const string Columns = "id, name, comment, \"from\", \"to\", room_id, deleted_at, version";

    public async Task<IReadOnlyList<ReservationResponse>> ListAsync(ReservationFilter filter, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns} FROM reservations
            WHERE (@deleted OR deleted_at IS NULL)
              AND (@room IS NULL OR room_id = @room)
              AND (@before IS NULL OR "from" <= @before)
              AND (@after IS NULL OR "to" >= @after)
            ORDER BY "from", id
            """);
        command.Parameters.AddWithValue("deleted", filter.IncludeDeleted);
        command.Parameters.AddWithValue("room", NpgsqlDbType.Uuid, (object?)filter.RoomId ?? DBNull.Value);
        command.Parameters.AddWithValue("before", NpgsqlDbType.Date, (object?)filter.Before ?? DBNull.Value);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Date, (object?)filter.After ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var reservations = new List<ReservationResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            reservations.Add(Read(reader));
        }
        return reservations;
    }

    public async Task<ReservationResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM reservations WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<(ReservationResponse Reservation, bool Created)> SaveAsync(Guid id, ReservationInput input, bool createOnly, string? ifMatch, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, id, cancellationToken);
        var current = await ReadLockedAsync(connection, transaction, id, cancellationToken);
        CheckETag(current, ifMatch);
        if (current is not null && createOnly)
        {
            throw new ApiException(409, "overlapping_reservation", "The reservation already exists.");
        }
        if (current?.DeletedAt is not null && !input.Restore)
        {
            throw new ApiException(409, "deleted_reservation", "Set deleted_at to null to restore this reservation.");
        }

        var reservation = new ReservationResponse(id, input.Name, input.Comment, input.From, input.To, input.RoomId, null);
        await using var command = new NpgsqlCommand("""
            INSERT INTO reservations (id, name, comment, "from", "to", room_id, deleted_at, version)
            VALUES (@id, @name, @comment, @from, @to, @room, NULL, @version)
            ON CONFLICT (id) DO UPDATE SET name = @name, comment = @comment, "from" = @from,
                "to" = @to, room_id = @room, deleted_at = NULL, version = @version
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", reservation.Name);
        command.Parameters.AddWithValue("comment", NpgsqlDbType.Text, (object?)reservation.Comment ?? DBNull.Value);
        command.Parameters.AddWithValue("from", reservation.From);
        command.Parameters.AddWithValue("to", reservation.To);
        command.Parameters.AddWithValue("room", reservation.RoomId);
        command.Parameters.AddWithValue("version", reservation.Version);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            throw new ApiException(409, "overlapping_reservation", "The room is already reserved during this interval.");
        }
        return (reservation, current is null);
    }

    public async Task DeleteAsync(Guid id, bool permanent, string? ifMatch, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, id, cancellationToken);
        var current = await ReadLockedAsync(connection, transaction, id, cancellationToken);
        if (current is null)
        {
            throw new ApiException(404, "schema_violation", "Reservation not found.");
        }
        CheckETag(current, ifMatch);
        if (current.DeletedAt is not null && !permanent)
        {
            throw new ApiException(404, "deleted_reservation", "The reservation is already deleted.");
        }
        await using var command = new NpgsqlCommand(permanent
            ? "DELETE FROM reservations WHERE id = @id"
            : "UPDATE reservations SET deleted_at = CURRENT_TIMESTAMP, version = @version WHERE id = @id", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        if (!permanent)
        {
            command.Parameters.AddWithValue("version", Guid.NewGuid());
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1 FROM reservations LIMIT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id, 0))", connection, transaction);
        command.Parameters.AddWithValue("id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ReservationResponse?> ReadLockedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM reservations WHERE id = @id FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static void CheckETag(ReservationResponse? current, string? ifMatch)
    {
        if (ifMatch is not null && (current is null || !ifMatch.Split(',').Select(value => value.Trim()).Any(value => value == "*" || value == current.ETag)))
        {
            throw new ApiException(412, "etag_mismatch", "If-Match does not match the current ETag.");
        }
    }

    private static ReservationResponse Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4), reader.GetGuid(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)) { Version = reader.GetGuid(7) };
}