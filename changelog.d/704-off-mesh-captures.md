---
version: 1.2.3
section: Tests
---
- **The portal 165 and blodfelag route probes run in the navmesh job again** (#704). Both September captures now sit
  just off the 1.2 mesh edge (3 to 9 units), so the approach finder and the corner recovery rightly refused to plan from them. The probes now
  plan from the floor the bot's floor step would move it to, and assert that floor is within 32 units of the capture.
  The approach finder and the corner recovery are unchanged.
