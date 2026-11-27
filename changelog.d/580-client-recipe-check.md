---
version: 1.2.3
section: Tools
---
- **Client recipe check** (#580; tooling, no world change). `source/tools/client_recipe_check.py` compares the recipe
  list the original client's crafting window shows (`tdl.crf`, as atom0s's craft_extract exports it to JSON, SQLite or
  CSV, run separately) with the world's `CraftedItem` rows, by the recipe id the client sends: recipes the world lacks
  or hides (which answer "That recipe cannot be made on this server."), and those whose name, craft, level or realm
  differ. It reads a world copy and the client files read-only; no craft_extract code or client data is in Git
  ([docs/research/CLIENT-RECIPE-CHECK-580.md](docs/research/CLIENT-RECIPE-CHECK-580.md)).
