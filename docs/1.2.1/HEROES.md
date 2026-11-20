# The famous heroes of each realm (1.2.1 lore pass)

Owner, 2026-11-20: Dame Ysolde repeated so much is a problem. Each realm gets 2 or 3 famous heroes. None is a bot.
They are legends. Source: the realm's own folklore (owner's choice: "Folklore heroes").

| Realm | Hero | Role in the stories | Myth source (public domain) |
|---|---|---|---|
| Albion | **Sir Galahad** | the pure knight; the quest that cannot be won by arms | Malory, *Le Morte d'Arthur*; the Vulgate *Queste del Saint Graal* |
| Albion | **Sir Gawain** | the sun-strong champion who keeps his word at a cost | *Sir Gawain and the Green Knight*; Malory |
| Albion | **Sir Bedivere** | the last survivor, the one who saw the end and told it | Malory; Geoffrey of Monmouth |
| Midgard | **Sigurd** | the dragon-slayer, the one who knows what a dragon costs | *Volsunga saga*; the Poetic Edda |
| Midgard | **Ragnar Lodbrok** | the raider-king who walked into the pit and sang | *Ragnars saga loðbrókar*; *Krákumál* |
| Midgard | **Hervor** | the shield-maiden who woke the dead for a sword | *Hervarar saga ok Heiðreks* |
| Hibernia | **Cú Chulainn** | the hound of Ulster who held the ford alone | *Táin Bó Cúailnge* |
| Hibernia | **Fionn mac Cumhaill** | the chief of the Fianna, who tasted the salmon of knowledge | the Fenian Cycle |
| Hibernia | **Oisín** | the poet who came back from Tír na nÓg and told it | the Fenian Cycle; *Acallam na Senórach* |

## Rules for the stories

1. **Legends, not witnesses.** The old figures are told about ("the songs say", "Albion's knights still swear that
   Galahad..."). They never sit in a present-day scene as a bot would. No hero is a present-day character.
2. **No hero is mentioned more than 12 times across all the lore** (`tools/lore/name_counts.py` enforces it). Do not put
   the same hero in every story: pick the one the story's shape fits, and leave most stories with none.
3. **The retiring names:** Dame Ysolde, Brother Cadoc, Sir Cador, Brother Odran, Sir Geraint and Captain Rhodri each
   end at 6 mentions or fewer. Where one is the story's witness, replace it with the realm's hero, or with an
   unnamed voice ("a friar of the company", "a sergeant who walked down"). Never leave a story shorter: keep every
   event and detail, change who tells it.
4. **Mark the sourcing.** A hero's own deeds come from the myth source above (provenance `"myth"` with that source). What
   the stories add around them is `"original"`.
5. **Era.** Nothing after 1.65 (8 October 2003).
6. **Dragon stories** lean on the hero who fits: Sigurd for Midgard's dragon, and Galahad or Gawain for Albion's.
   Do not force a hero into a dungeon story where nothing in the plot calls for one.
