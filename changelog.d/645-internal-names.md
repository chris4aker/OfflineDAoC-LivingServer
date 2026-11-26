---
version: 1.2.3
section: Tools
---
- **Internal names follow the new standard** (#645; owner, 2026-11-25: "DAoC-LivingServer should be the name, and
  standard"). The twelve `OfflineDaoc.*` projects, their folders and namespaces, and the server's shared
  `OfflineDaoc.Shared`/`.Configuration` namespaces are now `DAoCLivingServer.*`. The launcher's art files are now
  `DAoC-LivingServer-*`. Names that installed games and older launchers need stay, and each is listed with its reason in
  `docs/NAMING.md`, for example `OfflineDaoc.ProgressImport.exe`, `OfflineDaoc.Patcher.dll` and the #641 compatibility
  names. `tools/release/check_brand.py --internal` (CI's versions job, `tools/check.sh`) fails on a new `OfflineDaoc`
  outside that list.
