namespace ShortP2P.MessengerServer.UseCases;

/// <summary>Application-level failure with a stable error code.</summary>
public sealed class UseCaseException : Exception
{
    public UseCaseException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    public static UseCaseException Validation(string message) => new("Validation", message);

    public static UseCaseException Conflict(string message) => new("Conflict", message);

    public static UseCaseException NotFound(string message) => new("NotFound", message);

    public static UseCaseException Unauthorized(string message) => new("Unauthorized", message);
}
