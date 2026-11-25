using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// The player in the rehearsal (#520 scenario 3): a real <see cref="GamePlayer"/> (its class, level, stats and property
    /// calculators, its effect list and the server's player rules for crowd control) on a connection that drops every
    /// packet. It enters the region as an object of the region and zone (the client's login steps are skipped) and stands
    /// still; it never acts.
    /// </summary>
    public sealed class StandInPlayer : GamePlayer
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>A packet library that accepts every packet and sends nothing.</summary>
        public class NoPackets : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                Type type = method.ReturnType;
                return type == typeof(void) ? null : type.IsValueType ? Activator.CreateInstance(type) : null;
            }
        }

        private StandInPlayer(GameClient client, DbCoreCharacter character) : base(client, character) { }

        public static StandInPlayer Make(Region region, eRealm realm, eCharacterClass characterClass, byte level, string name)
        {
            var client = (GameClient)RuntimeHelpers.GetUninitializedObject(typeof(GameClient));
            typeof(GameClient).GetField("<Account>k__BackingField", Hidden)
                ?.SetValue(client, new DbAccount { Name = "rehearsal", Language = "EN", PrivLevel = 1 });
            client.Out = DispatchProxy.Create<IPacketLib, NoPackets>();
            var character = new DbCoreCharacter
            {
                Name = name, Level = level, Class = (int)characterClass, Realm = (byte)realm,
                Race = (int)RvrFightKits.Race(realm, characterClass), AccountName = "rehearsal",
                Strength = 75, Constitution = 75, Dexterity = 60, Quickness = 60, Intelligence = 60, Piety = 60, Empathy = 60, Charisma = 60,
                Region = region.ID,
            };
            typeof(DataObject).GetProperty(nameof(DataObject.ObjectId))?.SetValue(character, $"rehearsal-{name}");
            var player = new StandInPlayer(client, character);
            typeof(GameClient).GetField("<Player>k__BackingField", Hidden)?.SetValue(client, player);
            player.SetCharacterClass((int)characterClass);
            return player;
        }

        /// <summary>Enters the region at a spot: the region's and the zone's object lists, as AddToWorld puts an object there.</summary>
        public bool Enter(Region region, int x, int y)
        {
            CurrentRegion = region;
            X = x;
            Y = y;
            Z = 0;
            Zone zone = region.GetZone(x, y);
            MethodInfo add = typeof(Region).GetMethod("AddObject", Hidden);
            if (zone == null || !(bool)add.Invoke(region, [this]) || !zone.AddObject(this))
                return false;
            ObjectState = eObjectState.Active;
            Health = MaxHealth;
            return true;
        }

        /// <summary>Stands somewhere else in the same region (the zone moves it on its next tick).</summary>
        public void MoveInRegionTo(int x, int y)
        {
            X = x;
            Y = y;
            CurrentZone?.AddObject(this);
        }

        /// <summary>Leaves the region's lists.</summary>
        public void Leave()
        {
            if (ObjectState != eObjectState.Active)
                return;
            CurrentRegion?.GetType().GetMethod("RemoveObject", Hidden)?.Invoke(CurrentRegion, [this]);
            SubZoneObject?.InitiateSubZoneTransition(null, null);
            ObjectState = eObjectState.Inactive;
        }

        public override bool RemoveFromWorld()
        {
            Leave();
            return true;
        }
    }
}
