# Existing projects and notices

The current server and navigation-builder roots contain GPL version 3 license
texts. Preserve these and all per-file notices; dependencies may have other licenses.
The project's AI-developed additions do not remove upstream attribution.

- Dawn of Light / OpenDAoC: server emulator foundation. See source/server/LICENSE,
  gpl.txt, gplheader.txt and source headers.
- Navigation builder: see source/development-tools/OpenDAoC-BuildNav/LICENSE and
  the native pathing dependencies' notices.
- Asset tool: see tools/asset-tool/THIRD PARTY NOTICES.
- Quest Guide walkthroughs (runtime/server/classic-quest-guides.json): the Allakhazam
  (camelot.allakhazam.com) Dark Age of Camelot quest pages as they read in 2001-2004, most taken from
  the Internet Archive's Wayback Machine copies. They were written by Allakhazam and its contributors,
  who keep their rights; each guide names its source page and capture date. Monster placement and
  levels also draw on period player references: Allakhazam, CapnBry's DAoC radar sightings and the
  Uthgard bestiary.
- New client (0.36c): Godot Engine 4.7.2 with GodotSharp (MIT; Copyright (c) 2014-present Godot
  Engine contributors, (c) 2007-2014 Juan Linietsky, Ariel Manzur). The engine and its bundled
  components' notices ship in new-client/tools/godot_net/LICENSE.txt.
- New client content pipeline: nif.xml from the NifTools project (github.com/niftools/nifxml,
  GPL-3.0), in source/new-client/pipeline/spec. Real-ESRGAN (xinntao/Real-ESRGAN, BSD 3-Clause)
  was used to upscale the converted textures 4x; it is not included.
- The new client's converted zones, models, textures, sounds and interface art are derived from the
  original DAoC client's files, like the original client itself; they are not authored by this
  project.
- Boss script fixes, raid play, camp rules and the Darkness Falls door recount listed under 1.2.1 in
  CHANGELOG-LIVING-SERVER.md: ported from shadowofze's
  [OfflineDAoC-True165](https://github.com/zomboden/OfflineDAoC-True165) (v0.7, GPL-3.0). Each commit keeps him as
  its author and names the commit it came from.
- Additional launcher/portal snapshots and bundled SDK/native/Python packages:
  retain their own licenses and notices; inclusion is not a blanket relicense.

The original DAoC client, art, sound, maps and other third-party assets are not
represented as authored by this project, and no original game-client source tree
was located alongside the customization code. Being noncommercial does not itself
change third-party rights. The GPL server license is not a license for those assets.
