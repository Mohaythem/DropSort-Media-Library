---
name: dropsort-file-safety
description: Mandatory safety gate for every DropSort filesystem mutation and recovery path.
---
# DropSort File Safety

- Never overwrite an existing destination.
- Never perform an unjournaled media mutation.
- Never operate outside approved roots.
- Validate canonical paths and reject link/reparse traversal.
- Never expose automatic standalone deletion of original media.
- Source removal is allowed only to complete an explicitly journaled Move/Rename after destination verification.
- Create the operation record before execution.
- Verify destination before authoritative database path updates.
- Every Move/Rename must be reversible at core level.
- Interrupted operations must be recoverable after restart.
- Ambiguous states preserve both files; never guess by deleting either copy.
- Cross-volume moves use copy -> durable flush -> verify -> no-overwrite finalize -> source removal.
