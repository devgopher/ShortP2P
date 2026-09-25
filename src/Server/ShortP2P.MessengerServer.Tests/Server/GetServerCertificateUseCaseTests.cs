using ShortP2P.MessengerServer.Tests.Fakes;
using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.MessengerServer.UseCases.Server;

namespace ShortP2P.MessengerServer.Tests.Server;

public class GetServerCertificateUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsReaderResult()
    {
        var reader = new FakeServerCertificateReader
        {
            Info = new ServerCertificateInfo("deadbeef", "CN=unit", TestHarness.T0.AddYears(1))
        };

        var info = await new GetServerCertificateUseCase(reader).ExecuteAsync();
        Assert.Equal("deadbeef", info.FingerprintSha256);
        Assert.Equal("CN=unit", info.Subject);
    }
}
