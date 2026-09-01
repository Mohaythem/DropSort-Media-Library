using System.Text.Json;

namespace DropSort.UI.Services;

/// <summary>
/// The UI's own configuration, stored in the same settings table as the application's keys.
/// <para>
/// These are values the application contracts do not model but the designed pages need: the folders
/// Add Media scans, the destination root that Organize File moves into, and the user's own lists on
/// My Lists. Reads never throw - an unavailable or unreadable store answers with the default - so a
/// settings failure degrades to "not configured" instead of taking a page down.
/// </para>
/// </summary>
internal static class AppSettingsStore
{
    private const string MovieFolderKey = "ui_movie_folder";
    private const string ShowFolderKey = "ui_show_folder";
    private const string CustomListsKey = "ui_custom_lists";

    /// <summary>The approved movies root: what Add Media scans and what Organize File moves into.</summary>
    public static string? MovieFolder
    {
        get => Read(MovieFolderKey);
        set => Write(MovieFolderKey, value);
    }

    /// <summary>The TV folder Add Media scans in episodes mode.</summary>
    public static string? ShowFolder
    {
        get => Read(ShowFolderKey);
        set => Write(ShowFolderKey, value);
    }

    /// <summary>The user's own lists on My Lists, in display order.</summary>
    public static IReadOnlyList<string> CustomLists
    {
        get
        {
            var stored = Read(CustomListsKey);

            if (string.IsNullOrWhiteSpace(stored))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<string[]>(stored) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        set => Write(CustomListsKey, JsonSerializer.Serialize(value ?? []));
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
