using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.ServerTech;

public sealed class GetTotalPowerUseCase(IServerHostPowersRepository repository, IClock clock)
{
    public async Task<(double TotalPower, DateTime MeasuredAtUtc)> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await repository.GetAsync(cancellationToken).ConfigureAwait(false);
            if (double.IsFinite(row.TotalPower) && row.TotalPower is >= 1 and <= 100)
                return (row.TotalPower, row.TotalPowerMeasuredAtUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // fall through to defaults
        }

        var now = clock.UtcNow;
        return (ServerHostPowers.DefaultTotalPower, now);
    }
}

public sealed class GetFreePowersUseCase(IServerHostPowersRepository repository, IClock clock)
{
    public async Task<(double FreePowers, DateTime MeasuredAtUtc)> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await repository.GetAsync(cancellationToken).ConfigureAwait(false);
            if (double.IsFinite(row.FreePowers) && row.FreePowers is >= 0 and <= 100)
                return (row.FreePowers, row.FreePowersMeasuredAtUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // fall through to defaults
        }

        var now = clock.UtcNow;
        return (ServerHostPowers.DefaultFreePowers, now);
    }
}
