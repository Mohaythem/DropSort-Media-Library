using DropSort.Application.Dto;

namespace DropSort.UI.Services;

public enum AddMediaReviewFilter { AllNew, Ready, NeedsAttention }

/// <summary>Presentation facts only. Metadata failure does not change local eligibility.</summary>
public readonly record struct AddMediaReviewFacts(bool LocalEligible, ImportItemStatus Status, bool Selected, bool DiscoveredFile = true)
{
    public bool Registered => Status == ImportItemStatus.AlreadyInLibrary;
    public bool CanAdd => LocalEligible && !Registered;
    public bool Ready => CanAdd && Status is ImportItemStatus.ReadyToAdd or ImportItemStatus.Matched;
    public bool NeedsAttention => !Registered && !Ready && Status != ImportItemStatus.Matching;

    public bool Matches(AddMediaReviewFilter filter) => !Registered && filter switch
    {
        AddMediaReviewFilter.Ready => Ready,
        AddMediaReviewFilter.NeedsAttention => NeedsAttention,
        _ => true,
    };
}

/// <summary>Tracks changed rows in O(1); progress rendering never enumerates the queue.</summary>
public sealed class AddMediaReviewCounts<T> where T : notnull
{
    private readonly Dictionary<T, AddMediaReviewFacts> _facts = [];
    public int NewFiles { get; private set; }
    public int Registered { get; private set; }
    public int Eligible { get; private set; }
    public int Ready { get; private set; }
    public int Attention { get; private set; }
    public int Selected { get; private set; }

    public void Update(T row, AddMediaReviewFacts facts)
    {
        if (_facts.TryGetValue(row, out var previous)) Accumulate(previous, -1);
        _facts[row] = facts;
        Accumulate(facts, 1);
    }

    public void Remove(T row)
    {
        if (_facts.Remove(row, out var previous)) Accumulate(previous, -1);
    }

    public void Clear()
    {
        _facts.Clear();
        NewFiles = Registered = Eligible = Ready = Attention = Selected = 0;
    }

    private void Accumulate(AddMediaReviewFacts facts, int delta)
    {
        if (facts.Registered) Registered += delta;
        else if (facts.DiscoveredFile) NewFiles += delta;
        if (facts.CanAdd) Eligible += delta;
        if (facts.Ready) Ready += delta;
        if (facts.NeedsAttention) Attention += delta;
        if (facts.CanAdd && facts.Selected) Selected += delta;
    }
}
