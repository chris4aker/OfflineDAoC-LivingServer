using DOL.GS.Keeps;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
	public class GameSiegeRam : GameSiegeWeapon
	{
		public GameSiegeRam() : base()
		{
			MeleeDamageType = eDamageType.Crush;
			Name = "siege ram";

			//AmmoType = 0x3B00;
			//this.Effect = 0x8A1;
			AmmoType = 0x26;
			EnableToMove = false; // Disabled to prevent exploits (going through walls) and because rams are automatically placed in a correct position.
			this.Model = 0xA2A;//0xA28
			//TODO find all value for ram
			ActionDelay = new int[]
			{
				0,//none
				5000,//aiming
				10000,//arming
				0,//loading
				1100//fireing
			};//en ms
		}

		//Set the maxium rams allowed to attack a target at the same time.
		private const int MAX_RAMS_ATTACKING_TARGET = 2;
		public override ushort Type()
		{
			return 0x9602;
		}

		public override int MAX_PASSENGERS
		{
			get
			{
				switch (Level)
				{
					case 0:
						return 2;
					case 1:
						return 6;
					case 2:
						return 8;
					case 3:
						return 12;
				}
				return Level * 3;
			}
		}

		public override int SLOT_OFFSET
		{
			get
			{
				return 1;
			}
		}

		public override void Aim()
		{
			if (Owner?.TargetObject == null) return;
			//Only allow rams to attack keep or relic doors 
			if (!(Owner.TargetObject is GameKeepDoor) && !(Owner.TargetObject is GameRelicDoor))
			{
				OwnerOut?.SendMessage("Rams can only attack doors!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}
			//Range Check
			if (!this.IsWithinRadius(Owner.TargetObject, attackComponent.AttackRange))
			{
				if(Owner != null)
					OwnerOut?.SendMessage("You are too far away to attack " + Owner.TargetObject.Name, eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}
			//Limit 2 Rams aimed at door at a time
			//Check # of rams on the target
			int ramsAimedAtTarget=0;
			foreach (GameNPC npc in Owner.TargetObject.GetNPCsInRadius(600))
			{
				if(npc is GameSiegeRam ram)
				{
					if (ram != this && ram.IsAlive && ram.Health > ram.DecayedHp && ram.TargetObject == Owner.TargetObject &&
						SharesRamLimit(this, ram))
					{
						ramsAimedAtTarget++;
					}
				}
			}

			if (ramsAimedAtTarget >= MAX_RAMS_ATTACKING_TARGET)
			{
				if(Owner != null)
					OwnerOut?.SendMessage("Too many rams already attacking   " + TargetObject?.Name, eChatType.CT_System,eChatLoc.CL_SystemWindow);
				return;
			}

			base.Aim();
		}

		/// <summary>#525: the two-rams-per-door limit is counted for the bots' rams and the players' rams apart, so a
		/// player's ram neither takes a bot ram's place nor is refused because two bot rams are on the door (siege.md
		/// finding 5). A door can therefore take two bot rams and two player rams at once.</summary>
		public static bool SharesRamLimit(GameSiegeWeapon a, GameSiegeWeapon b) => (a?.Owner is GamePlayer) == (b?.Owner is GamePlayer);

		public override void Fire()
		{
			GameLiving target = (TargetObject as GameLiving);
			if(target != null && !target.IsAlive)
			{

				if(Owner != null)
					OwnerOut?.SendMessage(target.Name + " is already destroyed!" , eChatType.CT_System,eChatLoc.CL_SystemWindow);
				return;
			}

			base.Fire();
		}

		public override void DoDamage()
		{
			if (Owner is GameBot && !BotSiegeRuntime.CanDamage(this, TargetObject as GameLiving)) return;
			GameLiving target = (TargetObject as GameLiving);
			if (target == null)
			{
				if(Owner != null)
					OwnerOut?.SendMessage("Select a target first.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}

			//Only allow rams to attack keep or relic doors 
			if (!(target is GameKeepDoor) && !(target is GameRelicDoor))
			{
				if(Owner != null)
					OwnerOut?.SendMessage("Rams can only attack doors!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}

			//todo good  distance check
			if (!this.IsWithinRadius(target, attackComponent.AttackRange))
			{
				if(Owner != null)
					OwnerOut?.SendMessage("You are too far away to attack " + target.Name, eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return;
			}

			int damageAmount = CalcDamageToTarget(target);

			AttackData ad = new AttackData();
			ad.Attacker = this;
			ad.Target = target;
			ad.AttackType = AttackData.eAttackType.Ranged;
			ad.AttackResult = eAttackResult.HitUnstyled;
			ad.Damage = damageAmount;
			ad.DamageType = MeleeDamageType;
			
			int before = target.Health;
			target.TakeDamage(this, eDamageType.Crush, damageAmount, 0);
			RecordBotHit(before, target);
			NoteOutput(System.Math.Max(0, before - target.Health), target);
			target.OnAttackedByEnemy(ad);

			if(Owner != null)
			{
				Owner.OnAttackEnemy(ad);
				OwnerOut?.SendMessage("The " + this.Name + " hits " + target.Name + " for " + damageAmount + " damage!", eChatType.CT_YouHit, eChatLoc.CL_SystemWindow);
				Message.SystemToArea(this, GetName(0, false) + " hits " + target.GetName(0, true), eChatType.CT_OthersCombat, Owner);
			}
			base.DoDamage();
		}

		public override bool RiderMount(GamePlayer rider, bool forced)
		{
			if (!base.RiderMount(rider, forced))
				return false;
			UpdateRamStatus();
			return true;
		}

		public override bool RiderDismount(bool forced, GamePlayer player)
		{
			if (!base.RiderDismount(forced, player))
				return false;
			if (player.SiegeWeapon == this)
				ReleaseControl();
			UpdateRamStatus();
			return true;
		}

		public override void ReleaseControl()
		{
			base.ReleaseControl();
			foreach (GamePlayer player in CurrentRiders)
				player.DismountSteed(true);
		}

		/// <summary>Bots standing this close to a ram count as its crew (bots cannot mount).</summary>
		public const int BotCrewRadius = 300;

		/// <summary>
		/// Riders, plus the friendly bots standing at the ram (bots cannot mount; owner 2026-10-07: players crewed rams
		/// and bot crews must break doors in a sensible time). Up to the ram's places.
		/// #525: the same for every operated ram, the player's own included. Before, only a bot-operated ram counted the
		/// bots beside it, so a player's ram hit for about a seventh of a bot ram's rate (siege.md finding 5).
		/// </summary>
		public int Crew
		{
			get
			{
				GameLiving op = Owner;
				if (op == null) return System.Math.Min(MAX_PASSENGERS, CurrentRiders.Length);
				var bots = new System.Collections.Generic.List<GameBot>();
				foreach (GameNPC npc in GetNPCsInRadius(BotCrewRadius))
					if (npc is GameBot member) bots.Add(member);
				return CountCrew(op, Realm, CurrentRiders.Length, bots, MAX_PASSENGERS);
			}
		}

		/// <summary>A bot at the ram that counts as crew: alive, of the ram's realm, not the operator, and either an
		/// autonomous gamebot (any friendly gamebot crews any friendly ram: run 2026-10-07, Midgard's ram at Caer
		/// Erasleigh ran on one bot while its army fought beside it) or in the operator's own group (a player's or a
		/// bot's companions). Who operates the ram, player or bot, does not matter.</summary>
		public static bool CountsAsCrew(GameLiving op, eRealm realm, GameBot member) =>
			member != null && member != op && member.IsAlive && member.Realm == realm &&
			(member.IsAutonomousWorldBot || op?.Group != null && op.Group.IsInTheGroup(member));

		public static int CountCrew(GameLiving op, eRealm realm, int riders, System.Collections.Generic.IEnumerable<GameBot> nearby, int places)
		{
			int crew = riders;
			if (op != null)
				foreach (GameBot member in nearby)
					if (CountsAsCrew(op, realm, member)) crew++;
			return System.Math.Min(places, crew);
		}

		/// <summary>Damage of one ram hit: base plus half the base per crew member.</summary>
		public static int CrewDamage(int baseDamage, int crew) => baseDamage + (baseDamage / 2 * crew);

		/// <summary>Reload (arming) time in ms: shorter with more crew.</summary>
		public static int CrewReloadMilliseconds(int level, int crew, int places) =>
			10000 + ((level + 1) * 2000) - (int)(10000 * ((double)crew / (double)places));

		public void UpdateRamStatus()
		{
			//speed of reload/arming changed by number of riders
			ActionDelay[2] = GetReloadDelay;
		}

		private int GetReloadDelay
		{
			get
			{
				//custom formula
				return CrewReloadMilliseconds(Level, Crew, MAX_PASSENGERS);
			}
		}

		public override int CalcDamageToTarget(GameLiving target)
		{
			//return BaseDamage + (int)(((double)BaseDamage / 2.0) * (double)((double)CurrentRiders.Length / (double)MAX_PASSENGERS));
			int crew = Crew;
			_lastCrew = crew;
			return CrewDamage(BaseDamage, crew);
		}

		private int _lastCrew;
		private long _outputSince, _outputDamage;
		private int _outputHits;

		/// <summary>#525: one RVR_SIEGE_OUTPUT line per ram per minute of hitting (owner, kind, crew, hits, damage and
		/// the door hit), player rams included, so a player's ram can be compared with a bot's in the log.</summary>
		public const long OutputLogMilliseconds = 60_000;

		private void NoteOutput(int damage, GameLiving target)
		{
			long now = GameLoop.GameLoopTime;
			if (_outputSince == 0) _outputSince = now;
			_outputHits++;
			_outputDamage += damage;
			if (now - _outputSince < OutputLogMilliseconds) return;
			if (log.IsInfoEnabled)
				log.Info(OutputLine(Owner, Realm, _lastCrew, MAX_PASSENGERS, _outputHits, _outputDamage, (now - _outputSince) / 1000, target));
			_outputSince = now; _outputHits = 0; _outputDamage = 0;
		}

		public static string OperatorKind(GameLiving op) => op switch
		{
			GamePlayer => "player",
			GameBot { IsAutonomousWorldBot: true } => "gamebot",
			GameBot => "companion",
			null => "none",
			_ => "other"
		};

		public static string OutputLine(GameLiving op, eRealm realm, int crew, int places, int hits, long damage, long seconds, GameLiving target) =>
			$"RVR_SIEGE_OUTPUT kind=ram owner={op?.Name ?? "none"} operator={OperatorKind(op)} realm={realm} crew={crew}/{places} " +
			$"hits={hits} damage={damage} seconds={seconds} target={target?.Name ?? "none"} targetId={(target as GameKeepDoor)?.DoorId ?? 0} " +
			$"targetHp={target?.HealthPercent ?? 0}";

		public override int BaseDamage
		{
			get
			{
				int damageAmount = 0;
				switch (Level)
				{
					case 0:
						//damageAmount = 200;
						damageAmount = 100;
						break;
					case 1:
						//damageAmount = 300;
						damageAmount = 125;
						break;
					case 2:
						//damageAmount = 450;
						damageAmount = 150;
						break;
					case 3:
						//damageAmount = 750;
						damageAmount = 200;
						break;
				}
				return damageAmount;
			}
		}

		public override int MeleeAttackRange
		{
			get
			{
				switch (Level)
				{
					case 0: return 300;
					case 1: return 400;
					case 2:
					case 3: return 500;
					default: return 500;
				}
			}
		}

		public override short MaxSpeed
		{
			get
			{
				double speed = 10.0 + 5.0 * Level + 50.0 * CurrentRiders.Length / MAX_PASSENGERS;

				foreach (GamePlayer player in CurrentRiders)
				{
					RealmAbilities.RAPropertyEnhancer ab = player.GetAbility<RealmAbilities.AtlasOF_LifterAbility>();

					if (ab != null)
						speed *= 1 + ab.Amount / 100;
				}

				return (short) speed;
			}
		}
	}
}
