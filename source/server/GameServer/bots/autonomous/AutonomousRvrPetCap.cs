using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// Pet caps in gamebot RvR groups (North Star audit 2026-11-03, priority 7; r/daoc: "about 10 Cabalist pets chase
/// you"). When gamebots form an RvR group (a roaming warband of 2-8 or a siege party of 8), at most
/// rvr_group_pet_cap pet classes join a group of five or more, and half that (at least one) a group of four or fewer.
/// The pet classes left out stay free for the next group. Groups that a player leads or joins are never touched, and
/// which classes exist in the population is unchanged (that is the build weights' job). Pure.
/// </summary>
public static class AutonomousRvrPetCap
{
    /// <summary>The classes that fight through a pet or turrets. Druids and Hunters, whose pet is a side skill, aren't counted.</summary>
    public static bool IsPetClass(eCharacterClass characterClass) => characterClass is
        eCharacterClass.Cabalist or eCharacterClass.Theurgist or eCharacterClass.Necromancer or
        eCharacterClass.Spiritmaster or eCharacterClass.Bonedancer or
        eCharacterClass.Enchanter or eCharacterClass.Animist or eCharacterClass.Sluaghbinder;

    public static bool IsPetClass(GameBot bot) => bot?.CharacterClass != null && IsPetClass((eCharacterClass)bot.CharacterClass.ID);

    /// <summary>The cap for a group of this size: the setting for five or more, half of it (at least 1) below; 0 or less: no cap.</summary>
    public static int CapFor(int groupSize, int capOfEight) =>
        capOfEight <= 0 ? int.MaxValue : groupSize <= 4 ? Math.Max(1, (capOfEight + 1) / 2) : capOfEight;

    /// <summary>
    /// Takes up to <paramref name="take"/> candidates in their order, skipping pet classes once the group (counting the
    /// <paramref name="petsAlready"/> it has, the leader's included) reaches <paramref name="cap"/>.
    /// </summary>
    public static T[] Pick<T>(IEnumerable<T> ordered, int take, int petsAlready, int cap, Func<T, bool> isPet)
    {
        var picked = new List<T>(Math.Max(0, take));
        int pets = petsAlready;
        foreach (T candidate in ordered)
        {
            if (picked.Count >= take)
                break;
            if (isPet(candidate))
            {
                if (pets >= cap)
                    continue;
                pets++;
            }
            picked.Add(candidate);
        }
        return picked.ToArray();
    }
}
