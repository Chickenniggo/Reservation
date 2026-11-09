using System.Net;
using System.Text.Json;

namespace Reservations.Api;

public interface IAssetsClient
{
    Task RequireRoomAsync(Guid roomId, CancellationToken cancellationToken);
    Task<bool> IsLiveAsync(CancellationToken cancellationToken);
}

public sealed class AssetsClient(HttpClient httpClient) : IAssetsClient
{
    public async Task RequireRoomAsync(Guid roomId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"rooms/{roomId:D}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ApiException(400, "room_not_found", "The specified room does not exist.");
            }
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            throw new ApiException(503, "room_not_found", "The assets service is unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException(503, "room_not_found", "The assets service timed out.");
        }
    }

    public async Task<bool> IsLiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("health/live", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("live", out var live) && live.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}