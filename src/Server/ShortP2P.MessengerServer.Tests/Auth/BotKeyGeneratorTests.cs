using System.Text;
using FluentAssertions;
using ShortP2P.MessengerServer.Auth;

namespace ShortP2P.MessengerServer.Tests.Auth;

public class BotKeyGeneratorTests
{
    private readonly BotKeyGenerator _generator = new();
    
    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(512)]
    
    public void Generate_ReturnsBase64OfRequestedLength(int length)
    {
        var key = _generator.Generate(length);

        key.Should().HaveLength(length);
        
        var checkCorrectnessAction = () => Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
        checkCorrectnessAction.Should().NotThrow();
        
        var bytes = () => Convert.FromBase64String(key);
        bytes.Should().NotThrow().Which.Should().HaveCount(length * 3 / 4);
    }

    [Fact]
    public void Generate_ProducesDistinctKeys()
    {
       _generator.Generate().Should().NotBe(_generator.Generate());
    }
}