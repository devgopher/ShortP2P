using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Ephemeral in-RAM forward entry (never persisted).</summary>
public sealed record ForwardEnvelope(
    string ForwardId,
    string SrcNetworkId,
    string TgtNetworkId,
    ForwardKind Kind,
    string PayloadBase64,
    DateTime CreatedUtc);

/// <summary>In-memory peer-profile forward hub (dumb pipe, TTL eviction).</summary>
public interface IForwardHub
{
    /// <summary>Adds a forward. Returns false if <paramref name="envelope"/>.ForwardId already exists.</summary>
    bool TryAdd(ForwardEnvelope envelope);

    /// <summary>
    /// Takes all non-expired forwards for <paramref name="tgtNetworkId"/> and removes them from RAM.
    /// </summary>
    IReadOnlyList<ForwardEnvelope> TakeForTarget(string tgtNetworkId, DateTime utcNow);

    /// <summary>Drops expired entries (best-effort sweep).</summary>
    void PurgeExpired(DateTime utcNow);
}
