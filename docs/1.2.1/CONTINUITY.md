# Continuity (1.2.1)

Written by `python3 tools/lore/continuity.py --write`. The ledger is `tools/lore/continuity.json`: facts the lore
tells in more than one file and the forms each may take. `--check` reads every visible string in each fact's scope
(and the hidden facts' text, marked) and lists the statements that differ. Nothing here rewrites lore; settling a
contradiction follows HEROES.md decision 10 (Mythic's canon first, else the rule of cool, adding words, never
cutting).

**32 ledger facts, 0 contradiction(s).**

## The ledger

| Fact | told as the ledger has it in | contradictions |
|---|---|---|
| **caer-sidi-third-up**: Caer Sidi's third company went up eighty strong. | 5: `apocalypse`, `crypt-lord`, `silencer`, `soul-reckoner`, `caer-sidi` | - |
| **caer-sidi-third-home**: Of the eighty, sixty-seven came home. | 3: `apocalypse`, `soul-reckoner`, `caer-sidi` | - |
| **arthur-seven-came-home**: In the old song Arthur sailed Prydwen to Caer Sidi with three shiploads of men, and seven came home (Arthur and six). | 2: `apocalypse`, `caer-sidi` | - |
| **caer-sidi-crypts-nine**: The third company lost nine in the crypts (the Crypt Lord's court, the first hall). | 4: `apocalypse`, `crypt-lord`, `fates-chosen`, `caer-sidi` | - |
| **caer-sidi-gallery-two**: The third company lost two in Ilron's gallery, to shadows. | 3: `apocalypse`, `fates-chosen`, `lich-lord-ilron` | - |
| **caer-sidi-silencer-two**: Two of the third company fell to the Silencer, in the inner halls. | 5: `apocalypse`, `fates-chosen`, `lich-lord-ilron`, `silencer`, `soul-reckoner` | - |
| **caer-sidi-tally-counts-the-hush**: A text that counts the third company's dead (eighty up, nine in the crypts, sixty-seven home) also counts the Silencer's two: 9 + 2 + 2 = 13. | 2: `apocalypse`, `soul-reckoner` | - |
| **caer-sidi-landing-tally**: At the steward's landing the captain had lost nine in the crypts, two to the hush and two in the gallery. | 1: `fates-chosen` | - |
| **caer-sidi-thirteen-dead**: The third company's dead are the thirteen: nine, two and two. No one else of it dies on the climb. Sir Rhun comes home alive among the sixty-seven, but hollow (owner decision 10, the rule of cool). | 18: `apocalypse`, `bane-of-hope`, `bellum`, `crypt-lord`, `fames`, `fates-chosen` and 12 more | - |
| **caer-sidi-four-riders**: Four riders stand before the end's door: Fames, Bellum, Morbus and Funus. | 7: `apocalypse`, `bellum`, `fames`, `fates-chosen`, `funus`, `morbus` and 1 more | - |
| **dame-nest-eleven**: Dame Nest walked home calling names and found eleven. | 2: `bane-of-hope`, `caer-sidi` | - |
| **gwennol-keel-up**: The Gwennol went over and did not right herself; the boats found her at dusk keel up, the boy still holding the tiller, and the sea kept two men and every sack. | 3: `scurceol-hyrde`, `camelot`, `krondon` | - |
| **host-bodies**: The Host is one soul in eight bodies (one line in the Reckoner's ledger and seven blanks; at the tally, two struck for nothing, the real one, and the other five). | 1: `host` | - |
| **host-blanks**: The Reckoner wrote the Host down once and left seven blanks. | 2: `host`, `soul-reckoner` | - |
| **breanainn-never-climbs-again**: Bréanainn never went back up Tur Suil after the night of Kierac. | 3: `kierac-the-destroyer`, `master-of-pain`, `tur-suil` | - |
| **dyranapur-dark-moons**: Ulla carried the dark-moon cup for thirty-one dark moons; the slate's forty winters are the years she lived after. | 1: `dyranapur` | - |
| **dyranapur-forty-winters-is-after**: No text has Ulla carry the cup forty winters. | 1: `dyranapur` | - |
| **iorwerth-walled-vein**: Iorwerth sang the Quieting in the deepest cellar but never went past the walled vein (the brick). | 3: `draargus-the-mighty`, `xanxicar`, `crystal-cave` | - |
| **iorwerth-forty-years**: Iorwerth sang the Quieting on the longest night for forty years, and lived forty years more at Caer Gothwaite. | 3: `draargus-the-mighty`, `xanxicar`, `crystal-cave` | - |
| **wenoiak-books**: Weno'iak's books are on his observatory shelves; he moves them before any company comes, and leaves ash. | 3: `syssro-the-ruthless`, `wenoiak-the-enlightened`, `avalon-city` | - |
| **wenoiak-household-uncounted**: Weno'iak's household (cuirassiers, reeves, warsteeds) is told without a count. The fight advice (legend) keeps its head counts, as decision 9 keeps head counts in fight advice. | 2: `wenoiak-the-enlightened`, `avalon-city` | - |
| **feast-of-thrones-thirty**: Thirty lords met at the Feast of Thrones. | 4: `duke-satori`, `gatekeeper-dommel`, `grand-chancellor-adremal`, `legion` | - |
| **feast-of-thrones-nineteen**: The Master ate nineteen of them. | 6: `behemoth`, `duke-aypol`, `duke-eligar`, `marquis-almen`, `neutral`, `darkness-falls` | - |
| **feast-of-thrones-eleven**: Eleven knelt, and the court called him Legion. | 7: `behemoth`, `high-lord-baelerdoth`, `high-lord-baln`, `high-lord-saeor`, `legion`, `marquis-valupa` and 1 more | - |
| **darkness-falls-ten-rolls**: Ten Rolls of precedence line the approach; the present one is the Tenth. | 4: `archivist-borath`, `earl-vone`, `grand-chancellor-adremal`, `darkness-falls` | - |
| **darkness-falls-three-doors**: The demons' kingdom has three doors: in Camelot Hills, above Mularn and in Connacht. | 9: `behemoth`, `chamberlain`, `duke-aypol`, `duke-bimure`, `duke-harboris`, `field-marshal-nebir` and 3 more | - |
| **hallbjorn-eighty**: Hallbjorn Ulfsson's warband mustered at Fort Atla eighty strong. | 3: `gjalpinulva`, `malmohus`, `myrkwood-forest` | - |
| **galplen-twenty-seven**: Twenty-seven out of Galplen, who knew Ragnhild's song by heart, killed the dragon twice in one day. | 2: `gjalpinulva`, `myrkwood-forest` | - |
| **vind-ryttaren-attempts**: Asmund of the Vind Ryttaren made seven attempts on the roost and was thrown four times; Hrafn made four. | 3: `hurika`, `torst`, `tuscaran-glacier` | - |
| **tawi-shon-forty-winters**: Tawi Shon kept the boats' fire on the Cliffs of Moher for forty winters. | 2: `tawi-shon`, `cliffs-of-moher` | - |
| **king-mark-duke-marcus**: King Mark is Duke Marcus's son, the son who came home alone from Dartmoor. | 15: `asutri`, `captain-bardalph`, `captain-heathyr`, `clan-matron`, `dagar`, `golestandt` and 9 more | - |
| **arthur-is-dead**: At 1.65 Arthur is dead; his knights are legends of the past and never stand in a present-day scene. | 48: `agidad`, `apocalypse`, `bellum`, `captain-atwell`, `fames`, `funus` and 42 more | - |

## Contradictions found

None.

## Candidates not in the ledger (`--suggest`)

A number and the noun after it, told beside the same name in two or more files (fight advice, bot lines and the
Herald's templates left out). Those with different numbers come first: most are different things (two dukes'
thirty and thirty-one fights), some are worth a ledger entry or a look.

| name | noun | numbers (files) |
|---|---|---|
| Blade | orders | **2**: `chthonic-knights`, `grand-chancellor-adremal`, `mutilators`; **3**: `field-marshal-nebir`, `lieutenants`, `prince-asmoien` |
| Gate | orders | **2**: `chthonic-knights`, `neutral`; **3**: `field-marshal-nebir`, `gatekeeper-dommel`, `high-lord-baelerdoth` +2 |
| Hook | orders | **2**: `chthonic-knights`, `grand-chancellor-adremal`, `mutilators`; **3**: `field-marshal-nebir`, `lieutenants`, `prince-asmoien` |
| Roll | orders | **2**: `chthonic-knights`, `prince-asmoien`, `neutral`; **3**: `duke-bimure`, `gatekeeper-dommel`, `prince-asmoien` +1 |
| Roll | rolls | **3**: `earl-zhalzhak`, `mutilators`; **7**: `duke-harboris`; **10**: `marquis-scottiax`, `darkness-falls` |
| Dommel | orders | **2**: `chthonic-knights`, `prince-asmoien`, `neutral`; **3**: `prince-asmoien`, `neutral`, `darkness-falls` |
| Gatekeeper | orders | **2**: `chthonic-knights`, `prince-asmoien`, `neutral`; **3**: `prince-asmoien`, `neutral`, `darkness-falls` |
| Mutilators | orders | **2**: `chthonic-knights`, `grand-chancellor-adremal`; **3**: `duke-bimure`, `high-lord-baelerdoth` |
| Domnann | names | **3**: `cronwort`, `myrddraxis`; **5**: `spindler-broodmother` |
| Fourth | orders | **2**: `chthonic-knights`, `prince-asmoien`, `neutral`; **3**: `prince-asmoien`, `neutral` |
| Age | fights | **30**: `duke-alloc`; **31**: `duke-eligar` |
| Alun's | companies | **2**: `aidon-the-archwizard`; **6**: `lord-gildas` |
| Baelerdoth's | rolls | **7**: `duke-harboris`; **10**: `marquis-scottiax` |
| Caer | men | **40**: `crystal-cave`; **80**: `lich-lord-ilron` |
| Chancellor | demons | **2**: `inquisitors`; **3**: `duke-sallis` |
| Chthonic | orders | **2**: `grand-chancellor-adremal`; **3**: `duke-bimure` |
| Connacht | names | **4**: `corrupt-primal-of-earth`; **5**: `spindler-broodmother` |
| Dun | keeps | **2**: `mount-collory`; **4**: `marfach-caverns` |
| Eochaidh's | bands | **2**: `balor`; **3**: `tur-suil` |
| Gate's | orders | **2**: `chthonic-knights`; **3**: `duke-bimure` |
| Gothwaite | men | **40**: `crystal-cave`; **80**: `lich-lord-ilron` |
| Howth | doors | **2**: `coruscating-mine`; **4**: `sheeroe-hills` |
| Knights | orders | **2**: `grand-chancellor-adremal`; **3**: `duke-bimure` |
| Master's | rolls | **6**: `duke-satori`; **10**: `darkness-falls` |
| Pennine | summers | **3**: `hall-of-the-corrupt`; **4**: `lieutenant-meade` |
| Queen's | rolls | **8**: `director-kobil`; **10**: `marquis-scottiax` |
| Roll | halls | **2**: `neutral`; **3**: `darkness-falls` |
| Roll | letters | **2**: `marquis-chaosmar`; **4**: `field-marshal-nebir` |
| Sauvage | men | **3**: `green-knight`; **11**: `lord-gildas` |
| Skoll | legs | **2**: `hati`; **4**: `skoll` |
| Uncounted | fights | **30**: `duke-alloc`; **31**: `duke-eligar` |
| Falls | realms | 3: `albion`, `neutral`, `connacht` +2 |
| Avalon | heads | 2: `duraek-the-empowered`, `xanxicar`, `avalon-city` +1 |
| Darkness | realms | 3: `albion`, `neutral`, `connacht` +1 |
| Four | roads | 4: `albion-leader`, `apocalypse`, `serk` +1 |
| Sheeroe | doors | 4: `balor`, `cuuldurach-the-glimmer-king`, `connacht` +1 |
| Arward | best | 30: `golestandt`, `albion`, `dartmoor` |
| Atlantis | realms | 2: `albion`, `hibernia`, `midgard` |
| Blood | quarters | 4: `field-marshal-nebir`, `gatekeeper-dommel`, `high-lord-baelerdoth` |
| Breifine | keeners | 3: `black-lady`, `tinka`, `hibernia` |
