---
version: 1.2.3
section: RvR
---
- **Keep patrols survive a keep level change** (#697). A keep's patrol was checked under one key and kept under another, so
  the next level change (a claim, an upgrade, a capture) made a second patrol of the same keep, which stopped the level
  change part way. The check and the patrol now use one key, and a patrol that is already there keeps its guards. No world
  had patrols yet; this is the fix the frontier keep patrols (#577) need.
