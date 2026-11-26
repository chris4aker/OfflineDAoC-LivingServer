---
version: 1.2.3
section: Tests
---
- **Installer CI's `e2e` job runs on D:** (#680). It had about 4 GB to spare on C: at the end of a run; it now works in
  `D:\lstest`, as the other end-to-end jobs do, and logs free space on every drive at the start and the end of the step.
  CI only; no game or update change.
