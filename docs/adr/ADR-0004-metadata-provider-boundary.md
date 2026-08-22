# ADR-0004: Metadata Provider Boundary

**Status:** Accepted, implemented in Phase 2B

External movie metadata will be accessed through a provider contract. Provider-specific HTTP response objects must not enter media/core/UI boundaries.

TMDB is the first adapter selected behind this boundary; see ADR-0009. Search results remain
untrusted candidates and cannot authorize matching or filesystem operations.
