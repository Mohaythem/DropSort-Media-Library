using Microsoft.UI.Xaml;

namespace DropSort.UI.Resources;

/// <summary>
/// Code-behind for the shared card dictionary. It exists only so the templates can use compiled
/// {x:Bind} bindings: the XAML compiler supports x:Bind inside a ResourceDictionary only when that
/// dictionary has an x:Class.
/// </summary>
public sealed partial class MediaTemplates : ResourceDictionary
{
    public MediaTemplates() => InitializeComponent();
}
