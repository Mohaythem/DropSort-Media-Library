using DropSort.Infrastructure.Security;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class WindowsTmdbCredentialStoreTests
{
    [Fact]
    public void Isolated_profile_round_trip_uses_only_the_scoped_credential_target()
    {
        var profile = Path.Combine(Path.GetTempPath(), "DropSort-TmdbCredentialTest-" + Guid.NewGuid().ToString("N"));
        var store = new WindowsTmdbCredentialStore(profile);
        try
        {
            store.Delete();
            Assert.Null(store.Read());
            store.Write("synthetic-native-token");
            Assert.Equal("synthetic-native-token", store.Read());
        }
        finally
        {
            store.Delete();
        }
        Assert.Null(store.Read());
    }
}
