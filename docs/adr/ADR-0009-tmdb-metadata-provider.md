# ADR-0009: TMDB as the First Movie Metadata Adapter

**Status:** Accepted for Phase 2B

**Date:** 2026-08-11

## Context

DropSort needs one provider adapter behind the provider-neutral boundary accepted in ADR-0004.
Phase 2B requires movie title/year search, stable external IDs, normalized candidate and detail
metadata, credits, poster references, controlled HTTP failures, and a local cache. Provider data
remains untrusted candidate data and cannot authorize matching or filesystem operations.

Official documentation reviewed:

- [TMDB movie search](https://developer.themoviedb.org/reference/search-movie)
- [TMDB movie details](https://developer.themoviedb.org/reference/movie-details)
- [TMDB movie credits](https://developer.themoviedb.org/reference/movie-credits)
- [TMDB application authentication](https://developer.themoviedb.org/docs/authentication-application)
- [TMDB rate limiting](https://developer.themoviedb.org/docs/rate-limiting)
- [TMDB API FAQ and attribution](https://developer.themoviedb.org/docs/faq)
- [TMDB API terms](https://www.themoviedb.org/api-terms-of-use)
- [OMDb API parameters](https://www.omdbapi.com/)
- [OMDb terms](https://www.omdbapi.com/legal.htm)

## Options

### TMDB

- Search supports movie title and `primary_release_year` and returns stable numeric movie IDs.
- Search results include title, original title, release date, overview, rating, and poster path.
- Movie details provide genres and runtime; credits are available from a documented movie endpoint
  and can be appended to the detail request.
- Application authentication supports a bearer read-access token over HTTPS.
- The API documents `429` behavior and an approximate upper limit around 40 requests/second that
  may change.
- Non-commercial use requires attribution. Commercial use requires a separate agreement.
- TMDB content must not be cached for longer than six months.

### OMDb

- Search supports title, year, type, stable IMDb IDs, and pagination.
- Detail lookup provides plot, genres, runtime, rating, director, actors, and poster fields through a
  simple API-key request.
- Search responses are less rich, so useful candidate normalization requires additional detail
  calls or intentionally sparse candidates.
- The separately documented Poster API is patron-only.
- The public terms describe personal, non-commercial use restrictions.

## Decision

Use TMDB API v3 as the first adapter. It provides the strongest documented fit for rich candidate
search followed by a single detail request with appended credits, while keeping TMDB IDs, query
parameters, authentication, and raw response dictionaries inside `metadata/providers`.

Use the Python standard library HTTP stack behind an injected transport boundary. No new runtime
dependency is justified for two GET endpoints. Read the bearer credential only from
`DROPSORT_TMDB_READ_ACCESS_TOKEN`; never persist or log it.

Cache normalized JSON, not Python pickle or raw HTTP objects. Search entries expire after one day
and detail entries after seven days, both comfortably inside TMDB's six-month maximum. Expired
entries are never returned as hidden stale data. When refresh fails, the provider error is returned
to the caller; valid unexpired entries remain usable offline.

## Consequences

- DropSort must include the required TMDB attribution if/when a UI is implemented.
- Distribution for commercial use requires an appropriate TMDB commercial agreement.
- The adapter must translate authentication, rate-limit, availability, HTTP, and response failures
  into provider-neutral errors.
- TMDB may change limits or fields, so normalization is strict for required fields and conservative
  for optional fields.
- A future provider can implement the same contracts without changing parser, core, library, or
  cache consumers.
