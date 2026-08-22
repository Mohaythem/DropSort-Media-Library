from __future__ import annotations

from dataclasses import replace
from typing import Protocol

from dropsort.application.dto.movie_import import (
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.library.movies import MediaFileCatalogLookup
from dropsort.media.discovery.models import DiscoveryClassification, DiscoveredMedia
from dropsort.media.matcher.models import MatchDecision, MatchStatus
from dropsort.media.parser.models import ParsedMedia
from dropsort.application.use_cases.movie_search_fallbacks import movie_search_queries
from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataError,
    MetadataProvider,
    MetadataRateLimitError,
    MetadataResponseError,
    MovieCandidate,
)


class MovieMatchEngine(Protocol):
    def match(
        self,
        parsed: ParsedMedia,
        candidates: tuple[MovieCandidate, ...],
    ) -> MatchDecision: ...


class ProposeMovieImport:
    """Create an informational import proposal without catalog writes."""

    def __init__(
        self,
        provider: MetadataProvider,
        matcher: MovieMatchEngine,
        media_files: MediaFileCatalogLookup,
    ) -> None:
        self._provider = provider
        self._matcher = matcher
        self._media_files = media_files

    def execute(self, discovery: DiscoveredMedia) -> MovieImportProposal:
        if not isinstance(discovery, DiscoveredMedia):
            raise ValueError("discovery must be DiscoveredMedia")
        early = _non_movie_proposal(discovery)
        if early is not None:
            return early

        existing = self._media_files.get_by_path(discovery.path)
        if existing is not None:
            return _proposal(
                ImportProposalStatus.ALREADY_IN_LIBRARY,
                discovery,
                reasons=(ImportProposalReason.ALREADY_IN_LIBRARY,),
                existing_media_file_id=existing.id,
            )

        parsed = discovery.parsed_media
        if parsed is None or parsed.title is None:
            return _proposal(
                ImportProposalStatus.NO_MATCH,
                discovery,
                reasons=(ImportProposalReason.UNKNOWN_MEDIA,),
            )
        candidates_by_identity: dict[tuple[str, str], MovieCandidate] = {}
        decision: MatchDecision | None = None
        try:
            for query in movie_search_queries(parsed):
                returned = self._provider.search_movies(query)
                if any(
                    candidate.provider != self._provider.provider_name
                    for candidate in returned
                ):
                    raise MetadataResponseError(
                        "provider returned a candidate for another provider"
                    )
                for candidate in returned:
                    candidates_by_identity.setdefault(
                        (candidate.provider, candidate.external_id),
                        candidate,
                    )
                match_input = replace(parsed, title=query.title)
                decision = self._matcher.match(
                    match_input,
                    tuple(candidates_by_identity.values()),
                )
                if decision.status is not MatchStatus.NO_MATCH:
                    break
        except MetadataError as error:
            return _metadata_failure(discovery, error)

        if decision is None:
            decision = self._matcher.match(parsed, ())
        ranked = tuple(score.candidate for score in decision.ranked_candidates)
        if decision.status is MatchStatus.MATCHED:
            return _proposal(
                ImportProposalStatus.MATCH_PROPOSED,
                discovery,
                candidates=ranked,
                decision=decision,
                proposed_candidate=decision.candidate,
                reasons=(ImportProposalReason.MATCHED_CANDIDATE,),
            )
        if decision.status is MatchStatus.REVIEW_REQUIRED:
            return _proposal(
                ImportProposalStatus.REVIEW_REQUIRED,
                discovery,
                candidates=ranked,
                decision=decision,
                proposed_candidate=decision.candidate,
                reasons=(ImportProposalReason.REVIEW_REQUIRED,),
            )
        return _proposal(
            ImportProposalStatus.NO_MATCH,
            discovery,
            candidates=ranked,
            decision=decision,
            reasons=(ImportProposalReason.NO_MATCH,),
        )

    def after_provider_failure(
        self,
        discovery: DiscoveredMedia,
        reason: ImportProposalReason,
    ) -> MovieImportProposal:
        """Preserve cheap preflight checks while skipping a known-broken provider session."""
        if not isinstance(discovery, DiscoveredMedia):
            raise ValueError("discovery must be DiscoveredMedia")
        if reason not in {
            ImportProposalReason.METADATA_AUTHENTICATION,
            ImportProposalReason.METADATA_RATE_LIMIT,
            ImportProposalReason.METADATA_UNAVAILABLE,
        }:
            raise ValueError("reason must identify a session-wide provider failure")
        early = _non_movie_proposal(discovery)
        if early is not None:
            return early
        existing = self._media_files.get_by_path(discovery.path)
        if existing is not None:
            return _proposal(
                ImportProposalStatus.ALREADY_IN_LIBRARY,
                discovery,
                reasons=(ImportProposalReason.ALREADY_IN_LIBRARY,),
                existing_media_file_id=existing.id,
            )
        return _proposal(
            ImportProposalStatus.METADATA_UNAVAILABLE,
            discovery,
            reasons=(reason,),
        )


def _non_movie_proposal(discovery: DiscoveredMedia) -> MovieImportProposal | None:
    reason = {
        DiscoveryClassification.TV_EPISODE_SKIPPED: (
            ImportProposalReason.TV_EPISODE_NOT_SUPPORTED
        ),
        DiscoveryClassification.UNKNOWN_MEDIA: ImportProposalReason.UNKNOWN_MEDIA,
        DiscoveryClassification.ERROR: ImportProposalReason.DISCOVERY_ERROR,
    }.get(discovery.classification)
    return (
        None
        if reason is None
        else _proposal(
            ImportProposalStatus.NO_MATCH,
            discovery,
            reasons=(reason,),
        )
    )


def _metadata_failure(
    discovery: DiscoveredMedia,
    error: MetadataError,
) -> MovieImportProposal:
    if isinstance(error, MetadataAuthenticationError):
        reason = ImportProposalReason.METADATA_AUTHENTICATION
    elif isinstance(error, MetadataRateLimitError):
        reason = ImportProposalReason.METADATA_RATE_LIMIT
    elif isinstance(error, MetadataResponseError):
        reason = ImportProposalReason.METADATA_RESPONSE_ERROR
    else:
        reason = ImportProposalReason.METADATA_UNAVAILABLE
    return _proposal(
        ImportProposalStatus.METADATA_UNAVAILABLE,
        discovery,
        reasons=(reason,),
    )


def _proposal(
    status: ImportProposalStatus,
    discovery: DiscoveredMedia,
    *,
    candidates: tuple[MovieCandidate, ...] = (),
    decision: MatchDecision | None = None,
    proposed_candidate: MovieCandidate | None = None,
    reasons: tuple[ImportProposalReason, ...],
    existing_media_file_id: int | None = None,
) -> MovieImportProposal:
    return MovieImportProposal(
        status=status,
        discovery=discovery,
        candidates=candidates,
        match_decision=decision,
        proposed_candidate=proposed_candidate,
        reasons=reasons,
        existing_media_file_id=existing_media_file_id,
    )
