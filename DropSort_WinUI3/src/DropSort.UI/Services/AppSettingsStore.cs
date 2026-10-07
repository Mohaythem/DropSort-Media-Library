using System.Linq;

namespace DropSort.UI.Services;

/// <summary>
/// The UI's own configuration, stored in the same settings table as the application's keys.
/// <para>
/// These are values the application contracts do not model but the designed pages need: the media roots
/// Add Media scans, the destination root that Organize File moves into, and the user's own lists on My
/// Lists. Reads never throw - an unavailable or unreadable store answers with the default - so a settings
/// failure degrades to "not configured" instead of taking a page down.
/// </para>
/// <para>
/// Media roots are an ordered, de-duplicated list persisted under one key. The first root is the primary
/// root: the default Add Media scan target and the Organize destination. Add / Replace / Remove all edit
/// this list. Changing the configured roots only rewrites settings; it never touches media files. Legacy
/// single movie/show folder settings are imported once so existing installs keep their roots.
/// </para>
/// </summary>
internal static class AppSettingsStore
{
    private const string MediaFoldersKey = "ui_media_folders";
    private const string MovieFolderKey = "ui_movie_folder"; // legacy, imported once
    private const string ShowFolderKey = "ui_show_folder";   // legacy, imported once

    /// <summary>The primary media root: the default Add Media scan target and the Organize destination.</summary>
    public static string? MovieFolder => ConfiguredMediaFolders.FirstOrDefault();

    /// <summary>The second configured root, kept only as an Add Media prefill fallback.</summary>
    public static string? ShowFolder => ConfiguredMediaFolders.Skip(1).FirstOrDefault();

    /// <summary>All configured media roots in order; the first is primary.</summary>
    public static IReadOnlyList<string> ConfiguredMediaFolders => LoadFolders();

    /// <summary>Adds a root. Returns false when the path is blank or already configured (case-insensitive).</summary>
    public static bool AddMediaFolder(string path)
    {
        var normalized = path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var folders = LoadFolders();
        if (folders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        folders.Add(normalized);
        SaveFolders(folders);
        return true;
    }

    /// <summary>
    /// Replaces one configured root with another, preserving its position. Returns false when the old root
    /// is not configured, the new path is blank, or the new path already exists elsewhere in the list. A
    /// replace is never treated as an add: the list count does not grow.
    /// </summary>
    public static bool ReplaceMediaFolder(string oldPath, string newPath)
    {
        var oldNormalized = oldPath?.Trim() ?? string.Empty;
        var newNormalized = newPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newNormalized))
        {
            return false;
        }

        var folders = LoadFolders();
        var index = folders.FindIndex(f => string.Equals(f, oldNormalized, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        // A no-op replace (same path) is allowed; a replace onto a different existing root is rejected so
        // the list cannot silently collapse two entries into one.
        if (folders.Any(f => string.Equals(f, newNormalized, StringComparison.OrdinalIgnoreCase))
            && !string.Equals(folders[index], newNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        folders[index] = newNormalized;
        SaveFolders(folders);
        return true;
    }

    /// <summary>Removes a configured root. Returns false when it was not configured. Media files are untouched.</summary>
    public static bool RemoveMediaFolder(string path)
    {
        var normalized = path?.Trim() ?? string.Empty;
        var folders = LoadFolders();
        var removed = folders.RemoveAll(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            SaveFolders(folders);
        }
        return removed;
    }

    private static List<string> LoadFolders()
    {
        var raw = Read(MediaFoldersKey);
        if (raw is not null)
        {
            return Normalize(raw.Split('\n'));
        }

        // One-time in-memory import of the legacy movie/show folder settings; persisted on first mutation.
        return Normalize([Read(MovieFolderKey), Read(ShowFolderKey)]);
    }

    private static List<string> Normalize(IEnumerable<string?> paths) =>
        paths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void SaveFolders(IReadOnlyList<string> folders)
    {
        // Persist under the list key and retire the legacy keys so they never re-import over the user's edits.
        Write(MediaFoldersKey, folders.Count == 0 ? string.Empty : string.Join("\n", folders));
        Write(MovieFolderKey, null);
        Write(ShowFolderKey, null);
    }

    private static string? Read(string key)
    {
        try
        {
            return AppServices.IsAvailable ? AppServices.SettingsStore.Get(key) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Write(string key, string? value)
    {
        if (!AppServices.IsAvailable)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                AppServices.SettingsStore.Delete(key);
            }
            else
            {
                AppServices.SettingsStore.Set(key, value, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception)
        {
            // A settings write failure must not take the page down; the value stays unset.
        }
    }
}
