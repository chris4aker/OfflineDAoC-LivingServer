---
version: 1.2.3
section: Tests
---
- **The portal 165 route probe runs in the navmesh job again** (#704). Its September capture now sits just off the 1.2
  mesh edge (6 to 9 units), so the approach finder rightly refused to plan from it. The probe now plans from the floor the
  bot's floor step would move it to, and asserts that floor is within 32 units of the capture. Blodfelag's probe plans
  the same way and passes, but its class stays out of the job until its next sample (a long region 1 frontier path) is
  read. The approach finder and the corner recovery are unchanged.
