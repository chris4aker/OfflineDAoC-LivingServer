using System;
using DOL.Database;
using DOL.Language;

namespace DOL.GS
{
	/// <summary>
	/// This class holds all information that
	/// EVERY object in the game world needs!
	/// </summary>
	public class GameGravestone : GameStaticItem
	{
		/// <summary>
		/// how much xp are stored in this gravestone
		/// </summary>
		private long m_xpValue;
		/// <summary>
		/// Constructs a new empty Gravestone
		/// </summary>
		public GameGravestone(GamePlayer player, long xpValue):base()
		{
			//Objects should NOT be saved back to the DB
			//as standard! We want our mobs/items etc. at
			//the same startingspots when we restart!
			m_saveInDB = false;
			m_name = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameGravestone.GameGravestone.Grave", player.Name);
			Heading = player.Heading;
			m_x = player.X;
			m_y = player.Y;
			m_z = player.Z;
			CurrentRegionID = player.CurrentRegionID;
			m_level = 0;

			if (player.Realm == eRealm.Albion)
				m_model = 145; //Albion Gravestone
			else if (player.Realm == eRealm.Midgard)
				m_model = 636; //Midgard Gravestone
			else if (player.Realm == eRealm.Hibernia)
				m_model = 637; //Hibernia Gravestone
			else
				m_model = 1681; // Some rock.

			m_xpValue = xpValue;

			m_InternalID = player.InternalID;	// gravestones use the player unique id for themself
			ObjectState=eObjectState.Inactive;
		}

		#region Saved gravestones (1.2.3, after OpenDAoC-Core 25c8e9c5e and e10348f31)

		// Single-player sessions restart the server often, and a restart lost the XP in the gravestone. The stone is now kept on
		// the character (DbCoreCharacter.Gravestone*), set up again when the player enters the game, and removed when prayed
		// at, when the next one replaces it, or GRAVESTONE_DECAY_TIME days after it was made (0: never).

		/// <summary>Writes this gravestone into the character row.</summary>
		public static void Remember(DbCoreCharacter character, GameGravestone gravestone)
		{
			if (character == null || gravestone == null)
				return;

			character.HasGravestone = true;
			character.GravestoneRegion = gravestone.CurrentRegionID;
			character.GravestoneX = gravestone.X;
			character.GravestoneY = gravestone.Y;
			character.GravestoneZ = gravestone.Z;
			character.GravestoneHeading = gravestone.Heading;
			character.GravestoneXp = gravestone.XPValue;
			character.GravestoneCreated = DateTime.UtcNow;
		}

		/// <summary>Clears the saved gravestone (prayed at, replaced or decayed).</summary>
		public static void Forget(DbCoreCharacter character)
		{
			if (character == null)
				return;

			character.HasGravestone = false;
			character.GravestoneXp = 0;
		}

		/// <summary>Whether a gravestone made at <paramref name="created"/> has decayed by <paramref name="now"/> (pure).</summary>
		public static bool HasDecayed(DateTime created, DateTime now, int decayDays) =>
			decayDays > 0 && created != default && now - created >= TimeSpan.FromDays(decayDays);

		/// <summary>Whether the character's saved gravestone should be set up again (pure apart from the clock).</summary>
		public static bool ShouldRestore(bool hasGravestone, long xp, DateTime created, DateTime now, int decayDays) =>
			hasGravestone && xp > 0 && !HasDecayed(created, now, decayDays);

		/// <summary>
		/// Sets the player's saved gravestone up again when the player enters the game (after a restart), unless it is
		/// already in the world or has decayed. Older saves hold no position or XP and are left as they were.
		/// </summary>
		public static GameGravestone RestoreFor(GamePlayer player)
		{
			DbCoreCharacter character = player?.DBCharacter;

			if (character == null || !character.HasGravestone || character.GravestoneXp <= 0)
				return null;

			if (!ShouldRestore(character.HasGravestone, character.GravestoneXp, character.GravestoneCreated, DateTime.UtcNow,
				ServerProperties.Properties.GRAVESTONE_DECAY_TIME))
			{
				Forget(character);
				return null;
			}

			Region region = WorldMgr.GetRegion((ushort) character.GravestoneRegion);

			if (region == null)
				return null;

			GameGravestone existing = region.FindGraveStone(player);

			if (existing != null)
				return existing;

			GameGravestone gravestone = new(player, character.GravestoneXp);
			gravestone.CurrentRegionID = (ushort) character.GravestoneRegion;
			gravestone.X = character.GravestoneX;
			gravestone.Y = character.GravestoneY;
			gravestone.Z = character.GravestoneZ;
			gravestone.Heading = (ushort) character.GravestoneHeading;
			return gravestone.AddToWorld() ? gravestone : null;
		}

		#endregion
		/// <summary>
		/// returns the xpvalue of this gravestone
		/// </summary>
		public long XPValue
		{
			get
			{
				return m_xpValue;
			}
			set
			{
				m_xpValue = value;
			}
		}
	}
}
