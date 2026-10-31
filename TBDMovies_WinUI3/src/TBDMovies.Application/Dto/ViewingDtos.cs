using System;
using System.Collections.Generic;

namespace TBDMovies.Application.Dto;

public record ActiveViewingItem(
    int? MovieId,
    int? ShowId,
    int? EpisodeId,
    string Title,
    int? Year,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? EpisodeTitle,
    string? Path,
    DateTimeOffset StartedAt,
    string? ArtworkReference,
    string? BackdropReference,
    string? PosterReference);
