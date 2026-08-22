# ADR-0010: Conservative Movie Candidate Matching Policy

**Status:** Accepted for Phase 2C

**Date:** 2026-08-11

## Context

DropSort must rank provider-neutral `MovieCandidate` values against a parser-produced
`ParsedMedia` value. A false positive is more dangerous than a false negative because a later,
separately approved phase may consume the decision while planning organization work.

Phase 2C decisions are informational only:

```text
MATCHED != AUTHORIZED TO MOVE
```

The matcher cannot import file operations, persistence, provider adapters, HTTP, or UI code.

## Decision

Normalize titles with Unicode NFKC normalization, case folding, punctuation-to-space conversion,
and repeated-whitespace removal. Preserve all words, articles, and numbers.

Use title and year evidence only:

- Exact normalized candidate title starts at `0.86`.
- Exact normalized original title starts at `0.82`.
- Exact year adds `0.14`.
- A conflicting year subtracts `0.48`.
- A missing candidate year subtracts `0.08` when the parsed year exists.
- A missing parsed year adds no evidence and no invented year penalty.
- Standard-library `SequenceMatcher` supports ranking, but fuzzy similarity is capped below the
  `0.85` match threshold even when the year is exact.

Central decision thresholds are:

```text
AUTO_MATCH_THRESHOLD = 0.85
REVIEW_THRESHOLD = 0.60
AMBIGUITY_MARGIN = 0.08
```

`AUTO_MATCH_THRESHOLD` means only that the identity evidence is sufficiently strong for an
informational `MATCHED` result. It grants no filesystem authority.

Deduplicate candidates by `(provider, external_id)` and rank deterministically using score followed
by stable provider/candidate fields. A top-two score gap below `0.08` requires review, even when the
top score exceeds the match threshold.

Return stable `MatchReason` identifiers for positive evidence, penalties, ambiguity, missing input,
and threshold outcomes. Do not use rating, popularity, overview, poster, runtime, cast, or director
as identity evidence.

## Consequences

- Exact normalized title plus exact year is the strongest result.
- A unique exact title may match without a parsed year, but multiple same-title releases require
  review.
- Direct year conflicts and weak title similarity do not produce `MATCHED`.
- Strong fuzzy results remain reviewable rather than automatically matched.
- Localized titles can match through an explicitly reported exact `original_title`.
- A future review UI can explain decisions without parsing human prose.
- Any later filesystem authorization layer must be explicitly designed and approved; this decision
  model cannot authorize operations.
