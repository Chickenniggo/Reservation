using Npgsql;

namespace Reservations.Api;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("Reservations.Api.Database.sql")
            ?? throw new InvalidOperationException("The database migration is missing.");
        using var reader = new StreamReader(stream);
        await using var command = new NpgsqlCommand(await reader.ReadToEndAsync(cancellationToken), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}