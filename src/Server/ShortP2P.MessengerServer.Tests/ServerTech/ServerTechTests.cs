using Microsoft.Extensions.Logging.Abstractions;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.ServerTech;

namespace ShortP2P.MessengerServer.Tests.ServerTech;

public class TotalPowerCalculatorTests
{
    [Fact]
    public void Compute_AtMinimumInputs_ReturnsNearOne()
    {
        var value = TotalPowerCalculator.Compute(new HostHardwareInfo(
            TotalPowerCalculator.MinMhz,
            TotalPowerCalculator.MinCores,
            TotalPowerCalculator.MinRamMb));
        Assert.Equal(1, value, precision: 5);
    }

    [Fact]
    public void Compute_AtMaximumInputs_Returns100()
    {
        var value = TotalPowerCalculator.Compute(new HostHardwareInfo(
            TotalPowerCalculator.MaxMhz,
            TotalPowerCalculator.MaxCores,
            TotalPowerCalculator.MaxRamMb));
        Assert.Equal(100, value, precision: 5);
    }

    [Fact]
    public void Compute_ClampsOutOfRangeInputs()
    {
        var low = TotalPowerCalculator.Compute(new HostHardwareInfo(1, 0, 1));
        var high = TotalPowerCalculator.Compute(new HostHardwareInfo(99999, 999, 999999));
        Assert.Equal(1, low, precision: 5);
        Assert.Equal(100, high, precision: 5);
    }
}

public class FreePowersCalculatorTests
{
    [Fact]
    public void Compute_WhenFullyFree_Returns100()
    {
        Assert.Equal(100, FreePowersCalculator.Compute(new HostLoadInfo(0, 1)), precision: 5);
    }

    [Fact]
    public void Compute_WhenFullyBusy_Returns0()
    {
        Assert.Equal(0, FreePowersCalculator.Compute(new HostLoadInfo(1, 0)), precision: 5);
    }

    [Fact]
    public void Compute_UsesGeometricMeanOfFreeFractions()
    {
        var value = FreePowersCalculator.Compute(new HostLoadInfo(0.75, 0.25));
        Assert.Equal(100 * Math.Sqrt(0.25 * 0.25), value, precision: 5);
    }
}

public class GetHostPowersUseCaseTests
{
    [Fact]
    public async Task GetTotalPower_ReturnsStoredValueWhenValid()
    {
        var clock = new FakeClock(TestHarness.T0);
        var repo = new FakeServerHostPowersRepository
        {
            Current = new ServerHostPowers
            {
                Id = ServerHostPowers.SingletonId,
                TotalPower = 42,
                FreePowers = 33,
                TotalPowerMeasuredAtUtc = TestHarness.T0.AddMinutes(-1),
                FreePowersMeasuredAtUtc = TestHarness.T0.AddMinutes(-2)
            }
        };

        var (value, at) = await new GetTotalPowerUseCase(repo, clock).ExecuteAsync();
        Assert.Equal(42, value);
        Assert.Equal(TestHarness.T0.AddMinutes(-1), at);
    }

    [Fact]
    public async Task GetTotalPower_WhenStoreFails_ReturnsDefaults()
    {
        var clock = new FakeClock(TestHarness.T0);
        var repo = new FakeServerHostPowersRepository { ThrowOnGet = true };

        var (value, at) = await new GetTotalPowerUseCase(repo, clock).ExecuteAsync();
        Assert.Equal(ServerHostPowers.DefaultTotalPower, value);
        Assert.Equal(TestHarness.T0, at);
    }

    [Fact]
    public async Task GetFreePowers_WhenInvalidStored_ReturnsDefaults()
    {
        var clock = new FakeClock(TestHarness.T0);
        var repo = new FakeServerHostPowersRepository
        {
            Current = new ServerHostPowers
            {
                Id = ServerHostPowers.SingletonId,
                TotalPower = 50,
                FreePowers = 200,
                TotalPowerMeasuredAtUtc = TestHarness.T0,
                FreePowersMeasuredAtUtc = TestHarness.T0
            }
        };

        var (value, _) = await new GetFreePowersUseCase(repo, clock).ExecuteAsync();
        Assert.Equal(ServerHostPowers.DefaultFreePowers, value);
    }
}

public class HostPowersMeasurementServiceTests
{
    [Fact]
    public async Task MeasureTotalPower_PersistsComputedValue()
    {
        var clock = new FakeClock(TestHarness.T0);
        var repo = new FakeServerHostPowersRepository
        {
            Current = ServerHostPowers.CreateDefaults(TestHarness.T0.AddHours(-1))
        };
        var hardware = new FakeHostHardwareInfoProvider
        {
            Info = new HostHardwareInfo(TotalPowerCalculator.MaxMhz, TotalPowerCalculator.MaxCores, TotalPowerCalculator.MaxRamMb)
        };
        var load = new FakeHostLoadInfoProvider();
        var service = new HostPowersMeasurementService(
            repo, hardware, load, clock, NullLogger<HostPowersMeasurementService>.Instance);

        await service.MeasureTotalPowerAsync();

        Assert.Equal(100, repo.Current!.TotalPower, precision: 5);
        Assert.Equal(TestHarness.T0, repo.Current.TotalPowerMeasuredAtUtc);
    }

    [Fact]
    public async Task MeasureFreePowers_WhenLoadMissing_UsesDefault()
    {
        var clock = new FakeClock(TestHarness.T0);
        var repo = new FakeServerHostPowersRepository
        {
            Current = new ServerHostPowers
            {
                Id = ServerHostPowers.SingletonId,
                TotalPower = 55,
                FreePowers = ServerHostPowers.DefaultFreePowers,
                TotalPowerMeasuredAtUtc = TestHarness.T0.AddHours(-1),
                FreePowersMeasuredAtUtc = TestHarness.T0.AddHours(-1)
            }
        };
        var service = new HostPowersMeasurementService(
            repo,
            new FakeHostHardwareInfoProvider(),
            new FakeHostLoadInfoProvider { Info = null },
            clock,
            NullLogger<HostPowersMeasurementService>.Instance);

        await service.MeasureFreePowersAsync();

        Assert.Equal(ServerHostPowers.DefaultFreePowers, repo.Current!.FreePowers);
        Assert.Equal(55, repo.Current.TotalPower);
    }
}
