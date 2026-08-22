---
name: dropsort-testing
description: Required DropSort safety, failure-path, recovery, parser, and database test scenarios.
---
# DropSort Testing

Use pytest and temporary/sandbox directories only. Never test destructive behavior against real user media.

Mandatory safety scenarios include:
- destination already exists
- source disappears
- permission denied
- destination drive unavailable
- interrupted operation
- database write failure after filesystem success
- same source/destination
- path outside approved roots
- case-insensitive Windows collision
- restart after filesystem success before database commit
- both source and destination exist after interruption
- neither source nor destination exists
- cross-volume fallback
- reverse plan generation for committed Move/Rename
