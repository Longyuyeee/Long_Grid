using LongGrid.App;

namespace LongGrid.Core.Tests.Runtime;

[Collection(ProductM1ManualEvidenceSessionTestGroup.Name)]
public sealed class ProductM1ManualEvidenceSessionTests
{
    [Fact]
    public async Task AcceptsExactMarker()
    {
        string sessionId = Guid.NewGuid().ToString("N");
        string sessionDirectory = Path.Combine(
            Path.GetTempPath(),
            ProductM1ManualEvidenceSession.SessionDirectoryName,
            sessionId);
        string configurationDirectory = Path.Combine(sessionDirectory, "config");
        string? previousSession = Environment.GetEnvironmentVariable(
            ProductM1ManualEvidenceSession.EnvironmentVariableName);

        Directory.CreateDirectory(configurationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(
                sessionDirectory,
                ProductM1ManualEvidenceSession.MarkerFileName),
            sessionId);

        try
        {
            Environment.SetEnvironmentVariable(
                ProductM1ManualEvidenceSession.EnvironmentVariableName,
                sessionId);

            ProductM1ManualEvidenceSession session = Assert.IsType<
                ProductM1ManualEvidenceSession>(
                ProductM1ManualEvidenceSession.TryCreateFromEnvironment());

            Assert.Equal(Guid.ParseExact(sessionId, "N"), session.SessionId);
            Assert.Equal(sessionDirectory, session.SessionDirectory);
            Assert.Equal(configurationDirectory, session.ConfigurationDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ProductM1ManualEvidenceSession.EnvironmentVariableName,
                previousSession);
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RejectsMarkerWithSurroundingWhitespace()
    {
        string sessionId = Guid.NewGuid().ToString("N");
        string sessionDirectory = Path.Combine(
            Path.GetTempPath(),
            ProductM1ManualEvidenceSession.SessionDirectoryName,
            sessionId);
        string configurationDirectory = Path.Combine(sessionDirectory, "config");
        string? previousSession = Environment.GetEnvironmentVariable(
            ProductM1ManualEvidenceSession.EnvironmentVariableName);

        Directory.CreateDirectory(configurationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(
                sessionDirectory,
                ProductM1ManualEvidenceSession.MarkerFileName),
            $" {sessionId} ");

        try
        {
            Environment.SetEnvironmentVariable(
                ProductM1ManualEvidenceSession.EnvironmentVariableName,
                sessionId);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                ProductM1ManualEvidenceSession.TryCreateFromEnvironment);

            Assert.Equal(
                "M1 manual evidence directory must contain its exact session marker.",
                exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ProductM1ManualEvidenceSession.EnvironmentVariableName,
                previousSession);
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProductM1ManualEvidenceSessionTestGroup
{
    public const string Name = "Product M1 manual evidence session";
}
