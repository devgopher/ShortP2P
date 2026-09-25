using ShortP2P.MessengerServer.UseCases;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Tests.Abstractions;

public class StorageAccessTests
{
    [Fact]
    public void EnsureAnyStoreEnabled_WhenBothDisabled_ThrowsValidation()
    {
        var ex = Assert.Throws<UseCaseException>(() =>
            StorageAccess.EnsureAnyStoreEnabled(new MessengerCacheOptions
            {
                CacheEnabled = false,
                RepositoryEnabled = false
            }));
        Assert.Equal("Validation", ex.Code);
    }

    [Fact]
    public async Task TryWriteAsync_ReturnsFalseOnFailure()
    {
        var ok = await StorageAccess.TryWriteAsync(() => throw new InvalidOperationException());
        Assert.False(ok);
    }

    [Fact]
    public async Task TryGetAsync_ReturnsNullOnFailure()
    {
        var value = await StorageAccess.TryGetAsync<string>(() => throw new InvalidOperationException());
        Assert.Null(value);
    }

    [Fact]
    public async Task TryListAsync_ReturnsEmptyOnFailure()
    {
        var list = await StorageAccess.TryListAsync<int>(() => throw new InvalidOperationException());
        Assert.Empty(list);
    }

    [Fact]
    public async Task TryWriteToCacheAsync_WhenUnavailable_ReturnsFalse()
    {
        var wrote = await StorageAccess.TryWriteToCacheAsync(
            cacheEnabled: true,
            isWriteAvailable: () => false,
            addAsync: () => Task.CompletedTask);
        Assert.False(wrote);
    }

    [Fact]
    public async Task TryWriteToCacheAsync_WhenDisabled_ReturnsFalse()
    {
        var wrote = await StorageAccess.TryWriteToCacheAsync(
            cacheEnabled: false,
            isWriteAvailable: () => true,
            addAsync: () => Task.CompletedTask);
        Assert.False(wrote);
    }

    [Fact]
    public async Task TryExecuteAsync_SwallowsNonCancellationFailures()
    {
        await StorageAccess.TryExecuteAsync(() => throw new InvalidOperationException());
    }
}

public class UseCaseExceptionTests
{
    [Theory]
    [InlineData(nameof(UseCaseException.Validation), "Validation")]
    [InlineData(nameof(UseCaseException.Conflict), "Conflict")]
    [InlineData(nameof(UseCaseException.NotFound), "NotFound")]
    [InlineData(nameof(UseCaseException.Unauthorized), "Unauthorized")]
    [InlineData(nameof(UseCaseException.Unavailable), "Unavailable")]
    [InlineData(nameof(UseCaseException.PeerOffline), "PeerOffline")]
    [InlineData(nameof(UseCaseException.PayloadTooLarge), "PayloadTooLarge")]
    public void FactoryMethods_SetStableCodes(string factory, string code)
    {
        var ex = factory switch
        {
            nameof(UseCaseException.Validation) => UseCaseException.Validation("m"),
            nameof(UseCaseException.Conflict) => UseCaseException.Conflict("m"),
            nameof(UseCaseException.NotFound) => UseCaseException.NotFound("m"),
            nameof(UseCaseException.Unauthorized) => UseCaseException.Unauthorized("m"),
            nameof(UseCaseException.Unavailable) => UseCaseException.Unavailable("m"),
            nameof(UseCaseException.PeerOffline) => UseCaseException.PeerOffline("m"),
            nameof(UseCaseException.PayloadTooLarge) => UseCaseException.PayloadTooLarge("m"),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(code, ex.Code);
        Assert.Equal("m", ex.Message);
    }
}
