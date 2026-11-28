---
version: 1.2.3
section: RvR
---
- **Gamebot sieges keep to three engines per door** (#831). Two gamebots of a realm reaching their spots at the same moment could both start building, so a door could have a fourth engine on the way. Starting a build now takes the door's last place in one step, and a finished build holds its place until its engine stands. The limit is still three engines of a realm per door, and your own engines still count toward it.
