using System.Collections.Concurrent;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.Forwards;

namespace ShortP2P.MessengerServer.UseCases.Hosting;

/// <summary>Process-local ephemeral forward inbox. No DB / no profile persistence.</summary>
public sealed class InMemoryForwardHub : IForwardHub
{
    private readonly ConcurrentDictionary<string, ForwardEnvelope> _byId =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _byTarget =
        new(StringComparer.Ordinal);

    public bool TryAdd(ForwardEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var id = envelope.ForwardId.Trim();
        if (id.Length == 0)
            return false;

        var stored = envelope with
        {
            ForwardId = id,
            SrcNetworkId = envelope.SrcNetworkId.Trim(),
            TgtNetworkId = envelope.TgtNetworkId.Trim()
        };

        if (!_byId.TryAdd(id, stored))
            return false;

        var queue = _byTarget.GetOrAdd(stored.TgtNetworkId, _ => new ConcurrentQueue<string>());
        queue.Enqueue(id);
        return true;
    }

    public IReadOnlyList<ForwardEnvelope> TakeForTarget(string tgtNetworkId, DateTime utcNow)
    {
        var tgt = tgtNetworkId.Trim();
        if (tgt.Length == 0 || !_byTarget.TryGetValue(tgt, out var queue))
            return [];

        var list = new List<ForwardEnvelope>();
        while (queue.TryDequeue(out var id))
        {
            if (!_byId.TryRemove(id, out var envelope))
                continue;

            if (utcNow - envelope.CreatedUtc > ForwardPayloadLimits.ForwardTtl)
                continue;

            list.Add(envelope);
        }

        return list;
    }

    public void PurgeExpired(DateTime utcNow)
    {
        foreach (var kv in _byId)
        {
            if (utcNow - kv.Value.CreatedUtc <= ForwardPayloadLimits.ForwardTtl)
                continue;
            _byId.TryRemove(kv.Key, out _);
        }
    }
}
