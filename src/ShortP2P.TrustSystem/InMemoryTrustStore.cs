using System.Collections.Concurrent;

namespace ShortP2P.TrustSystem;

public sealed class InMemoryTrustStore : ITrustStore
{
    private readonly ConcurrentDictionary<string, ServerTrustState> _states = new(StringComparer.OrdinalIgnoreCase);

    public Task<ServerTrustState?> GetAsync(ServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_states.TryGetValue(endpoint.Key, out var state) ? state.Clone() : null);
    }

    public Task UpsertAsync(ServerTrustState state, CancellationToken cancellationToken = default)
    {
        Require.NotNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = ServerEndpoint.Parse(state.Host, state.Port);
        state.Host = endpoint.Host;
        state.Port = endpoint.Port;
        _states[endpoint.Key] = state.Clone();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServerTrustState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ServerTrustState> list = _states.Values
            .Select(s => s.Clone())
            .OrderBy(s => s.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Port)
            .ToList();
        return Task.FromResult(list);
    }
}
