---
name: dropsort-review
description: Mandatory pre-merge review checklist for DropSort changes.
---
# DropSort Review

Before merge, ask:
- Can this lose or overwrite a user file?
- Can any path escape approved roots or traverse a link/reparse point?
- Is every filesystem mutation journaled first?
- Is the operation reversible?
- What happens if the app crashes at every mutation boundary?
- Can SQLite and filesystem diverge, and can restart reconcile it?
- Are failure paths tested in temporary directories?
- Does this create unnecessary coupling or infrastructure?
- Is V1 scope still locked?
- For matching work later: can low-confidence results cause automatic movement?

Any credible data-loss path is Critical/Blocker and must be fixed before continuing.
