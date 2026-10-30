namespace DropSort.UI.Services;

/// <summary>Selection and expansion of review leaves, independent of controls and persistence.</summary>
public sealed class TvReviewGroupState<T>(IReadOnlyList<T> members)
{
    public IReadOnlyList<T> Members { get; set; } = members;
    public bool IsExpanded { get; set; } = true;
    public bool? Selection(Func<T, bool> eligible, Func<T, bool> selected)
    {
        var selectable = Members.Where(eligible).ToArray();
        if (selectable.Length == 0) return false;
        var count = selectable.Count(selected);
        return count == 0 ? false : count == selectable.Length ? true : null;
    }
    public void Select(bool value, Func<T, bool> eligible, Action<T, bool> setSelected)
    {
        foreach (var member in Members.Where(eligible)) setSelected(member, value);
    }
    public IEnumerable<T> VisibleMembers => IsExpanded ? Members : [];
}
