from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum

from dropsort.media.discovery.models import DiscoveryClassification, DiscoveredMedia
from dropsort.media.matcher.models import MatchDecision, MatchStatus
from dropsort.metadata.contracts import MovieCandidate


class ImportProposalStatus(StrEnum):
    MATCH_PROPOSED = "MATCH_PROPOSED"
    REVIEW_REQUIRED = "REVIEW_REQUIRED"
    NO_MATCH = "NO_MATCH"
    METADATA_UNAVAILABLE = "METADATA_UNAVAILABLE"
    ALREADY_IN_LIBRARY = "ALREADY_IN_LIBRARY"
    MANUAL_SELECTION = "MANUAL_SELECTION"


class ImportProposalReason(StrEnum):
    MATCHED_CANDIDATE = "MATCHED_CANDIDATE"
    REVIEW_REQUIRED = "REVIEW_REQUIRED"
    NO_MATCH = "NO_MATCH"
    TV_EPISODE_NOT_SUPPORTED = "TV_EPISODE_NOT_SUPPORTED"
    UNKNOWN_MEDIA = "UNKNOWN_MEDIA"
    DISCOVERY_ERROR = "DISCOVERY_ERROR"
    ALREADY_IN_LIBRARY = "ALREADY_IN_LIBRARY"
    METADATA_UNAVAILABLE = "METADATA_UNAVAILABLE"
    METADATA_AUTHENTICATION = "METADATA_AUTHENTICATION"
    METADATA_RATE_LIMIT = "METADATA_RATE_LIMIT"
    METADATA_RESPONSE_ERROR = "METADATA_RESPONSE_ERROR"
    MANUAL_SELECTION = "MANUAL_SELECTION"


@dataclass(frozen=True, slots=True)
class MovieImportProposal:
    """Informational proposal; never catalog or filesystem authorization."""

    status: ImportProposalStatus
    discovery: DiscoveredMedia
    candidates: tuple[MovieCandidate, ...]
    match_decision: MatchDecision | None
    proposed_candidate: MovieCandidate | None
    reasons: tuple[ImportProposalReason, ...]
    existing_media_file_id: int | None

    def __post_init__(self) -> None:
        if not isinstance(self.status, ImportProposalStatus):
            raise ValueError("status must be ImportProposalStatus")
        if not isinstance(self.discovery, DiscoveredMedia):
            raise ValueError("discovery must be DiscoveredMedia")
        if not isinstance(self.candidates, tuple) or any(
            not isinstance(candidate, MovieCandidate) for candidate in self.candidates
        ):
            raise ValueError("candidates must contain MovieCandidate values")
        if self.match_decision is not None and not isinstance(
            self.match_decision, MatchDecision
        ):
            raise ValueError("match_decision must be MatchDecision or None")
        if self.proposed_candidate is not None and not isinstance(
            self.proposed_candidate, MovieCandidate
        ):
            raise ValueError("proposed_candidate must be MovieCandidate or None")
        if not isinstance(self.reasons, tuple) or not self.reasons or any(
            not isinstance(reason, ImportProposalReason) for reason in self.reasons
        ):
            raise ValueError("reasons must contain ImportProposalReason values")
        if self.existing_media_file_id is not None and (
            isinstance(self.existing_media_file_id, bool)
            or not isinstance(self.existing_media_file_id, int)
            or self.existing_media_file_id <= 0
        ):
            raise ValueError("existing_media_file_id must be a positive integer or None")
        if self.status is ImportProposalStatus.ALREADY_IN_LIBRARY:
            if self.existing_media_file_id is None:
                raise ValueError("ALREADY_IN_LIBRARY requires an existing media-file ID")
        elif self.existing_media_file_id is not None:
            raise ValueError("only ALREADY_IN_LIBRARY can contain an existing media-file ID")
        if self.status in {
            ImportProposalStatus.MATCH_PROPOSED,
            ImportProposalStatus.REVIEW_REQUIRED,
        }:
            if self.discovery.classification is not (
                DiscoveryClassification.MOVIE_CANDIDATE
            ):
                raise ValueError("match proposal requires a movie discovery")
            if self.match_decision is None or self.proposed_candidate is None:
                raise ValueError("match proposal requires a decision and proposed candidate")
            expected_status = {
                ImportProposalStatus.MATCH_PROPOSED: MatchStatus.MATCHED,
                ImportProposalStatus.REVIEW_REQUIRED: MatchStatus.REVIEW_REQUIRED,
            }[self.status]
            if self.match_decision.status is not expected_status:
                raise ValueError("proposal status does not match the match decision")
            if self.proposed_candidate != self.match_decision.candidate:
                raise ValueError("proposed candidate must match the decision candidate")
            if self.proposed_candidate not in self.candidates:
                raise ValueError("proposed candidate must be in proposal candidates")
        elif self.status is ImportProposalStatus.MANUAL_SELECTION:
            if self.discovery.classification is not DiscoveryClassification.MOVIE_CANDIDATE:
                raise ValueError("manual selection requires a movie discovery")
            if not self.candidates:
                raise ValueError("manual selection requires candidates")
            if self.match_decision is not None:
                raise ValueError("manual selection cannot carry an automatic decision")
        elif self.proposed_candidate is not None:
            raise ValueError("this proposal status cannot select a candidate")
        if self.match_decision is not None:
            ranked = tuple(
                score.candidate for score in self.match_decision.ranked_candidates
            )
            if ranked != self.candidates:
                raise ValueError("proposal candidates must preserve decision ranking")
            if (
                self.status is ImportProposalStatus.NO_MATCH
                and self.match_decision.status is not MatchStatus.NO_MATCH
            ):
                raise ValueError("NO_MATCH proposal requires a NO_MATCH decision")
        elif self.candidates and self.status is not ImportProposalStatus.MANUAL_SELECTION:
            raise ValueError("candidates require a match decision")


@dataclass(frozen=True, slots=True)
class ConfirmMovieImportCommand:
    """Explicit caller intent to import one candidate from a prior proposal."""

    proposal: MovieImportProposal
    chosen_candidate: MovieCandidate

    def __post_init__(self) -> None:
        if not isinstance(self.proposal, MovieImportProposal):
            raise ValueError("proposal must be MovieImportProposal")
        if not isinstance(self.chosen_candidate, MovieCandidate):
            raise ValueError("chosen_candidate must be MovieCandidate")
        if self.proposal.status not in {
            ImportProposalStatus.MATCH_PROPOSED,
            ImportProposalStatus.REVIEW_REQUIRED,
            ImportProposalStatus.MANUAL_SELECTION,
        }:
            raise ValueError("proposal is not confirmable")
        if self.proposal.discovery.classification is not (
            DiscoveryClassification.MOVIE_CANDIDATE
        ):
            raise ValueError("only a movie discovery can be confirmed")
        if self.chosen_candidate not in self.proposal.candidates:
            raise ValueError("chosen candidate must be one of the proposal candidates")
