---
version: 1.2.3
section: Tools
---
- **Building a world from source works again** (#725). `DAoCLivingServer.Setup` read the whole period restore list,
  which has grown camp sections since October, and stopped on its first comment line. It now reads only the 1,290
  archived rows its migration restores, so the migration is the same as before. The installer and the release packages
  were not affected.
