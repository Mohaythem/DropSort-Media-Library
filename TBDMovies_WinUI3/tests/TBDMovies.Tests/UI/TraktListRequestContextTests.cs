using TBDMovies.UI.Services;
using Xunit;

namespace TBDMovies.Tests.UI;

public sealed class TraktListRequestContextTests
{
    [Fact]
    public void DialogOrPostCannotContinueAfterSelectionAccountCoordinatorOrLifetimeChanges()
    {
        var coordinator = new object();
        var context = new TraktListRequestContext("trakt:list:42", "account-a", coordinator, 3);
        Assert.True(context.IsCurrent(true, "trakt:list:42", "account-a", coordinator, 3, false));
        Assert.False(context.IsCurrent(true, "trakt:list:43", "account-a", coordinator, 3, false));
        Assert.False(context.IsCurrent(true, "custom:42", "account-a", coordinator, 3, false));
        Assert.False(context.IsCurrent(true, "trakt:list:42", "account-b", coordinator, 3, false));
        Assert.False(context.IsCurrent(true, "trakt:list:42", null, coordinator, 3, false));
        Assert.False(context.IsCurrent(true, "trakt:list:42", "account-a", new object(), 3, false));
        Assert.False(context.IsCurrent(true, "trakt:list:42", "account-a", coordinator, 4, false));
        Assert.False(context.IsCurrent(false, "trakt:list:42", "account-a", coordinator, 3, false));
        Assert.False(context.IsCurrent(true, "trakt:list:42", "account-a", coordinator, 3, true));
    }
}
