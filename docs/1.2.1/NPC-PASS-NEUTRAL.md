# NPC pass, phase 2: the shared places (Neutral)

HEROES.md decisions 10 and 11. Only lore files whose realm is Neutral (and `history/neutral`) were touched. Nothing old
was cut: a word-subsequence check of every visible string against the old text finds only stand-ins replaced by a
name ("The imps say" → "Pico says", "Its captains tell" → "Captain Circa ... tells"), the renames below, the agreement
of a verb ("say" → "says") and a full stop turned into a comma where a clause was added.

## What changed

- **The realms' voices below.** Lady Fridwulf of Camelot (cleric trainer) speaks for the Church in three places,
  Sven of Jordheim (skald trainer) keeps Midgard's songs of the dead, and Lasairiona of Tir na Nog (champion trainer)
  sends Hibernia's champions after Ealadhach. Morlin Caan, the smith by the temple, turns up once. Daray was left
  out: the Hibernia writer has Daray at 12 in Hibernian files.
- **The imps get names.** Four of the "The imps say" lines (36 of them in 16 files) now come from the hall imps the
  lore already knows: Pico (the Albion hall's bookmaker and Abdin's wager), Etho (the Hibernia hall's seller of news,
  for candle-ends) and Bolo (who stands at Mystemas's side).
- **Thidranki's captains.** Captain Circa (Albion) and Captain Astridr (Midgard), the period's battleground quest
  captains (Camelot Herald, *Battleground Quest Guide*), each get a habit at their portal keep. Captain Cody
  (Hibernia) was not used, because Cody is a gamebot first name (see "for the owner").
- **Sister Elaydith's runner** now has a name, Siencyn of Camelot, in both of the runner's accounts.
- **Selia**, Tir na Nog's bard trainer, guards the song of the warden who tidies the dead.
- **Facts:** each touched file has an `original` fact naming its new NPCs and their roster source.
- **Continuity ledger:** `sven-names-the-dead`, `siencyn-elaydith-runner`, `tudno-died-at-the-gate`.

Count after this pass, on this branch (bare name): Sven 6, Lasairiona 3, Lady Fridwulf 3, Etho 4, Pico 6, Bolo 6,
Selia 4, Morlin Caan 4, Captain Circa 1, Captain Astridr 1, Siencyn 2.

## The five best moments

1. `locations/darkness-falls`, Jofrid's book page.
   - Before: "Told by Gunnhild the Lame of Mularn, ... to a skald of Jordheim."
   - After: "Told by Jofrid the Lame of Mularn, ... to Sven, a skald of Jordheim, who has sung it since with every name
     of the dead in it and never hers."
2. `bosses/legion`, the rolls of the first breakers.
   - Before: Midgard's roll names Thane Hrolf the Tall, who sang each soul's name as he struck.
   - After, added: "Sven of Jordheim keeps that roll and sings the names Hrolf sang, and leaves out Hrolf's own,
     because Hrolf is still living." And for Hibernia: "Lasairiona of Tir na Nog reads the names of Ealadhach's lost
     wardens to every champion she sends after him, and then asks which of his two descents they would have gone on."
3. `bosses/high-lord-saeor`.
   - Before: "The Church of Camelot teaches that the dead are owed a burial and a prayer, and its priests say that a
     company which loses a man to Saeor has lost more than a man: it has lost the burial..."
   - After: "Lady Fridwulf, who trains the Church of Camelot's clerics, teaches ... and says ... She says the prayer
     over the empty bier all the same, and will not shorten it by a line."
4. `locations/darkness-falls`, the eastern halls.
   - Before: the Albion armourers know a Baelerdoth hauberk by its softened rings.
   - After, added: "and Morlin Caan, the smith by the temple in Jordheim, will not mend one at all, but hangs it on his
     forge wall where the young can see it."
5. `locations/thidranki`, Albion's and Midgard's sides.
   - Before: "Its captains tell Thidranki as the hill where Albion's young learned siegecraft"; Midgard's recruits
     "say it is only good manners."
   - After: "Captain Circa, who sends Albion's young out of the southern portal keep, tells Thidranki as ... Circa has
     every new band lay a hand on the oldest ram's iron before it climbs, and counts the hands again at dusk." And:
     "Captain Astridr looks at the stone below the Midgard gate before a band rides out, and sends back any youth who
     left nothing on it." (Geirlaug's own page has the girl of Mularn who leaves the crust anyway.)

Also: Sven keeps the Grand Summoner's name out of every verse, "because a mask has no name to sing"
(`bosses/grand-summoner-govannon`). Sven's verse of Midgard's first dead is sung "at the southern gates to every warband
bound for Mularn, every name in it" (`locations/darkness-falls`). Etho "will not sell it for less than three
candle-ends" (`bosses/chamberlain`). Pico keeps the imps' book on the Behemoth's death (`bosses/behemoth`). Lady
Fridwulf has each of her clerics read the Job verse aloud before they go down (`bosses/behemoth`).

## Renames (gamebot first names, Neutral-only people)

| Old | New | Files |
|---|---|---|
| Brother Aldric (the first company's chaplain) | Brother Tudno | `locations/darkness-falls`, `bosses/gatekeeper-dommel` |
| Gunnhild the Lame of Mularn | Jofrid the Lame | `locations/darkness-falls`, `bosses/princess-nahemah` |
| Ulfhild Long-Memory, skald of Mularn | Geirlaug Long-Memory | `locations/thidranki` (Lydsyg's Ulfhild is a different woman, left to Midgard) |
| Ottar Svertingsson | Hallgeir Svertingsson | `bosses/high-lord-baln` (Hrefna's grandson Ottar is a different man) |
| Arngrim the rune-cutter | Steinthor the rune-cutter | `locations/proving-fields` (Arngrim is also the berserks' father) |
| Fionnait the harper | Ligach the harper | `locations/proving-fields` |
| Cathal of the long spear | Dúnlang of the long spear | `bosses/duke-bimure` |

Each new name was checked against the gamebot lists (`EraBotNames.cs` first and lore names, the syllable generators
of `AutonomousBotIdentityGenerator.cs` and `BotCharacterGenerator.cs`), the hired companions (`Hire165.cs`), the
legends (`name_counts.legend_names`) and the rest of the lore.

## Summaries

None rewritten or trimmed: no Neutral epic summary sits at the 495-500 padded floor. The longest Neutral epic texts are
stories of 336-403 words.

## For the owner

- **Sven and Cody are gamebot first names** (`EraBotNames.MaleFirstNames`), and so is Gareth (Marshal Gareth stays,
  decision 10). Sven is an approved recurring voice (decision 11), so he is used here. Captain Cody is not.
- **Left to the realm writers** (these names also appear in their realms' files): Vigfus Hallason (Summoner's Hall,
  a Midgard lore name, also in `jordheim`), Sir Edric of Caer Ulfwych (an Albion syllable name, also in
  `tomb-of-mithra`), Loremaster Sigrun (`summoners-hall`).
- The invented-name generator can make almost any short name (Daray, Bolo and Peran fit its patterns), so only the
  written lists and the syllable generators were treated as binding.
