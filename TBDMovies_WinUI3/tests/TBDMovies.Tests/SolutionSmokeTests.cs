using TBDMovies.Domain;
using TBDMovies.Application;
using TBDMovies.Infrastructure.Persistence;
using Xunit;

namespace TBDMovies.Tests;

public class SolutionSmokeTests
{
    [Fact]
    public void DomainAssembly_Loads()
    {
        var name = DomainAssemblyMarker.AssemblyName;
        Assert.Equal("TBDMovies.Domain", name);
    }

    [Fact]
    public void ApplicationAssembly_Loads()
    {
        var name = ApplicationAssemblyMarker.AssemblyName;
        Assert.Equal("TBDMovies.Application", name);
    }

    [Fact]
    public void SqliteInitializes_ReturnsVersion()
    {
        var version = DatabaseBootstrap.VerifySqliteConnection();

        Assert.False(string.IsNullOrWhiteSpace(version));
        // SQLite versions are in "major.minor.patch" format
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }
}
