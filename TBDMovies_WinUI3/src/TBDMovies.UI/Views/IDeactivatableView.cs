namespace TBDMovies.UI.Views;

/// <summary>
/// Implemented by cached pages that own asynchronous work which must stop when the shell hides them.
/// </summary>
public interface IDeactivatableView
{
    void Deactivate();
}
