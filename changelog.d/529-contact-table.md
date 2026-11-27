---
version: 1.2.3
section: RvR
---
- **Realm alarms call only what a gamebot has actually seen, and every gamebot keeps a contact table** (#529, RvR
  plan #506, piece B steps 1-2). This covers autonomous gamebots in the frontier, the battlegrounds and the raid
  dungeons. Companions, temporary helpers and bots in a player's group keep no contacts and are unchanged.
  - A /faction "inc" alarm and the realm's news board ("where are the mids?") now count the enemies a gamebot noticed,
    was told of by a group mate, or was hit by in the last few seconds. Before, they counted everyone within the scan
    radius, behind hills and walls and before any bot had noticed them. Stealthers are still never called.
  - Each gamebot now remembers each enemy it knows of: when it first and last saw them, where, and how (noticed,
    called, attacked, hit, or taken under the keep rules). That knowledge fades over two minutes. Nothing else decides
    from it yet. The group fight board compares what its area search lists with what the members know, and logs the
    difference as `RVR_CONTACT_SHADOW` (once a minute per realm) and `RVR_CONTACT_UNKNOWN` (samples). This is the number
    to read before the board is switched to contacts.
  - `RvrContacts`, `RvrContactShadow`, `UT_RvrContacts529`; the RvR fight rehearsal prints the shadow. **Tests only.**
