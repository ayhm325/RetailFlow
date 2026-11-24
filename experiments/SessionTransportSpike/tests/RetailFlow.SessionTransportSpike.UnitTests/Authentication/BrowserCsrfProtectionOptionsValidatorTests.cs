using Microsoft.Extensions.Options;

using RetailFlow.SessionTransportSpike.Authentication;

namespace RetailFlow.SessionTransportSpike.UnitTests.Authentication;

public sealed class BrowserCsrfProtectionOptionsValidatorTests
{
    [Theory]
    [InlineData(
        "Development",
        "http://localhost:5173",
        true)]
    [InlineData(
        "Development",
        "http://127.0.0.1:5173",
        true)]
    [InlineData(
        "Development",
        "https://localhost:5173",
        true)]
    [InlineData(
        "Production",
        "http://localhost:5173",
        false)]
    [InlineData(
        "Production",
        "http://127.0.0.1:5173",
        false)]
    [InlineData(
        "Staging",
        "http://localhost:5173",
        false)]
    [InlineData(
        "Production",
        "https://retailflow.example.com",
        true)]
    [InlineData(
        "Staging",
        "https://retailflow.example.com",
        true)]
    public void Validate_RespectsEnvironmentAndOriginScheme(
        string environmentName,
        string browserOrigin,
        bool expectedSuccess)
    {
        // Arrange
        var environment = new TestHostEnvironment(environmentName);

        var validator =
            new BrowserCsrfProtectionOptionsValidator(environment);

        var options = new BrowserCsrfProtectionOptions
        {
            BrowserOrigin = browserOrigin
        };

        // Act
        var result = validator.Validate(
            Options.DefaultName,
            options);

        // Assert
        Assert.Equal(expectedSuccess, result.Succeeded);
    }
}
