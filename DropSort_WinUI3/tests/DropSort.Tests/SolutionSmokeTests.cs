using DropSort.Domain;
using DropSort.Application;
using DropSort.Infrastructure.Persistence;
using Xunit;

namespace DropSort.Tests;

public class SolutionSmokeTests
{
    [Fact]
    public void DomainAssembly_Loads()
    {
        var name = DomainAssemblyMarker.AssemblyName;
        Assert.Equal("DropSort.Domain", name);
    }

    [Fact]
    public void ApplicationAssembly_Loads()
    {
        var name = ApplicationAssemblyMarker.AssemblyName;
        Assert.Equal("DropSort.Application", name);
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
