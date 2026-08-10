using System.Net;
using ShortP2P.MessengerServer.Contracts.Dtos;

namespace ShortP2P.MessengerServer.Http;

/// <summary>Thrown when the messenger server returns a non-success HTTP status.</summary>
public sealed class MessengerServerApiException : Exception
{
    public MessengerServerApiException(
        HttpStatusCode statusCode,
        string? errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary><see cref="ApiError.Code"/> when the body was an <see cref="ApiError"/>.</summary>
    public string? ErrorCode { get; }

    public static MessengerServerApiException FromApiError(HttpStatusCode statusCode, ApiError error) =>
        new(statusCode, error.Code, error.Message);

    public static MessengerServerApiException FromStatus(HttpStatusCode statusCode, string? body) =>
        new(statusCode, null, string.IsNullOrWhiteSpace(body)
            ? $"Messenger server returned {(int)statusCode} {statusCode}."
            : $"Messenger server returned {(int)statusCode} {statusCode}: {body}");
}
