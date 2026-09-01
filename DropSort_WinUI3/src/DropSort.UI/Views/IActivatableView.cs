namespace DropSort.UI.Views;

/// <summary>
/// Implemented by pages that need to refresh their projected collections every time the shell
/// shows them, so navigating away and back never leaves stale filter or search results on screen.
/// </summary>
public interface IActivatableView
{
    void Activate();
}
