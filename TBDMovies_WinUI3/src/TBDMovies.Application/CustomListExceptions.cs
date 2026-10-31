using System;

namespace TBDMovies.Application;

/// <summary>
/// Thrown when a custom list would be created or renamed to a display name that another custom list
/// already uses (compared after trimming, case-insensitively). List identity stays the stable id; this
/// only keeps display names unambiguous. The UI turns this into a localized validation message.
/// </summary>
public sealed class DuplicateCustomListNameException : Exception
{
    public DuplicateCustomListNameException(string name)
        : base($"A custom list named '{name}' already exists.")
    {
        Name = name;
    }

    public string Name { get; }
}
