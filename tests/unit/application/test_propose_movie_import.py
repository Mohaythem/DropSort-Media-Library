from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

import pytest

from dropsort.application.dto.movie_import import (
    ImportProposalReason,
    ImportProposalStatus,
)
from dropsort.application.use_cases import ProposeMovieImport
from dropsort.library.movies import MediaFile
from dropsort.media.discovery import (
    DiscoveryClassification,
    DiscoveryErrorCode,
    DiscoveryIssue,
    DiscoveredMedia,
)
from dropsort.media.matcher import MatchStatus, MovieMatcher
from dropsort.media.parser import MediaType, ParsedMedia, parse_media_filename
from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataProvider,
    MetadataRateLimitError,
    MetadataResponseError,
    MetadataUnavailableError,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)


class FakeProvider:
    provider_name = "tmdb"

    def __init__(
        self,
        candidates: tuple[MovieCandidate, ...] = (),
        error: Exception | None = None,
    ) -> None:
        self.candidates = candidates
        self.error = error
        self.searches: list[MovieSearchQuery] = []

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        self.searches.append(query)
        if self.error is not None:
            raise self.error
        return self.candidates

    def get_movie(self, external_id: str) -> MovieMetadata:
        raise AssertionError("proposal generation must not load details")


class QueryAwareProvider(FakeProvider):
    def __init__(
        self,
        responses: dict[MovieSearchQuery, tuple[MovieCandidate, ...]],
        *,
        error_on: MovieSearchQuery | None = None,
    ) -> None:
        super().__init__()
        self.responses = responses
        self.error_on = error_on

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        self.searches.append(query)
        if query == self.error_on:
            raise MetadataUnavailableError("offline")
        return self.responses.get(query, ())


class FakeLookup:
    def __init__(self, existing: MediaFile | None = None) -> None:
        self.existing = existing
        self.paths: list[Path] = []

    def get_by_path(self, path: Path) -> MediaFile | None:
        self.paths.append(path)
        return self.existing


def _candidate(
    title: str = "The Dark Knight",
    year: int | None = 2008,
    external_id: str = "155",
) -> MovieCandidate:
    return MovieCandidate(
        provider="tmdb",
        external_id=external_id,
        title=title,
        original_title=None,
        year=year,
        overview=None,
        rating=None,
        poster_reference=None,
    )


def _item(
    tmp_path: Path,
    *,
    media_type: MediaType = MediaType.MOVIE,
    title: str | None = "The Dark Knight",
    year: int | None = 2008,
) -> DiscoveredMedia:
    parsed = ParsedMedia(
        original_name="item.mkv",
        media_type=media_type,
        title=title,
        year=year,
        resolution="1080p",
        source="BluRay",
        codec="x264",
        extension=".mkv",
    )
    classification = {
        MediaType.MOVIE: DiscoveryClassification.MOVIE_CANDIDATE,
        MediaType.TV_EPISODE: DiscoveryClassification.TV_EPISODE_SKIPPED,
        MediaType.UNKNOWN: DiscoveryClassification.UNKNOWN_MEDIA,
    }[media_type]
    return DiscoveredMedia(
        (tmp_path / "item.mkv").absolute(),
        100,
        parsed,
        classification,
        None,
    )


def _named_item(
    tmp_path: Path,
    *,
    original_name: str,
    title: str,
    year: int | None,
) -> DiscoveredMedia:
    parsed = ParsedMedia(
        original_name=original_name,
        media_type=MediaType.MOVIE,
        title=title,
        year=year,
        resolution="1080p",
        source="BluRay",
        codec="x265",
        extension=Path(original_name).suffix.casefold(),
    )
    return DiscoveredMedia(
        (tmp_path / original_name).absolute(),
        100,
        parsed,
        DiscoveryClassification.MOVIE_CANDIDATE,
        None,
    )


def _use_case(provider: MetadataProvider, lookup: FakeLookup | None = None):
    return ProposeMovieImport(provider, MovieMatcher(), lookup or FakeLookup())


def test_exact_candidate_returns_match_proposal_without_loading_details(tmp_path: Path) -> None:
    provider = FakeProvider((_candidate(),))

    proposal = _use_case(provider).execute(_item(tmp_path))

    assert proposal.status is ImportProposalStatus.MATCH_PROPOSED
    assert proposal.proposed_candidate == _candidate()
    assert proposal.match_decision.status is MatchStatus.MATCHED  # type: ignore[union-attr]
    assert provider.searches == [MovieSearchQuery("The Dark Knight", 2008)]


def test_invalid_discovery_input_is_rejected(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="discovery"):
        _use_case(FakeProvider()).execute(object())  # type: ignore[arg-type]


def test_movie_without_parsed_title_does_not_call_provider(tmp_path: Path) -> None:
    provider = FakeProvider((_candidate(),))

    proposal = _use_case(provider).execute(_item(tmp_path, title=None))

    assert proposal.status is ImportProposalStatus.NO_MATCH
    assert provider.searches == []


def test_multiple_remakes_are_ranked_and_correct_year_is_proposed(tmp_path: Path) -> None:
    candidates = (
        _candidate("The Thing", 2011, "2"),
        _candidate("The Thing", 1982, "1"),
    )

    proposal = _use_case(FakeProvider(candidates)).execute(
        _item(tmp_path, title="The Thing", year=1982)
    )

    assert proposal.status is ImportProposalStatus.MATCH_PROPOSED
    assert proposal.proposed_candidate.external_id == "1"  # type: ignore[union-attr]
    assert [item.external_id for item in proposal.candidates] == ["1", "2"]


def test_ambiguous_candidates_require_review(tmp_path: Path) -> None:
    candidates = (
        _candidate("The Thing", 1982, "1"),
        _candidate("The Thing", 2011, "2"),
    )

    proposal = _use_case(FakeProvider(candidates)).execute(
        _item(tmp_path, title="The Thing", year=None)
    )

    assert proposal.status is ImportProposalStatus.REVIEW_REQUIRED
    assert ImportProposalReason.REVIEW_REQUIRED in proposal.reasons


def test_empty_or_weak_results_return_no_match(tmp_path: Path) -> None:
    empty = _use_case(FakeProvider()).execute(_item(tmp_path))
    weak = _use_case(FakeProvider((_candidate("Interstate", 2008),))).execute(
        _item(tmp_path, title="Interstellar", year=2008)
    )

    assert empty.status is ImportProposalStatus.NO_MATCH
    assert weak.status is ImportProposalStatus.NO_MATCH


@pytest.mark.parametrize(
    ("error", "reason"),
    (
        (MetadataUnavailableError("offline"), ImportProposalReason.METADATA_UNAVAILABLE),
        (MetadataAuthenticationError("auth"), ImportProposalReason.METADATA_AUTHENTICATION),
        (MetadataRateLimitError("rate"), ImportProposalReason.METADATA_RATE_LIMIT),
        (MetadataResponseError("bad"), ImportProposalReason.METADATA_RESPONSE_ERROR),
    ),
)
def test_metadata_failures_are_controlled_proposals(
    tmp_path: Path,
    error: Exception,
    reason: ImportProposalReason,
) -> None:
    proposal = _use_case(FakeProvider(error=error)).execute(_item(tmp_path))

    assert proposal.status is ImportProposalStatus.METADATA_UNAVAILABLE
    assert proposal.reasons == (reason,)


def test_cross_provider_candidate_is_a_controlled_response_failure(tmp_path: Path) -> None:
    candidate = MovieCandidate("other", "1", "Movie", None, 2024, None, None, None)

    proposal = _use_case(FakeProvider((candidate,))).execute(
        _item(tmp_path, title="Movie", year=2024)
    )

    assert proposal.status is ImportProposalStatus.METADATA_UNAVAILABLE
    assert proposal.reasons == (ImportProposalReason.METADATA_RESPONSE_ERROR,)


@pytest.mark.parametrize(
    ("item", "reason"),
    (
        (MediaType.TV_EPISODE, ImportProposalReason.TV_EPISODE_NOT_SUPPORTED),
        (MediaType.UNKNOWN, ImportProposalReason.UNKNOWN_MEDIA),
    ),
)
def test_non_movies_never_call_provider(
    tmp_path: Path,
    item: MediaType,
    reason: ImportProposalReason,
) -> None:
    provider = FakeProvider((_candidate(),))

    proposal = _use_case(provider).execute(_item(tmp_path, media_type=item, title=None, year=None))

    assert proposal.status is ImportProposalStatus.NO_MATCH
    assert proposal.reasons == (reason,)
    assert provider.searches == []


def test_discovery_error_never_calls_provider(tmp_path: Path) -> None:
    provider = FakeProvider((_candidate(),))
    discovery = DiscoveredMedia.error(
        (tmp_path / "broken.mkv").absolute(),
        DiscoveryIssue(DiscoveryErrorCode.STAT_FAILED, "failed"),
    )

    proposal = _use_case(provider).execute(discovery)

    assert proposal.status is ImportProposalStatus.NO_MATCH
    assert proposal.reasons == (ImportProposalReason.DISCOVERY_ERROR,)
    assert provider.searches == []


def test_existing_catalog_path_short_circuits_provider(tmp_path: Path) -> None:
    existing = SimpleNamespace(id=7)
    lookup = FakeLookup(existing=existing)  # type: ignore[arg-type]
    provider = FakeProvider((_candidate(),))

    proposal = _use_case(provider, lookup).execute(_item(tmp_path))

    assert proposal.status is ImportProposalStatus.ALREADY_IN_LIBRARY
    assert proposal.existing_media_file_id == 7
    assert provider.searches == []


def test_domain_prefix_and_bracketed_release_noise_use_bounded_clean_fallback(
    tmp_path: Path,
) -> None:
    cleaned = MovieSearchQuery("Kaze Tachinu", None)
    provider = QueryAwareProvider(
        {
            cleaned: (
                _candidate("Kaze Tachinu", 2013, "37797"),
                _candidate("Kaze Tachinu", 2013, "37797"),
            )
        }
    )
    filename = "AnimeSanka.com Kaze Tachinu [Bluray - 1080p - Ar - x265].mp4"
    parsed = parse_media_filename(filename)
    discovery = DiscoveredMedia(
        (tmp_path / filename).absolute(),
        100,
        parsed,
        DiscoveryClassification.MOVIE_CANDIDATE,
        None,
    )

    proposal = _use_case(provider).execute(discovery)

    assert parsed.title == "Kaze Tachinu"
    assert provider.searches[0] == cleaned
    assert len(provider.searches) <= 4
    assert len(set(provider.searches)) == len(provider.searches)
    assert [candidate.external_id for candidate in proposal.candidates] == ["37797"]
    assert proposal.status is ImportProposalStatus.MATCH_PROPOSED


@pytest.mark.parametrize(
    ("original_name", "parsed_title", "clean_title", "year", "candidate_title"),
    (
        (
            "Fight Club 10th Anniversary Edition 1999 1080p BluRay x265.mkv",
            "Fight Club 10th Anniversary Edition",
            "Fight Club",
            1999,
            "Fight Club",
        ),
        (
            "Fight Club 10th Anniversary Edition 1080p BluRay x265.mkv",
            "Fight Club 10th Anniversary Edition",
            "Fight Club",
            None,
            "Fight Club",
        ),
    ),
)
def test_edition_suffix_fallback_is_year_aware_and_yearless(
    tmp_path: Path,
    original_name: str,
    parsed_title: str,
    clean_title: str,
    year: int | None,
    candidate_title: str,
) -> None:
    query = MovieSearchQuery(clean_title, year)
    provider = QueryAwareProvider(
        {query: (_candidate(candidate_title, 1999, "550"),)}
    )

    proposal = _use_case(provider).execute(
        _named_item(
            tmp_path,
            original_name=original_name,
            title=parsed_title,
            year=year,
        )
    )

    assert query in provider.searches
    assert proposal.proposed_candidate is not None
    assert proposal.proposed_candidate.external_id == "550"


@pytest.mark.parametrize(
    ("title", "year"),
    (("1917", 2019), ("2001 A Space Odyssey", 1968), ("Blade Runner 2049", 2017)),
)
def test_numeric_titles_are_not_cleaned_as_release_noise(
    tmp_path: Path,
    title: str,
    year: int,
) -> None:
    provider = FakeProvider((_candidate(title, year, "numeric"),))

    proposal = _use_case(provider).execute(
        _named_item(
            tmp_path,
            original_name=f"{title}.{year}.1080p.mkv",
            title=title,
            year=year,
        )
    )

    assert provider.searches[0] == MovieSearchQuery(title, year)
    assert proposal.status is ImportProposalStatus.MATCH_PROPOSED


def test_fallback_provider_failure_is_not_reported_as_zero_results(tmp_path: Path) -> None:
    primary = MovieSearchQuery("Fight Club 10th Anniversary Edition", 1999)
    fallback = MovieSearchQuery("Fight Club", 1999)
    provider = QueryAwareProvider({}, error_on=fallback)
    discovery = _named_item(
        tmp_path,
        original_name="Fight Club 10th Anniversary Edition 1999 1080p.mkv",
        title=primary.title,
        year=1999,
    )

    proposal = _use_case(provider).execute(discovery)

    assert provider.searches[:2] == [primary, fallback]
    assert proposal.status is ImportProposalStatus.METADATA_UNAVAILABLE
    assert proposal.reasons == (ImportProposalReason.METADATA_UNAVAILABLE,)


def test_session_failure_shortcut_preserves_nonmovie_existing_and_unavailable_states(
    tmp_path: Path,
) -> None:
    reason = ImportProposalReason.METADATA_RATE_LIMIT
    provider = FakeProvider()
    use_case = _use_case(provider)

    unavailable = use_case.after_provider_failure(_item(tmp_path), reason)
    television = use_case.after_provider_failure(
        _item(tmp_path, media_type=MediaType.TV_EPISODE, title=None, year=None),
        reason,
    )
    existing = _use_case(provider, FakeLookup(SimpleNamespace(id=8))).after_provider_failure(
        _item(tmp_path),
        reason,
    )

    assert unavailable.status is ImportProposalStatus.METADATA_UNAVAILABLE
    assert unavailable.reasons == (reason,)
    assert television.reasons == (ImportProposalReason.TV_EPISODE_NOT_SUPPORTED,)
    assert existing.status is ImportProposalStatus.ALREADY_IN_LIBRARY
    assert existing.existing_media_file_id == 8
    assert provider.searches == []


def test_session_failure_shortcut_validates_input_and_reason(tmp_path: Path) -> None:
    use_case = _use_case(FakeProvider())

    with pytest.raises(ValueError, match="discovery"):
        use_case.after_provider_failure(object(), ImportProposalReason.METADATA_UNAVAILABLE)  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="session-wide"):
        use_case.after_provider_failure(_item(tmp_path), ImportProposalReason.NO_MATCH)
