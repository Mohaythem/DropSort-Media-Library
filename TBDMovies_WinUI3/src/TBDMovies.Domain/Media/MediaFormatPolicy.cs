using System;
using System.Collections.Generic;
using System.IO;

namespace TBDMovies.Domain.Media;

/// <summary>
/// Central policy for supported video extensions and media classification.
/// Whitelists legitimate media container extensions and rejects executables/scripts.
/// </summary>
public static class MediaFormatPolicy
{
    private static readonly HashSet<string> ApprovedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv",
        ".mp4",
        ".avi",
        ".mov",
        ".m4v",
        ".wmv"
    };

    /// <summary>
    /// The set of approved video file extensions, including the leading dot.
    /// </summary>
    public static IReadOnlySet<string> SupportedVideoExtensions => ApprovedExtensions;

    /// <summary>
    /// Checks whether an extension (with leading dot) is an approved video format.
    /// Case-insensitive.
    /// </summary>
    public static bool IsSupportedVideoExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && ApprovedExtensions.Contains(extension);

    /// <summary>
    /// Checks whether the file path has an approved video extension.
    /// Case-insensitive. Rejects null, whitespace, missing extensions, and unapproved extensions.
    /// </summary>
    public static bool IsSupportedVideoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var extension = Path.GetExtension(path);
            return IsSupportedVideoExtension(extension);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
