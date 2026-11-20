# 1.2.1 fix list (owner 16:13: publish, add issues here)
Already in docs/KNOWN-ISSUES.md (#377): faction quiet minutes; other region-200 seam loops; casting LosChecker.Out gap; Caledon guards not /target-able; click-targeting; stock merchant templates; QueuePacket DrainTo; raids tuning.
New from aa537ce (after #377 was written):
- Kanira3/Krudrum: "No connected exterior route" Castle Sauvage -> Caer Benowyc twice even after RVR_KEEP_ROUTE_RESTART (16:37-16:53).
- 11 Midgard bots logged in at an invalid position in Cruachan Gorge 200:370940,392757 (off mesh 155-696), moved; died to Hib guards.
- Connacht 200:314846,471535: repeated route rescues uphill to a hunting camp (bots + pets).
- Midgard defence of Dun nGed marched with 0 bots (16:38:53).
- GM /jump to a bot inside an enemy keep sends the GM to Cotswold ("area isn't secure").
- Launcher caption reads "HOW MUCH THEY TALK" (SocialExpansionSettingsControl.cs:150) while the docs and accessible name say "How much bots talk" (:35). Make them match.
- LORE (audit 2026-11-19, docs/1.2.1/LORE-AUDIT.md, grade C+): relabel 214 of 262 "mythic" facts that really come from Allakhazam/Prima/GameBanshee, and make the checker reject "mythic" without a Mythic source; strip developer notes from 74 legend texts and duplicated lines in 55 files (lint); reconcile Legion's origin with the neutral history; Cuuldurach "only male dragon" is player talk; fix Gjalpinulva to lead with Mythic's account; cap phrase tics ("the X say" 1,157x, Dame Ysolde 48x); trim the 165 stories over 400 words; give each realm's epics its own voice. Release notes' "where they disagreed, the archive won" overstates the check: soften.
