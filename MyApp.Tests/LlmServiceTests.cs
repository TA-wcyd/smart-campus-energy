using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyApp.Infrastructure.AI;
using Xunit;

namespace MyApp.Tests;

public class LlmServiceTests
{
    [Fact]
    public async Task GenerateResponseAsync_ReturnsExpectedOutput()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"AiSettings:Model", "test-model"}
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        var logger = NullLogger<LlmService>.Instance;
        var service = new LlmService(configuration, logger);

        // Act
        var result = await service.GenerateResponseAsync("Hello World");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Hello World", result);
    }
}
