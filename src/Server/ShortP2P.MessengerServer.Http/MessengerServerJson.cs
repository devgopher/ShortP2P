using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShortP2P.MessengerServer.Contracts.Dtos;

namespace ShortP2P.MessengerServer.Http;

internal static class MessengerServerJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static HttpContent ToJsonContent<T>(T value) =>
        JsonContent.Create(value, options: Options);

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        ApiError? error = null;
        string? raw = null;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
                error = JsonSerializer.Deserialize<ApiError>(raw, Options);
        }
        catch
        {
            // Fall through to status-based exception.
        }

        if (error is { Code: not null, Message: not null })
            throw MessengerServerApiException.FromApiError(response.StatusCode, error);

        throw MessengerServerApiException.FromStatus(response.StatusCode, raw);
    }

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var value = await response.Content
            .ReadFromJsonAsync<T>(Options, cancellationToken)
            .ConfigureAwait(false);

        return value ?? throw new MessengerServerApiException(
            response.StatusCode,
            null,
            $"Messenger server returned an empty {typeof(T).Name} body.");
    }
}
