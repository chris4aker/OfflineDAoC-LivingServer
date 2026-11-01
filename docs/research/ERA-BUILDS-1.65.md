# Era builds and class shares, DAoC 1.65 (to 8 October 2003)

Research for Living Server phase B part 1 ([BOT-BUILDS-AND-RVR.md](../offlinedaoc-livingserver/docs/BOT-BUILDS-AND-RVR.md) on `plan/rvr-builds`).
Scope: Classic + Shrouded Isles classes (39), no Catacombs/ToA classes, no Sluaghbinder. Draft for the owner's review, 2026-11-01.
The machine-readable version is `bot-builds.draft.json` beside this file.

## 0. What was looked at, and how good it is

- **Main source: FreddysHouse forums**, the community boards for the European (GOA) servers Excalibur, Prydwen and Merlin.
  They are still online with their original post dates. Method, as for the bot names: crawl every thread list of the 15 realm
  forums and 4 RvR/class forums from February 2002 to 8 October 2003 (29,913 threads). Then take the 1,483 threads whose
  title says spec, template, build, respec or group setup, fetch their posts (18,766), and pull out every "number + spec line"
  build a poster wrote (2,666 builds across 39 classes). Each build row below says how many dated posts back it up and links the
  newest three. **Quality: good for which builds people ran and argued for. It is a proxy for how often they were played, not a
  count.** Most posts are dated 2003; Midgard and SI-class threads are almost all from 2003.
- **Skew:** the sources are European. GOA ran the US patches a few weeks to months late (1.62 reached Europe in summer 2003), so a
  European spring-2003 post often describes the US's late 2002. US class boards (Vault Network, Mythic boards, Camelot Herald class
  pages) could not be reached: the Wayback Machine is blocked from this session (same as for the names research), and the
  Allakhazam and Herald pages that are live are post-2004. **No US 2003 spec source is cited.**
- **Census:** the period census sites (Camelot Seer at daoc.gamescape.com, duskwave.com/daoc/generalstats.php) are only archived
  on the Wayback Machine, which can't be reached from here. What survives is what players pasted on FreddysHouse (section 2). No full
  class table for 2003 was reached.
- **Not used:** 2010s freeshard guides (Uthgard, Eden). Nothing below depends on them, so no "later" source is cited.
- **Notation:** *sourced* = a dated 2002–2003 post says it (link + date). *estimate* = my reasoned number, not in any source.
  **Every build weight and every class share in this file is an estimate.** Only the spec points and the census numbers in
  section 2 are sourced.

## 1. Builds per class

How to read a row: "Spec at 50" is the build as posters wrote it, in the repo's spec line names (`DOL.GS.Specs`). "Dated posts"
is how many posts (dated to 8 Oct 2003) have the same top two lines within 4 points. "Weight" is my estimate of the share of that
class's level 50s on this build, from those counts and what the threads say. "Use": rvr = mainly the RvR build at 50,
levelling = mainly a levelling/PvE build, both = either.

### What the threads agree on, for the RvR-critical builds

- **PBAE casters.**
  - *Albion:* the Ice Wizard (Cold Magic 50, the rest in Earth/Fire) is the PBAE build. It was **rare**, though: "find me 1 ice
    wizard ... one thing alb aint short of is fire wizards" ([2003-01-07](https://forums.freddyshouse.com/threads/the-perfect-albion-rvr-group.109225/)).
    In 2003 Albion PBAE groups were built around Theurgist PBT and nukes more than a PBAE wizard.
  - *Midgard:* PBAE is the **Suppression Spiritmaster** (49 Supp). "Pac healer (48 pac) ... Shaman (43 aug/31 cave for pbae
    disease and ae root) ... Spiritmaster (49 Suppression) x2" ([2003-08-09](https://forums.freddyshouse.com/threads/looking-to-try-a-pbaoe-setup-mid-grp.140481/)).
    The PBT carrier is the Suppression Runemaster.
  - *Hibernia:* the **Mana Eldritch** and the **Mana Enchanter** (Mana 50, Light ~20) are the "manabomb"
    ([2002-11-06](https://forums.freddyshouse.com/threads/so-respec-is-here-what-are-you-doing.103407/),
    [2003-05-29](https://forums.freddyshouse.com/threads/chanter-spec.128639/)). Hibernia ran PBAE in RvR more than the other
    realms: "hib grps are very reliant on casters (pbae etc)" ([2003-08-03](https://forums.freddyshouse.com/threads/do-you-still-use-casters-in-fixed-rvr-groups.139911/)).
- **Healers.**
  - Midgard splits the Healer into a **48 Pacification CC healer** ("24/6/48 MAP", [2003-02-26](https://forums.freddyshouse.com/threads/statistics-and-other-lies.115851/))
    and a **Mend/Aug main healer**. Groups wanted one of each.
  - Albion's Cleric RvR builds are **Rejuvenation** or **Enhancement/Rejuv** (spec buffs). Smite (48/23/9) was the
    pre-1.51 (mid-2002) build and was nerfed ([2002-07-22](https://forums.freddyshouse.com/threads/how-many-pure-smiters-we-got.93469/)).
  - Hibernia's Druid builds are **Regrowth 41 / Nurture 35** and a Nurture-heavy buffer build.
- **CC.**
  - *Albion:* the Mind Sorcerer (Mind 44 / Body 31 was the most posted build after 1.59) and the Minstrel
    (Instruments 50 / Stealth 50 / Slash 29).
  - *Midgard:* the Pac Healer.
  - *Hibernia:* the Bard (Music 47 / Nurture 43 / Regrowth 16 is by far the most posted Hibernian build: 18 identical posts).
- **Tanks.**
  - *Albion:* the slam-hybrid Armsman (Polearm 50 / Shields 42 / Slash or Thrust 39) and the Shield Paladin
    (Chants 48 / Shields 42).
  - *Midgard:* the Warrior (Axe 50 / Shields 42 / Parry 39).
  - *Hibernia:* the Hero (Large Weapons 50 / Shields 42 / Parry 39).

### Albion


**Armsman**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Polearm/Shield hybrid | tank | rvr | Polearm 50, Shields 42, Slash 39, Parry 6 | 0.40 | 16 | [armsman-spec-advice](https://forums.freddyshouse.com/threads/armsman-spec-advice.134508/) 2003-07-11; [armsman-spec-question-purepole](https://forums.freddyshouse.com/threads/armsman-spec-question-purepole.130247/) 2003-06-10; [if-your-armsmen-has-a-full-respec-what](https://forums.freddyshouse.com/threads/if-your-armsmen-has-a-full-respec-what-would-you-do.128208/) 2003-06-01 |
| Two-handed/Shield hybrid | tank | rvr | Two Handed 50, Shields 42, Slash 39 | 0.15 | 6 | [2hand-armsman-spec](https://forums.freddyshouse.com/threads/2hand-armsman-spec.131215/) 2003-06-14; [99-2h-armsman-sc-template](https://forums.freddyshouse.com/threads/99-2h-armsman-sc-template.128704/) 2003-06-02; [if-your-armsmen-has-a-full-respec-what](https://forums.freddyshouse.com/threads/if-your-armsmen-has-a-full-respec-what-would-you-do.128208/) 2003-05-30 |
| Sword and board | tank | both | Slash 50, Shields 50, Parry 28 | 0.30 | 14 | [99-armsman-template](https://forums.freddyshouse.com/threads/99-armsman-template.145044/) 2003-09-05; [if-your-armsmen-has-a-full-respec-what](https://forums.freddyshouse.com/threads/if-your-armsmen-has-a-full-respec-what-would-you-do.128208/) 2003-06-01; [armsman-spec](https://forums.freddyshouse.com/threads/armsman-spec.120340/) 2003-04-03 |
| Polearm/Shield hybrid (Thrust) | tank | rvr | Polearm 50, Shields 42, Thrust 39, Parry 6 | 0.15 | 16 | [armsman-spec-advice](https://forums.freddyshouse.com/threads/armsman-spec-advice.134508/) 2003-07-11; [armsman-spec-question-purepole](https://forums.freddyshouse.com/threads/armsman-spec-question-purepole.130247/) 2003-06-10; [if-your-armsmen-has-a-full-respec-what](https://forums.freddyshouse.com/threads/if-your-armsmen-has-a-full-respec-what-would-you-do.128208/) 2003-06-01 |

**Cabalist**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Matter/Body | pet-dot | both | Matter Magic 46, Body Magic 28, Spirit Magic 4 | 0.40 | 5 | [cabby-spec](https://forums.freddyshouse.com/threads/cabby-spec.140145/) 2003-08-06; [my-final-cabby-spec](https://forums.freddyshouse.com/threads/my-final-cabby-spec.111065/) 2003-01-23; [very-good-solo-cabalist-spec](https://forums.freddyshouse.com/threads/very-good-solo-cabalist-spec.110080/) 2003-01-13 |
| Matter/Spirit | pet-dot | both | Matter Magic 46, Spirit Magic 28 | 0.30 | 3 | [givf-cabalist-sc-template-please](https://forums.freddyshouse.com/threads/givf-cabalist-sc-template-please.132870/) 2003-06-26; [my-final-cabby-spec](https://forums.freddyshouse.com/threads/my-final-cabby-spec.111065/) 2003-01-23 |
| Body | assist-dd | rvr | Body Magic 45, Matter Magic 27 | 0.15 | 2 | [my-final-cabby-spec](https://forums.freddyshouse.com/threads/my-final-cabby-spec.111065/) 2003-01-23; [cabalist-spec](https://forums.freddyshouse.com/threads/cabalist-spec.105667/) 2002-11-27 |
| Spirit/Body | pet | levelling | Spirit Magic 37, Body Magic 34, Matter Magic 19 | 0.15 | 3 | [cabalist-spec](https://forums.freddyshouse.com/threads/cabalist-spec.137705/) 2003-07-28 |

**Cleric**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Rejuvenation healer | healer | rvr | Rejuvenation 46, Enhancement 27, Smite 8 | 0.35 | 7 | [cleric-spec](https://forums.freddyshouse.com/threads/cleric-spec.132302/) 2003-06-22; [cleric-spec](https://forums.freddyshouse.com/threads/cleric-spec.132314/) 2003-06-21; [cleric-sc-template-opinions-please](https://forums.freddyshouse.com/threads/cleric-sc-template-opinions-please.117725/) 2003-03-15 |
| Enhancement/Rejuv (spec buffs) | healer-buffer | both | Enhancement 40, Rejuvenation 36, Smite 4 | 0.45 | 7 | [cleric-spec](https://forums.freddyshouse.com/threads/cleric-spec.132314/) 2003-06-21; [cleric-spec](https://forums.freddyshouse.com/threads/cleric-spec.132302/) 2003-06-21; [cleric-sc-setup-so-near-yet-so-far](https://forums.freddyshouse.com/threads/cleric-sc-setup-so-near-yet-so-far.116565/) 2003-03-12 |
| Smite | assist-dd | levelling | Smite 48, Rejuvenation 23, Enhancement 9 | 0.20 | 0 | [how-many-pure-smiters-we-got](https://forums.freddyshouse.com/threads/how-many-pure-smiters-we-got.93469/) 2002-07-22 |

**Friar**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Enhance/Staff | support-melee | both | Enhancement 45, Staff 44, Rejuvenation 15, Parry 12 | 0.75 | 23 | [friar-sc-template](https://forums.freddyshouse.com/threads/friar-sc-template.135338/) 2003-07-09; [friar-spec](https://forums.freddyshouse.com/threads/friar-spec.117332/) 2003-03-17; [friar-spec](https://forums.freddyshouse.com/threads/friar-spec.114639/) 2003-02-22 |
| Enhance/Staff/Rejuv | support-melee | rvr | Enhancement 45, Staff 39, Rejuvenation 25, Parry 12 | 0.25 | 31 | [friar-sc-template-yes-yes-again](https://forums.freddyshouse.com/threads/friar-sc-template-yes-yes-again.131065/) 2003-06-16; [friar-spec](https://forums.freddyshouse.com/threads/friar-spec.127463/) 2003-05-27; [sc-template-for-friars](https://forums.freddyshouse.com/threads/sc-template-for-friars.121133/) 2003-04-09 |

**Infiltrator**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Thrust/CS assassin | stealther | rvr | Thrust 50, Critical Strike 34, Stealth 35, Envenom 36, Dual Wield 10 | 0.40 | 59 | [which-infil-spec](https://forums.freddyshouse.com/threads/which-infil-spec.150636/) 2003-10-05; [need-help-to-spec-a-infil](https://forums.freddyshouse.com/threads/need-help-to-spec-a-infil.143569/) 2003-08-29; [my-specs-and-options](https://forums.freddyshouse.com/threads/my-specs-and-options.139910/) 2003-08-07 |
| Thrust/DW "5-spec" | stealther | rvr | Thrust 50, Stealth 35, Dual Wield 35, Critical Strike 34, Envenom 33 | 0.35 | 71 | [which-infil-spec](https://forums.freddyshouse.com/threads/which-infil-spec.150636/) 2003-10-06; [need-help-to-spec-a-infil](https://forums.freddyshouse.com/threads/need-help-to-spec-a-infil.143569/) 2003-09-22; [my-specs-and-options](https://forums.freddyshouse.com/threads/my-specs-and-options.139910/) 2003-08-07 |
| Slash/CS assassin | stealther | both | Slash 50, Critical Strike 34, Stealth 35, Envenom 36, Dual Wield 10 | 0.25 | 6 | [need-help-to-spec-a-infil](https://forums.freddyshouse.com/threads/need-help-to-spec-a-infil.143569/) 2003-08-27; [infiltrator-race-starting-stat-points-](https://forums.freddyshouse.com/threads/infiltrator-race-starting-stat-points-and-infiltrator-templates.138925/) 2003-08-04; [2nd-oppinion-on-infil-spec-please](https://forums.freddyshouse.com/threads/2nd-oppinion-on-infil-spec-please.131623/) 2003-06-17 |

**Mercenary**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Thrust dual wield | melee-dps | rvr | Thrust 50, Dual Wield 50, Parry 10 | 0.45 | 8 | [thrust-mercenary-sc-template](https://forums.freddyshouse.com/threads/thrust-mercenary-sc-template.145900/) 2003-09-07; [mercenary-speccing-and-race](https://forums.freddyshouse.com/threads/mercenary-speccing-and-race.139816/) 2003-08-07; [mercfiltrator-or-5-spec-oppinions-plz](https://forums.freddyshouse.com/threads/mercfiltrator-or-5-spec-oppinions-plz.114839/) 2003-02-26 |
| Slash dual wield | melee-dps | both | Slash 50, Dual Wield 50, Parry 28 | 0.40 | 5 | [merc-specs](https://forums.freddyshouse.com/threads/merc-specs.114278/) 2003-02-21; [mercenary-spec-help-required](https://forums.freddyshouse.com/threads/mercenary-spec-help-required.113127/) 2003-02-03; [how-have-you-speced-your-mercanery](https://forums.freddyshouse.com/threads/how-have-you-speced-your-mercanery.100502/) 2002-10-12 |
| Defensive parry | melee-dps | rvr | Slash 29, Dual Wield 50, Parry 49 | 0.15 | 1 | [merc-specs](https://forums.freddyshouse.com/threads/merc-specs.114278/) 2003-02-26 |

**Minstrel**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Instruments/Stealth/Slash | cc-support | rvr | Instruments 50, Stealth 50, Slash 29 | 0.45 | 25 | [mincer-spec](https://forums.freddyshouse.com/threads/mincer-spec.147275/) 2003-09-18; [minstrel-sc-template](https://forums.freddyshouse.com/threads/minstrel-sc-template.137188/) 2003-07-23; [post-a-reply-in-here-if-you-think-you-](https://forums.freddyshouse.com/threads/post-a-reply-in-here-if-you-think-you-need-a-full-skill-or-ra-respec.130722/) 2003-06-15 |
| Instruments/Slash (no stealth) | cc-support | both | Instruments 44, Slash 44, Stealth 28 | 0.30 | 5 | [minstrel-spec](https://forums.freddyshouse.com/threads/minstrel-spec.105153/) 2002-11-30; [speccing-a-minstrel](https://forums.freddyshouse.com/threads/speccing-a-minstrel.103715/) 2002-11-04; [minstrel-speccing](https://forums.freddyshouse.com/threads/minstrel-speccing.93675/) 2002-07-29 |
| Instruments/Thrust | cc-support | both | Instruments 44, Thrust 39, Stealth 29 | 0.25 | 2 | [minstrel-spec](https://forums.freddyshouse.com/threads/minstrel-spec.113887/) 2003-02-20; [minstrel-respec](https://forums.freddyshouse.com/threads/minstrel-respec.102744/) 2002-11-02 |

**Necromancer**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Deathsight | pet-dd | both | Deathsight 48, Death Servant 24, Painworking 6 | 0.45 | 1 | [necro-spec](https://forums.freddyshouse.com/threads/necro-spec.114235/) 2003-02-18 |
| Death Servant | pet | levelling | Death Servant 47, Deathsight 25, Painworking 8 | 0.35 | 2 | [necro-spec](https://forums.freddyshouse.com/threads/necro-spec.115325/) 2003-02-21; [necro-spec](https://forums.freddyshouse.com/threads/necro-spec.114235/) 2003-02-18 |
| Deathsight/Painworking | pet-dd | rvr | Deathsight 48, Painworking 24, Death Servant 6 | 0.20 | 1 | [necro-50-spec](https://forums.freddyshouse.com/threads/necro-50-spec.115758/) 2003-03-04 |

**Paladin**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Chants/Shield/Parry | tank-support | rvr | Chants 48, Shields 42, Slash 29, Parry 30 | 0.40 | 55 | [paladin-spec-and-race](https://forums.freddyshouse.com/threads/paladin-spec-and-race.146293/) 2003-09-09; [pally-spec](https://forums.freddyshouse.com/threads/pally-spec.141120/) 2003-08-13; [paladin-spec](https://forums.freddyshouse.com/threads/paladin-spec.136761/) 2003-07-21 |
| Chants/Shield/Slash | tank-support | both | Chants 48, Shields 42, Slash 39, Parry 15 | 0.30 | 55 | [paladin-spec-and-race](https://forums.freddyshouse.com/threads/paladin-spec-and-race.146293/) 2003-09-09; [pally-spec](https://forums.freddyshouse.com/threads/pally-spec.141120/) 2003-08-13; [paladin-spec](https://forums.freddyshouse.com/threads/paladin-spec.136761/) 2003-07-21 |
| Full shield | tank-support | rvr | Shields 50, Chants 46, Slash 29, Parry 19 | 0.15 | 12 | [paladin-spec-and-race](https://forums.freddyshouse.com/threads/paladin-spec-and-race.146293/) 2003-09-09; [paladin-spec](https://forums.freddyshouse.com/threads/paladin-spec.136761/) 2003-07-21; [paladin-spec](https://forums.freddyshouse.com/threads/paladin-spec.132456/) 2003-06-23 |
| Two-handed chanter | tank-support | both | Chants 46, Two Handed 44, Parry 20 | 0.15 | 4 | [paladin-spec-and-race](https://forums.freddyshouse.com/threads/paladin-spec-and-race.146293/) 2003-09-08; [paladin-spec-questions](https://forums.freddyshouse.com/threads/paladin-spec-questions.103964/) 2002-11-09; [lvl-50-paladins-how-should-i-final-spe](https://forums.freddyshouse.com/threads/lvl-50-paladins-how-should-i-final-spec-only-4-more-lvls-to-go.94840/) 2002-08-13 |

**Reaver**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Flex/Shield/Soulrending | tank | both | Flexible 50, Shields 42, Soulrending 39, Parry 6 | 0.70 | 23 | [new-reaver-spec](https://forums.freddyshouse.com/threads/new-reaver-spec.132053/) 2003-06-23; [reaver-spec](https://forums.freddyshouse.com/threads/reaver-spec.130996/) 2003-06-15; [reaver-spec](https://forums.freddyshouse.com/threads/reaver-spec.128895/) 2003-05-31 |
| Soulrending focus | tank | rvr | Soulrending 45, Flexible 44, Shields 42, Parry 10 | 0.30 | 2 | [reaver-spec-help](https://forums.freddyshouse.com/threads/reaver-spec-help.115670/) 2003-02-27; [how-you-speccing-your-reaver](https://forums.freddyshouse.com/threads/how-you-speccing-your-reaver.114996/) 2003-02-23 |

**Scout**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Longbow archer | archer | both | Longbows 50, Shields 42, Stealth 33, Thrust 21 | 0.75 | 38 | [interesting-scout-spec](https://forums.freddyshouse.com/threads/interesting-scout-spec.144286/) 2003-09-01; [scout-template-need-help](https://forums.freddyshouse.com/threads/scout-template-need-help.135542/) 2003-07-16; [how-to-specc-a-scout](https://forums.freddyshouse.com/threads/how-to-specc-a-scout.132691/) 2003-06-24 |
| Longbow/Slash | archer | rvr | Longbows 46, Shields 42, Stealth 33, Slash 29 | 0.15 | 52 | [interesting-scout-spec](https://forums.freddyshouse.com/threads/interesting-scout-spec.144286/) 2003-09-01; [scout-template-need-help](https://forums.freddyshouse.com/threads/scout-template-need-help.135542/) 2003-07-16; [how-to-specc-a-scout](https://forums.freddyshouse.com/threads/how-to-specc-a-scout.132691/) 2003-06-24 |
| Melee scout | melee-dps | levelling | Thrust 50, Shields 42, Stealth 20 | 0.10 | 5 | [interesting-scout-spec](https://forums.freddyshouse.com/threads/interesting-scout-spec.144286/) 2003-09-02 |

**Sorcerer**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Mind/Body (AE mez) | cc-mezzer | rvr | Mind Magic 44, Body Magic 31 | 0.40 | 24 | [sorc-spec](https://forums.freddyshouse.com/threads/sorc-spec.138240/) 2003-07-30; [a-sorc-specc-dilemma](https://forums.freddyshouse.com/threads/a-sorc-specc-dilemma.124565/) 2003-05-03; [any-offers-for-a-sorc-sc-template-plea](https://forums.freddyshouse.com/threads/any-offers-for-a-sorc-sc-template-please.123414/) 2003-04-29 |
| Body/Mind | assist-dd | rvr | Body Magic 41, Mind Magic 34 | 0.35 | 24 | [sorc-spec](https://forums.freddyshouse.com/threads/sorc-spec.138240/) 2003-07-31; [sorc-ac-template](https://forums.freddyshouse.com/threads/sorc-ac-template.128699/) 2003-05-29; [sorc-sc-template-40-body-36-mind](https://forums.freddyshouse.com/threads/sorc-sc-template-40-body-36-mind.124682/) 2003-05-04 |
| Matter/Mind | assist-dd | levelling | Matter Magic 41, Mind Magic 34 | 0.25 | 10 | [sorc-spec](https://forums.freddyshouse.com/threads/sorc-spec.138240/) 2003-07-31; [solo-sorc-spec](https://forums.freddyshouse.com/threads/solo-sorc-spec.123344/) 2003-04-27; [1-59-1-60-for-sorcs-how-will-you-spec](https://forums.freddyshouse.com/threads/1-59-1-60-for-sorcs-how-will-you-spec.116573/) 2003-03-06 |

**Theurgist**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Air | assist-dd | rvr | Wind Magic 45, Earth Magic 26, Cold Magic 14 | 0.50 | 14 | [someone-tell-me-how-i-should-spec-my-t](https://forums.freddyshouse.com/threads/someone-tell-me-how-i-should-spec-my-thurg.142027/) 2003-08-17; [help-w-theurg-sc-template](https://forums.freddyshouse.com/threads/help-w-theurg-sc-template.131762/) 2003-06-21; [theurgist-speccing-question](https://forums.freddyshouse.com/threads/theurgist-speccing-question.117859/) 2003-03-17; [theurgist-speccing-question](https://forums.freddyshouse.com/threads/theurgist-speccing-question.117859/) 2003-03-14 |
| Earth (PBT) | support-pbt | rvr | Earth Magic 50, Cold Magic 26, Wind Magic 11 | 0.30 | 6 | [help-w-theurg-sc-template](https://forums.freddyshouse.com/threads/help-w-theurg-sc-template.131762/) 2003-06-21; [air-theurgist-sc-template](https://forums.freddyshouse.com/threads/air-theurgist-sc-template.126487/) 2003-05-16; [theurg-sc-setup](https://forums.freddyshouse.com/threads/theurg-sc-setup.111555/) 2003-01-22 |
| Ice/Earth | cc-root | levelling | Cold Magic 41, Earth Magic 35 | 0.20 | 1 | [theurgist-spec-question](https://forums.freddyshouse.com/threads/theurgist-spec-question.96528/) 2002-08-27; [theurgist-speccing-question](https://forums.freddyshouse.com/threads/theurgist-speccing-question.117859/) 2003-03-14 |

**Wizard**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Fire | assist-dd | both | Fire Magic 50, Earth Magic 18, Cold Magic 8 | 0.45 | 3 | [wizard-respec](https://forums.freddyshouse.com/threads/wizard-respec.105489/) 2002-11-23; [how-many-wizards-are-respeccing-to-ice](https://forums.freddyshouse.com/threads/how-many-wizards-are-respeccing-to-ice.103074/) 2002-11-03; [to-respec-or-not-to-respec-for-wizzies](https://forums.freddyshouse.com/threads/to-respec-or-not-to-respec-for-wizzies.95858/) 2002-08-28; [the-perfect-albion-rvr-group](https://forums.freddyshouse.com/threads/the-perfect-albion-rvr-group.109225/) 2003-01-07 |
| Ice PBAE | pbae | rvr | Cold Magic 50, Earth Magic 20, Fire Magic 20 | 0.35 | 1 | [ice-wizzy-spec](https://forums.freddyshouse.com/threads/ice-wizzy-spec.127027/) 2003-05-24; [how-many-wizards-are-respeccing-to-ice](https://forums.freddyshouse.com/threads/how-many-wizards-are-respeccing-to-ice.103074/) 2002-11-03 |
| Earth | assist-dd | levelling | Earth Magic 48, Cold Magic 24, Fire Magic 4 | 0.20 | 1 | [to-all-wizards-respeccing-to-earth](https://forums.freddyshouse.com/threads/to-all-wizards-respeccing-to-earth.102924/) 2002-11-06; [wizard-spec-at-40](https://forums.freddyshouse.com/threads/wizard-spec-at-40.140141/) 2003-08-06 |

### Midgard


**Berserker**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Axe/Left Axe | melee-dps | both | Axe 50, Left Axe 50, Parry 28 | 0.60 | 7 | [best-zerker-spec](https://forums.freddyshouse.com/threads/best-zerker-spec.125989/) 2003-05-12; [beserker-spec-noob-query](https://forums.freddyshouse.com/threads/beserker-spec-noob-query.120008/) 2003-04-01; [zerker-specc](https://forums.freddyshouse.com/threads/zerker-specc.118138/) 2003-03-25 |
| Hammer/Left Axe | melee-dps | rvr | Hammer 50, Left Axe 50, Parry 28 | 0.25 | 5 | [best-zerker-spec](https://forums.freddyshouse.com/threads/best-zerker-spec.125989/) 2003-05-14; [zerker-spec-advise](https://forums.freddyshouse.com/threads/zerker-spec-advise.116660/) 2003-03-13 |
| Sword/Left Axe | melee-dps | rvr | Sword 50, Left Axe 50, Parry 28 | 0.15 | 1 | [advice-specing-my-berserker](https://forums.freddyshouse.com/threads/advice-specing-my-berserker.133028/) 2003-06-27 |

**Bonedancer**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Suppression | pet-dd | both | Suppression 47, Darkness 26 | 0.55 | 9 | [bonedancer-spec-advice](https://forums.freddyshouse.com/threads/bonedancer-spec-advice.140129/) 2003-08-07; [looking-for-some-bonedancer-template-f](https://forums.freddyshouse.com/threads/looking-for-some-bonedancer-template-for-insane-soloing.133785/) 2003-07-06; [bonedancers-how-will-you-spec-yours](https://forums.freddyshouse.com/threads/bonedancers-how-will-you-spec-yours.113568/) 2003-02-23 |
| Bone Army | pet | levelling | Bone Army 45, Suppression 25, Darkness 16 | 0.45 | 5 | [looking-for-some-bonedancer-template-f](https://forums.freddyshouse.com/threads/looking-for-some-bonedancer-template-for-insane-soloing.133785/) 2003-07-02; [bonedancer-spec](https://forums.freddyshouse.com/threads/bonedancer-spec.115633/) 2003-02-24; [bonedancers-how-will-you-spec-yours](https://forums.freddyshouse.com/threads/bonedancers-how-will-you-spec-yours.113568/) 2003-02-19 |

**Healer**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Pacification (48 Pac) | healer-cc | rvr | Pacification 48, Mending 24, Augmentation 6 | 0.45 | 6 | [pac-healer-spec](https://forums.freddyshouse.com/threads/pac-healer-spec.146828/) 2003-09-11; [is-it-worth-healer-speccing-high-pac-j](https://forums.freddyshouse.com/threads/is-it-worth-healer-speccing-high-pac-just-for-pom5.146011/) 2003-09-10; [speccing-a-pac-mend-healer](https://forums.freddyshouse.com/threads/speccing-a-pac-mend-healer.141439/) 2003-08-22; [statistics-and-other-lies](https://forums.freddyshouse.com/threads/statistics-and-other-lies.115851/) 2003-02-26; [looking-to-try-a-pbaoe-setup-mid-grp](https://forums.freddyshouse.com/threads/looking-to-try-a-pbaoe-setup-mid-grp.140481/) 2003-08-09 |
| Pac/Mend | healer-cc | rvr | Pacification 44, Mending 31 | 0.20 | 10 | [pac-healer-spec](https://forums.freddyshouse.com/threads/pac-healer-spec.146828/) 2003-09-11; [speccing-a-pac-mend-healer](https://forums.freddyshouse.com/threads/speccing-a-pac-mend-healer.141439/) 2003-08-21; [pondering-a-respec](https://forums.freddyshouse.com/threads/pondering-a-respec.130691/) 2003-06-15 |
| Mend/Aug | healer | both | Mending 39, Augmentation 37, Pacification 4 | 0.35 | 12 | [mend-aug-healer-spec-advice](https://forums.freddyshouse.com/threads/mend-aug-healer-spec-advice.139739/) 2003-08-06; [aug-spec-healer](https://forums.freddyshouse.com/threads/aug-spec-healer.126290/) 2003-05-20; [healer-spec-help](https://forums.freddyshouse.com/threads/healer-spec-help.115996/) 2003-03-04 |

**Hunter**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Bow/Spear | archer | both | Composite Bow 45, Spear 39, Stealth 35, Beastcraft 32 | 0.55 | 14 | [hunter-specs-pls](https://forums.freddyshouse.com/threads/hunter-specs-pls.150620/) 2003-10-02; [hunter-spec](https://forums.freddyshouse.com/threads/hunter-spec.146661/) 2003-09-13; [hunter-spec](https://forums.freddyshouse.com/threads/hunter-spec.139096/) 2003-08-01 |
| Spear/Bow (melee hunter) | melee-dps | rvr | Spear 44, Composite Bow 40, Stealth 35, Beastcraft 32 | 0.45 | 17 | [hunter-spec](https://forums.freddyshouse.com/threads/hunter-spec.146661/) 2003-09-13; [hunter-specs-what-works](https://forums.freddyshouse.com/threads/hunter-specs-what-works.138239/) 2003-08-11; [hunter-specs-what-works](https://forums.freddyshouse.com/threads/hunter-specs-what-works.139555/) 2003-08-11 |

**Runemaster**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Darkness | assist-dd | both | Darkness 47, Suppression 26 | 0.55 | 8 | [rm-spec](https://forums.freddyshouse.com/threads/rm-spec.124523/) 2003-05-02; [runemaster-spec](https://forums.freddyshouse.com/threads/runemaster-spec.120840/) 2003-04-10; [runemaster-spec](https://forums.freddyshouse.com/threads/runemaster-spec.102985/) 2002-11-03 |
| Darkness 50 | assist-dd | rvr | Darkness 50, Suppression 20 | 0.25 | 8 | [rm-spec](https://forums.freddyshouse.com/threads/rm-spec.124523/) 2003-05-01; [runemaster-spec](https://forums.freddyshouse.com/threads/runemaster-spec.108736/) 2003-01-08; [runemaster-spec](https://forums.freddyshouse.com/threads/runemaster-spec.102985/) 2002-11-08 |
| Suppression (PBT) | support-pbt | rvr | Suppression 50, Darkness 24 | 0.20 | 2 | [runemaster-spec](https://forums.freddyshouse.com/threads/runemaster-spec.120840/) 2003-04-09; [runemaster-spec](https://forums.freddyshouse.com/threads/runemaster-spec.102985/) 2002-11-01 |

**Savage**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Hand to Hand | melee-dps | both | Hand to Hand 50, Savagery 42, Parry 9 | 0.65 | 14 | [2h-savage-spec](https://forums.freddyshouse.com/threads/2h-savage-spec.134856/) 2003-07-06; [suggestions-on-a-savage-spec](https://forums.freddyshouse.com/threads/suggestions-on-a-savage-spec.131381/) 2003-06-19; [savage-spec](https://forums.freddyshouse.com/threads/savage-spec.128284/) 2003-05-30 |
| Two-handed (Sword/Hammer) | melee-dps | rvr | Sword 50, Savagery 42, Parry 4 | 0.35 | 5 | [better-savage-spec-h2h-or-2h](https://forums.freddyshouse.com/threads/better-savage-spec-h2h-or-2h.135697/) 2003-07-11; [2h-savage-spec](https://forums.freddyshouse.com/threads/2h-savage-spec.134856/) 2003-07-06; [suggestions-on-a-savage-spec](https://forums.freddyshouse.com/threads/suggestions-on-a-savage-spec.131381/) 2003-06-18 |

**Shadowblade**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Axe/Left Axe | stealther | both | Axe 44, Left Axe 44, Stealth 35, Envenom 35, Critical Strike 10 | 0.40 | 12 | [about-my-spec](https://forums.freddyshouse.com/threads/about-my-spec.140177/) 2003-08-08; [kobold-sb-your-idea-about-a-spec](https://forums.freddyshouse.com/threads/kobold-sb-your-idea-about-a-spec.138914/) 2003-07-30; [best-sb-spec-after-1-62](https://forums.freddyshouse.com/threads/best-sb-spec-after-1-62.137745/) 2003-07-26 |
| Sword/CS (2h or 1h) | stealther | rvr | Sword 44, Critical Strike 44, Stealth 35, Envenom 35 | 0.35 | 6 | [need-help-with-sb-spec](https://forums.freddyshouse.com/threads/need-help-with-sb-spec.147706/) 2003-09-25; [need-some-help-with-sb-spec](https://forums.freddyshouse.com/threads/need-some-help-with-sb-spec.130096/) 2003-06-10; [how-do-i-spec-my-sb](https://forums.freddyshouse.com/threads/how-do-i-spec-my-sb.108510/) 2002-12-28 |
| Axe/CS | stealther | rvr | Axe 44, Critical Strike 39, Envenom 34, Stealth 34, Left Axe 25 | 0.25 | 20 | [need-help-with-sb-spec](https://forums.freddyshouse.com/threads/need-help-with-sb-spec.147706/) 2003-09-25; [re-biles-sb-spec-advice-on-vn-boards](https://forums.freddyshouse.com/threads/re-biles-sb-spec-advice-on-vn-boards.145761/) 2003-09-07; [sb-spec](https://forums.freddyshouse.com/threads/sb-spec.141836/) 2003-08-14 |

**Shaman**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Cave/Aug | healer-debuff | both | Subterranean 41, Augmentation 32, Mending 14 | 0.55 | 26 | [your-shaman-spec-ideas-welcome-p](https://forums.freddyshouse.com/threads/your-shaman-spec-ideas-welcome-p.148962/) 2003-09-25; [cave-shammy-spec-playstyle](https://forums.freddyshouse.com/threads/cave-shammy-spec-playstyle.146888/) 2003-09-15; [longwind-and-shammy-spec](https://forums.freddyshouse.com/threads/longwind-and-shammy-spec.145909/) 2003-09-11 |
| Aug/Cave (resist buffs, end regen) | healer-buffer | rvr | Augmentation 38, Subterranean 37, Mending 9 | 0.30 | 15 | [your-shaman-spec-ideas-welcome-p](https://forums.freddyshouse.com/threads/your-shaman-spec-ideas-welcome-p.148962/) 2003-09-26; [shaman-spec](https://forums.freddyshouse.com/threads/shaman-spec.140479/) 2003-08-19; [shammie-spec](https://forums.freddyshouse.com/threads/shammie-spec.139526/) 2003-08-04; [looking-to-try-a-pbaoe-setup-mid-grp](https://forums.freddyshouse.com/threads/looking-to-try-a-pbaoe-setup-mid-grp.140481/) 2003-08-09 |
| Aug/Mend | healer-buffer | rvr | Augmentation 42, Mending 33, Subterranean 7 | 0.15 | 5 | [best-rvr-shaman-spec](https://forums.freddyshouse.com/threads/best-rvr-shaman-spec.143170/) 2003-08-25; [shaman-spec-help-plz](https://forums.freddyshouse.com/threads/shaman-spec-help-plz.138734/) 2003-07-31; [how-to-spec-a-shaman-at-40](https://forums.freddyshouse.com/threads/how-to-spec-a-shaman-at-40.120115/) 2003-04-09 |

**Skald**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Battlesongs/Axe | support-melee | both | Battlesongs 46, Axe 44, Parry 17 | 0.35 | 5 | [skald-template-stats](https://forums.freddyshouse.com/threads/skald-template-stats.145278/) 2003-09-06; [advice-on-skald-spec](https://forums.freddyshouse.com/threads/advice-on-skald-spec.135495/) 2003-07-09; [skald-spec](https://forums.freddyshouse.com/threads/skald-spec.121330/) 2003-04-16 |
| Two-handed sword | melee-dps | both | Sword 50, Battlesongs 43, Parry 2 | 0.35 | 10 | [help-49-hammer-44-bs-skald-template](https://forums.freddyshouse.com/threads/help-49-hammer-44-bs-skald-template.146703/) 2003-09-10; [skald-spec-question](https://forums.freddyshouse.com/threads/skald-spec-question.144790/) 2003-08-29; [advice-on-skald-spec](https://forums.freddyshouse.com/threads/advice-on-skald-spec.135495/) 2003-07-09 |
| Battlesongs/Hammer | support-melee | rvr | Battlesongs 46, Hammer 44, Parry 17 | 0.30 | 5 | [help-49-hammer-44-bs-skald-template](https://forums.freddyshouse.com/threads/help-49-hammer-44-bs-skald-template.146703/) 2003-09-11; [skald-spec](https://forums.freddyshouse.com/threads/skald-spec.121330/) 2003-04-15; [skald-spec-help](https://forums.freddyshouse.com/threads/skald-spec-help.96987/) 2002-09-03 |

**Spiritmaster**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Suppression (PBAE) | pbae | rvr | Suppression 49, Darkness 25 | 0.45 | 3 | [wtb-single-line-respec-stone](https://forums.freddyshouse.com/threads/wtb-single-line-respec-stone.140736/) 2003-08-12; [sm-spec](https://forums.freddyshouse.com/threads/sm-spec.119658/) 2003-04-02; [looking-to-try-a-pbaoe-setup-mid-grp](https://forums.freddyshouse.com/threads/looking-to-try-a-pbaoe-setup-mid-grp.140481/) 2003-08-09; [sm-spec](https://forums.freddyshouse.com/threads/sm-spec.109396/) 2003-01-14 |
| Darkness/Suppression | assist-dd | both | Darkness 47, Suppression 26 | 0.35 | 3 | [sm-spec](https://forums.freddyshouse.com/threads/sm-spec.109396/) 2003-01-14 |
| Summoning | pet | levelling | Summoning 50, Suppression 20 | 0.20 | 2 | [summon-spec-spiritmaster](https://forums.freddyshouse.com/threads/summon-spec-spiritmaster.117177/) 2003-03-14; [sm-spec](https://forums.freddyshouse.com/threads/sm-spec.109396/) 2003-01-14 |

**Thane**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Storm/Hammer/Shield | hybrid-dd | both | Stormcalling 46, Hammer 44, Shields 42 | 0.70 | 14 | [need-help-with-thane-specc](https://forums.freddyshouse.com/threads/need-help-with-thane-specc.140628/) 2003-08-07; [slightly-different-thane-spec](https://forums.freddyshouse.com/threads/slightly-different-thane-spec.135474/) 2003-07-11; [to-respecc-or-not-to-respecc](https://forums.freddyshouse.com/threads/to-respecc-or-not-to-respecc.116546/) 2003-03-09 |
| Storm/Shield/Parry | hybrid-dd | rvr | Stormcalling 48, Shields 42, Hammer 39, Parry 15 | 0.30 | 23 | [need-help-with-thane-specc](https://forums.freddyshouse.com/threads/need-help-with-thane-specc.140628/) 2003-08-07; [slightly-different-thane-spec](https://forums.freddyshouse.com/threads/slightly-different-thane-spec.135474/) 2003-07-11; [thane-sc-template](https://forums.freddyshouse.com/threads/thane-sc-template.125645/) 2003-05-07 |

**Warrior**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Axe/Shield/Parry | tank | both | Axe 50, Shields 42, Parry 39 | 0.45 | 8 | [warrior-spec](https://forums.freddyshouse.com/threads/warrior-spec.140962/) 2003-08-20; [warrior-specs](https://forums.freddyshouse.com/threads/warrior-specs.120441/) 2003-04-07; [warrior-axe-shield-spec](https://forums.freddyshouse.com/threads/warrior-axe-shield-spec.102809/) 2002-11-04 |
| Hammer/Shield | tank | both | Hammer 50, Shields 50, Parry 28 | 0.30 | 5 | [warrior-spec](https://forums.freddyshouse.com/threads/warrior-spec.140962/) 2003-08-22; [warrior-template-help](https://forums.freddyshouse.com/threads/warrior-template-help.138864/) 2003-08-01; [warrior-specs](https://forums.freddyshouse.com/threads/warrior-specs.120441/) 2003-04-02 |
| Axe/Shield 50 | tank | rvr | Axe 50, Shields 50, Parry 18 | 0.25 | 9 | [warrior-spec](https://forums.freddyshouse.com/threads/warrior-spec.140962/) 2003-08-16; [anyone-got-a-nice-warrior-template](https://forums.freddyshouse.com/threads/anyone-got-a-nice-warrior-template.129013/) 2003-06-02; [warrior-specs](https://forums.freddyshouse.com/threads/warrior-specs.120441/) 2003-04-03 |

### Hibernia


**Animist**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Arboreal (bomber/pet) | pet | both | Arboreal Path 50, Creeping Path 19, Verdant Path 7 | 1.00 | 2 | [how-to-specc-the-animist](https://forums.freddyshouse.com/threads/how-to-specc-the-animist.135197/) 2003-07-10; [animist-spec](https://forums.freddyshouse.com/threads/animist-spec.131459/) 2003-06-19 |

**Bard**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Music/Nurture | cc-mezzer | both | Music 47, Nurture 43, Regrowth 16 | 0.70 | 42 | [bard-specs](https://forums.freddyshouse.com/threads/bard-specs.148969/) 2003-09-26; [bard-spec-for-rvr](https://forums.freddyshouse.com/threads/bard-spec-for-rvr.144244/) 2003-08-31; [bard-spec](https://forums.freddyshouse.com/threads/bard-spec.137141/) 2003-07-19 |
| Regrowth/Nurture | healer | rvr | Regrowth 47, Nurture 43, Music 16 | 0.30 | 3 | [bard-specs](https://forums.freddyshouse.com/threads/bard-specs.148969/) 2003-09-29; [bard-spec](https://forums.freddyshouse.com/threads/bard-spec.126023/) 2003-05-14; [bard-spec](https://forums.freddyshouse.com/threads/bard-spec.118292/) 2003-03-22 |

**Blademaster**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Blades/Celtic Dual | melee-dps | both | Blades 50, Celtic Dual 50, Parry 28 | 0.40 | 2 | [bm-speccing](https://forums.freddyshouse.com/threads/bm-speccing.132760/) 2003-06-27; [what-spec-are-bms-using-these-days](https://forums.freddyshouse.com/threads/what-spec-are-bms-using-these-days.108211/) 2002-12-29 |
| Piercing/Celtic Dual | melee-dps | rvr | Piercing 50, Celtic Dual 42, Parry 39 | 0.35 | 3 | [what-spec-are-bms-using-these-days](https://forums.freddyshouse.com/threads/what-spec-are-bms-using-these-days.108211/) 2002-12-31; [respeccing](https://forums.freddyshouse.com/threads/respeccing.103641/) 2002-11-07 |
| Blunt/Celtic Dual | melee-dps | rvr | Blunt 50, Celtic Dual 41, Parry 40 | 0.25 | 2 | [bm-speccing](https://forums.freddyshouse.com/threads/bm-speccing.132760/) 2003-06-27; [what-spec-are-bms-using-these-days](https://forums.freddyshouse.com/threads/what-spec-are-bms-using-these-days.108211/) 2002-12-29 |

**Champion**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Large Weapons/Valor | melee-dps | both | Large Weapons 50, Valor 50, Parry 28 | 0.80 | 19 | [champion-shield-blabes-spec](https://forums.freddyshouse.com/threads/champion-shield-blabes-spec.143560/) 2003-08-30; [spec-help-please](https://forums.freddyshouse.com/threads/spec-help-please.138805/) 2003-08-03; [champion-spec](https://forums.freddyshouse.com/threads/champion-spec.135027/) 2003-07-10 |
| Large Weapons/Valor/Blades | melee-dps | rvr | Large Weapons 50, Valor 40, Blades 39, Parry 28 | 0.20 | 4 | [champion-spec](https://forums.freddyshouse.com/threads/champion-spec.135027/) 2003-07-11; [need-help-with-a-champ-spec-please](https://forums.freddyshouse.com/threads/need-help-with-a-champ-spec-please.132792/) 2003-06-20 |

**Druid**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Regrowth/Nurture | healer | both | Regrowth 41, Nurture 35 | 0.50 | 19 | [rvr-grp-druid-spec](https://forums.freddyshouse.com/threads/rvr-grp-druid-spec.150179/) 2003-10-03; [druid-spec-help-needed](https://forums.freddyshouse.com/threads/druid-spec-help-needed.148472/) 2003-09-23; [druid-speccing](https://forums.freddyshouse.com/threads/druid-speccing.143389/) 2003-09-02 |
| Nurture/Regrowth (spec buffs) | healer-buffer | rvr | Nurture 44, Regrowth 31 | 0.30 | 14 | [druid-speccing](https://forums.freddyshouse.com/threads/druid-speccing.143389/) 2003-08-28; [abnormal-dr00d-specc](https://forums.freddyshouse.com/threads/abnormal-dr00d-specc.132480/) 2003-06-24; [what-is-a-good-druid-spec](https://forums.freddyshouse.com/threads/what-is-a-good-druid-spec.129432/) 2003-06-04 |
| Nature | healer-pet | levelling | Nature 36, Regrowth 35, Nurture 19 | 0.20 | 7 | [druid-spec-help-needed](https://forums.freddyshouse.com/threads/druid-spec-help-needed.148472/) 2003-09-23; [what-is-a-good-druid-spec](https://forums.freddyshouse.com/threads/what-is-a-good-druid-spec.129432/) 2003-06-04; [final-druid-spec](https://forums.freddyshouse.com/threads/final-druid-spec.122917/) 2003-04-21 |

**Eldritch**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Mana (PBAE "manabomb") | pbae | rvr | Mana 50, Light 20, Void 6 | 0.40 | 6 | [spec-eldritch](https://forums.freddyshouse.com/threads/spec-eldritch.149703/) 2003-09-26; [eldritch-specs](https://forums.freddyshouse.com/threads/eldritch-specs.140985/) 2003-08-12; [caster-noob-needs-eld-specc-advise](https://forums.freddyshouse.com/threads/caster-noob-needs-eld-specc-advise.104762/) 2002-11-11; [so-respec-is-here-what-are-you-doing](https://forums.freddyshouse.com/threads/so-respec-is-here-what-are-you-doing.103407/) 2002-11-06 |
| Void | assist-dd | both | Void 46, Mana 26, Light 10 | 0.35 | 4 | [what-rvr-spec-and-why](https://forums.freddyshouse.com/threads/what-rvr-spec-and-why.112903/) 2003-02-13; [eld-specing](https://forums.freddyshouse.com/threads/eld-specing.100494/) 2002-10-12; [light-spec-eldritches](https://forums.freddyshouse.com/threads/light-spec-eldritches.99042/) 2002-09-20 |
| Light | assist-dd | levelling | Light 45, Mana 29 | 0.25 | 1 | [eld-specing](https://forums.freddyshouse.com/threads/eld-specing.100494/) 2002-10-11 |

**Enchanter**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Mana (PBAE) | pbae | both | Mana 50, Light 20 | 0.65 | 4 | [mana-magician-template](https://forums.freddyshouse.com/threads/mana-magician-template.132389/) 2003-06-21; [chanter-spec](https://forums.freddyshouse.com/threads/chanter-spec.128639/) 2003-05-29; [enchanter-spec](https://forums.freddyshouse.com/threads/enchanter-spec.118542/) 2003-03-22 |
| Light | assist-dd | rvr | Light 45, Mana 29 | 0.20 | 3 | [light-spec-enchanters](https://forums.freddyshouse.com/threads/light-spec-enchanters.147299/) 2003-09-17; [enchanter-spec](https://forums.freddyshouse.com/threads/enchanter-spec.118542/) 2003-03-22 |
| Enchantments (pet/buff) | pet-buffer | levelling | Mana 39, Enchantments 34, Light 10 | 0.15 | 1 | [why-do-chanters-only-get-1-0-spec-poin](https://forums.freddyshouse.com/threads/why-do-chanters-only-get-1-0-spec-points.99084/) 2002-09-26 |

**Hero**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Large Weapons/Shield | tank | both | Large Weapons 50, Shields 42, Parry 39 | 0.40 | 8 | [hero-spec](https://forums.freddyshouse.com/threads/hero-spec.134321/) 2003-07-02; [opinions-on-this-hero-spec](https://forums.freddyshouse.com/threads/opinions-on-this-hero-spec.129372/) 2003-06-04; [hero-template-for-rvr-server-advice-ne](https://forums.freddyshouse.com/threads/hero-template-for-rvr-server-advice-needed.107638/) 2002-12-19 |
| Spear (Celtic Spear)/Shield | tank | rvr | Celtic Spear 50, Shields 42, Parry 39 | 0.25 | 1 | [spearo-spec](https://forums.freddyshouse.com/threads/spearo-spec.102105/) 2002-10-28 |
| Sword and board | tank | both | Blades 50, Shields 50, Parry 28 | 0.35 | 2 | [hero-spec](https://forums.freddyshouse.com/threads/hero-spec.134321/) 2003-07-02; [help-with-sc-template](https://forums.freddyshouse.com/threads/help-with-sc-template.117080/) 2003-03-13 |

**Mentalist**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Light/Mana | assist-dd | both | Light 45, Mana 28, Mentalism 6 | 0.60 | 16 | [mentalist-spec](https://forums.freddyshouse.com/threads/mentalist-spec.129596/) 2003-06-02; [mentalist-specc](https://forums.freddyshouse.com/threads/mentalist-specc.125786/) 2003-05-13; [mentalist-spec](https://forums.freddyshouse.com/threads/mentalist-spec.114139/) 2003-02-18 |
| Mana/Mentalism | pbae | rvr | Mana 46, Mentalism 28 | 0.25 | 4 | [mentalist-spec](https://forums.freddyshouse.com/threads/mentalist-spec.129596/) 2003-06-01; [mentalist-specc](https://forums.freddyshouse.com/threads/mentalist-specc.125786/) 2003-05-11; [mentalist-spec](https://forums.freddyshouse.com/threads/mentalist-spec.114139/) 2003-02-19 |
| Mentalism (heals) | healer | levelling | Mentalism 45, Light 25, Mana 25 | 0.15 | 1 | [is-this-a-good-spec-for-a-mentalist](https://forums.freddyshouse.com/threads/is-this-a-good-spec-for-a-mentalist.91578/) 2002-07-04 |

**Nightshade**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Piercing/CS | stealther | rvr | Piercing 50, Critical Strike 34, Stealth 35, Envenom 36 | 0.45 | 1 | [nightshade-template](https://forums.freddyshouse.com/threads/nightshade-template.126709/) 2003-06-05 |
| Critical Strike | stealther | both | Critical Strike 44, Piercing 39, Stealth 40, Envenom 36 | 0.35 | 5 | [anyone-got-a-spec-for-a-ns-if-he-auto-](https://forums.freddyshouse.com/threads/anyone-got-a-spec-for-a-ns-if-he-auto-trains-stealth-to-50.141339/) 2003-08-15; [shade-specs-plz-help](https://forums.freddyshouse.com/threads/shade-specs-plz-help.122602/) 2003-04-22; [lvl-50-ns-spec](https://forums.freddyshouse.com/threads/lvl-50-ns-spec.102245/) 2002-10-24 |
| Celtic Dual | stealther | rvr | Piercing 44, Celtic Dual 43, Envenom 36, Stealth 35 | 0.20 | 3 | [wta-full-skill-respec-stone](https://forums.freddyshouse.com/threads/wta-full-skill-respec-stone.147403/) 2003-09-18; [wta-full-skill-respec-stone](https://forums.freddyshouse.com/threads/wta-full-skill-respec-stone.145932/) 2003-09-18; [night-shade-spec-headache](https://forums.freddyshouse.com/threads/night-shade-spec-headache.132381/) 2003-06-27 |

**Ranger**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Archery/Pathfinding | archer | both | Recurve Bow 42, Pathfinding 40, Stealth 40, Piercing 30 | 0.60 | 19 | [ranger-spec-questions-again](https://forums.freddyshouse.com/threads/ranger-spec-questions-again.140520/) 2003-08-13; [ranger-spec-question](https://forums.freddyshouse.com/threads/ranger-spec-question.132826/) 2003-06-27; [ranger-rvr-spec](https://forums.freddyshouse.com/threads/ranger-rvr-spec.129040/) 2003-06-04 |
| Melee ranger (Piercing/CD) | melee-dps | rvr | Piercing 44, Pathfinding 36, Recurve Bow 35, Stealth 30, Celtic Dual 21 | 0.40 | 3 | [ranger-rvr-spec](https://forums.freddyshouse.com/threads/ranger-rvr-spec.129040/) 2003-06-02; [ranger-spec](https://forums.freddyshouse.com/threads/ranger-spec.124132/) 2003-05-08; [the-perfect-ranger-template](https://forums.freddyshouse.com/threads/the-perfect-ranger-template.87783/) 2002-05-17 |

**Valewalker**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Scythe/Arboreal | melee-dps | both | Scythe 50, Arboreal Path 38, Parry 20 | 1.00 | 1 | [valewalker-bg-and-final-spec](https://forums.freddyshouse.com/threads/valewalker-bg-and-final-spec.118642/) 2003-03-24 |

**Warden**

| Build | Role | Use | Spec at 50 | Weight (est.) | Dated posts | Sources |
|---|---|---|---|---|---|---|
| Nurture/Regrowth | support-healer | both | Nurture 49, Regrowth 33, Parry 14 | 0.55 | 7 | [warden-spec](https://forums.freddyshouse.com/threads/warden-spec.146822/) 2003-09-17; [warden-spec](https://forums.freddyshouse.com/threads/warden-spec.141502/) 2003-08-26; [warden-spec-help](https://forums.freddyshouse.com/threads/warden-spec-help.124124/) 2003-04-30 |
| Nurture/Blades | support-melee | levelling | Nurture 49, Blades 39, Regrowth 16, Parry 12 | 0.45 | 8 | [warden-spec](https://forums.freddyshouse.com/threads/warden-spec.146822/) 2003-09-17; [warden-spec](https://forums.freddyshouse.com/threads/warden-spec.141502/) 2003-09-04; [warden-spec-help](https://forums.freddyshouse.com/threads/warden-spec-help.124124/) 2003-04-30 |
## 2. Class shares per realm, circa 2003

### What dated numbers exist

| What | Date | Numbers | Source |
|---|---|---|---|
| Level 50 characters, Midgard, Excalibur (Europe), classic classes | 2003-01-22 | Skald 157, Healer 117, Warrior 104, Shadowblade 99, Runemaster 95, Thane 94, Berserker 78, Hunter 51, Shaman 44, Spiritmaster 44 (883 in total) | [FreddysHouse](https://forums.freddyshouse.com/threads/level-50-class-population-for-excal-midgard.111651/) (sourced) |
| US servers, levels 5–50, from Camelot Seer (daoc.gamescape.com) | 2003-06-19 | #1 Cleric 12,753; #2 Shaman 11,718; about 10,500 Infiltrators, 9,000 Shadowblades, 8,500 Druids, 8,400 Nightshades, 7,900 Bards, 6,450 Friars, 5,100 Wardens | [FreddysHouse "Class census for june"](https://forums.freddyshouse.com/threads/class-census-for-june-o.131872/) (sourced, partial: the full table is not quoted) |
| Same census, level 50 only | 2003-06-19 | about 2,600 Infiltrators, 2,400 Shadowblades, 2,000 Nightshades; #17 Necromancer 1,913; #35 Savage 622; #36 Reaver 485; #37 Bonedancer 481; #38 Valewalker 280; #39 Animist 230 (39 classes) | same thread (sourced, partial) |
| SI classes at level 50, Excalibur | 2003-08-20 | Necromancer 178, Savage 101, Reaver 41, Bonedancer 28, Valewalker 23, Animist 10 | [FreddysHouse "Classes Stats"](https://forums.freddyshouse.com/threads/classes-stats.142215/) (sourced) |
| Realm split, Prydwen | 2002-06-25 | Hibernia "never goes above 23–24%" of those online | [FreddysHouse](https://forums.freddyshouse.com/threads/we-are-only-20-of-prydwens-population.91188/) (sourced, player headcount) |
| Realm split, Excalibur | 2003-06-20 | "around 40% alb ... around 25% hibernian" | [census thread](https://forums.freddyshouse.com/threads/class-census-for-june-o.131872/) (sourced, player estimate) |

What the numbers say:
- **Buffbots inflate the healer classes.** Clerics and Shamans topped the census, and posters say why: "everybody has a buffbot"
  (2003-06-19, same thread). For RvR I weight Cleric and Shaman below their census rank.
- **Stealthers were among the most played classes in every realm.**
- **The SI classes were rare at 50 until late 2003.** They came out in December 2002.
- **Commentary on rare classes:** Albion was short of clerics, PBT theurgists and sorcerers
  ([2003-04-18](https://forums.freddyshouse.com/threads/how-many-balanced-ald-fgs-do-you-ever-see.121986/)). Skalds were
  "extremely rare these days" on Excalibur in May 2003, though they led the January count
  ([2003-05-07](https://forums.freddyshouse.com/threads/creating-balanced-guild-rvr-groups.125540/)). Few Mentalists ran in
  RvR groups ([2003-07-24](https://forums.freddyshouse.com/threads/rvr-class-balance.137262/)).

### Shares used in the draft (all estimates)

These blend the partial counts above with group demand (section 3), and move toward the SI classes' growth to October 2003. They
are estimates, not a census. Levelling shares weight buffbot and popular solo classes higher.


**Albion** (estimate)

| Class | RvR share | Levelling share | Rank word (RvR) |
|---|---|---|---|
| Cleric | 0.120 | 0.140 | common |
| Infiltrator | 0.110 | 0.110 | common |
| Armsman | 0.090 | 0.080 | common |
| Minstrel | 0.090 | 0.080 | common |
| Paladin | 0.080 | 0.070 | uncommon |
| Mercenary | 0.080 | 0.070 | uncommon |
| Friar | 0.070 | 0.070 | uncommon |
| Scout | 0.070 | 0.070 | uncommon |
| Wizard | 0.070 | 0.070 | uncommon |
| Theurgist | 0.060 | 0.060 | uncommon |
| Sorcerer | 0.050 | 0.050 | uncommon |
| Cabalist | 0.040 | 0.050 | rare |
| Necromancer | 0.040 | 0.050 | rare |
| Reaver | 0.030 | 0.030 | rare |

**Midgard** (estimate)

| Class | RvR share | Levelling share | Rank word (RvR) |
|---|---|---|---|
| Healer | 0.150 | 0.120 | common |
| Skald | 0.120 | 0.090 | common |
| Shadowblade | 0.110 | 0.120 | common |
| Warrior | 0.100 | 0.080 | common |
| Berserker | 0.100 | 0.080 | common |
| Shaman | 0.090 | 0.130 | common |
| Savage | 0.070 | 0.050 | uncommon |
| Runemaster | 0.070 | 0.070 | uncommon |
| Hunter | 0.060 | 0.080 | uncommon |
| Thane | 0.050 | 0.070 | uncommon |
| Spiritmaster | 0.050 | 0.060 | uncommon |
| Bonedancer | 0.030 | 0.050 | rare |

**Hibernia** (estimate)

| Class | RvR share | Levelling share | Rank word (RvR) |
|---|---|---|---|
| Druid | 0.120 | 0.120 | common |
| Bard | 0.110 | 0.100 | common |
| Nightshade | 0.110 | 0.110 | common |
| Hero | 0.100 | 0.090 | common |
| Ranger | 0.090 | 0.090 | common |
| Eldritch | 0.090 | 0.090 | common |
| Warden | 0.080 | 0.070 | uncommon |
| Blademaster | 0.080 | 0.080 | uncommon |
| Enchanter | 0.080 | 0.090 | uncommon |
| Champion | 0.060 | 0.060 | uncommon |
| Mentalist | 0.040 | 0.050 | rare |
| Valewalker | 0.020 | 0.025 | rare |
| Animist | 0.020 | 0.025 | rare |

## 3. What an 8-man RvR group looked like in 2003

Each realm's lists below are dated posts on FreddysHouse, not a measurement. The "typical" line at the end of each realm is an
**estimate**: what most of the posted setups share.

**Albion**
- Posted setups:
  - Cleric, Friar, Body Sorcerer, Theurgist, Paladin, Mercenary, plus a Minstrel or a second Theurgist or Cleric
    ([2003-01-07](https://forums.freddyshouse.com/threads/the-perfect-albion-rvr-group.109225/)).
  - "Sorc (AE mez), Theurg (PBT), Minstrel (speed 5, SoS), Cleric, Friar, Pala ... 2 heavy damage dealers"
    ([2003-02-25](https://forums.freddyshouse.com/threads/what-is-your-best-rvr-group.114736/)).
  - "1 theurg 1 sorc 1 cleric 1 friar 1 mincer 1 paladin 2 merc/armsman" ([2003-04-18](https://forums.freddyshouse.com/threads/how-many-balanced-ald-fgs-do-you-ever-see.121986/)).
- **Typical (estimate):** Cleric, Friar, Minstrel, Sorcerer (Mind), Theurgist (Air or Earth/PBT), Paladin, and two of
  Armsman/Mercenary (sometimes a Wizard or a second Cleric). That is one or two healers, a speed and CC bard-type, a mezzer, PBT,
  one tank-support and two melee damage dealers.

**Midgard**
- Posted setups:
  - "aug/mend healer, pac healer, skald, pbt rm, shammy, zerker x3" ([2003-02-05](https://forums.freddyshouse.com/threads/ideal-mid-rvr-group.112964/)).
  - "pac/mend healer, aug/mend shammy, 3x debuff rm, skald, 2 tanks" and "2 healers, shaman, skald, runemaster, 3 savages"
    ([2003-05](https://forums.freddyshouse.com/threads/creating-balanced-guild-rvr-groups.125540/)).
  - "skald, 2 healers, 2 rms, sm, warrior, 2 zerks" ([2002-12-31](https://forums.freddyshouse.com/threads/whats-your-perfect-rvr-group.108728/)).
  - PBAE variant: "aug/mend healer, pac healer, shaman, skald, 2 Suppression SMs, warrior (slambot), savage"
    ([2003-08-09](https://forums.freddyshouse.com/threads/looking-to-try-a-pbaoe-setup-mid-grp.140481/)).
- **Typical (estimate):** two Healers (Pac + Mend), a Shaman, a Skald, a Runemaster, and three melee (Berserker/Savage/Warrior),
  or a PBAE version with two Spiritmasters in place of two melee.

**Hibernia**
- Posted setups:
  - "1 bard 1 druid 1 warden 1/2 chanters 3/4 heros/champs" ([2003-04-18](https://forums.freddyshouse.com/threads/how-many-balanced-ald-fgs-do-you-ever-see.121986/)).
  - "bard, druid, druid, warden, pbae caster, pbae caster/void eld/offensive tank, mana eld, slammer" and "2 (pb) mages, 1 bard,
    2 druids, 1 warden, 2 tanks" ([2003-08-03/04](https://forums.freddyshouse.com/threads/do-you-still-use-casters-in-fixed-rvr-groups.139911/)).
- **Typical (estimate):** Bard, two Druids, Warden, two PBAE casters (Mana Eldritch/Enchanter), and two tanks (a Hero or
  Champion, often one slammer).

## 4. Every place I estimated

- **All build weights within a class.** These came from the "dated posts" counts and the threads' wording. Thin classes (n ≤ 3
  posts): Wizard (every build), Necromancer, Valewalker, Animist, Hero Spear, Mercenary parry, Cleric Smite (2002 only), Theurgist
  Ice, Eldritch Light, Enchanter Enchantments, Mentalist Mentalism, Nightshade Piercing.
- **Two builds share one set of posts.** For each of these pairs, the posts back the shared lines, and how they split between the
  two builds is a guess:
  - Armsman Polearm/Slash and Polearm/Thrust
  - Paladin's two Chants 48 / Shields 42 builds
  - Friar's two builds
  - Infiltrator's two Thrust 50 builds
  - Scout's two Longbow builds
- **All class shares, RvR and levelling, every realm.** The only full table is Midgard's level-50 classic classes on Excalibur in
  January 2003 (stored as `sourcedSnapshot` in the JSON). The June 2003 US census survives only as fragments.
- **The "typical" 8-man of each realm.**
- **Spec points are as posted.** Some posters' totals may be off by a point or two. I didn't recompute point budgets; the bot
  generator should clamp to the real budget for each class.
