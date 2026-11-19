# Period encounters (Prima)

The Prima scripted encounters from shadowofze's Offline DAoC True 1.65 (`scripts/true165`, v0.4, GPL-3.0): Cuinn,
Talvor, the Emmisary of Logi, Brungar, Zargin, the panicked syhr'phint, the ixthiar creeper, Celain Eirich, the
Unseelie Envoy convoy, the anglator lures, the scourgin, the Star Lake procession, Arr, Thergus's ogre war, Gormror's
raid and Hamorik's ritual. The files are kept as they are in True 1.65.

Nothing here runs on its own. An encounter starts only on a world row that names its class or brain (a Mob row's
`ClassType` or `Brain`), and a Living Server package never changes world rows. Those rows reach a world through a
world update made with `source/tools/true165_prima_content.py`; see `docs/TRUE165-CONTENT.md`.
