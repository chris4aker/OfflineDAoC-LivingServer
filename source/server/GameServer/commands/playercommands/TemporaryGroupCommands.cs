using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS.Commands
{
    public static class TemporaryGroupClassCatalog
    {

        public static IEnumerable<(eCharacterClass CharacterClass, string Role)> ForRealm(eRealm realm)
        {
            // /spawn (and /raid, which fills through it) never offers the Sluaghbinder, in either edition, in the popup or
            // the text form: bots never play the custom class (owner 2026-11-17, BotCustomClassRule).
            IEnumerable<eCharacterClass> classes = AutonomousBotIdentityGenerator.GetEraClasses(realm)
                .Where(c => OfflineDaoc.Shared.BotCustomClassRule.BotsMayPlay((int)c));

            return classes.Select(characterClass => (characterClass, BotPartyRoles.Label(characterClass)));
        }

        public static bool TryResolve(eRealm realm, string input, out eCharacterClass characterClass)
        {
            string wanted = Normalize(input);
            foreach ((eCharacterClass candidate, _) in ForRealm(realm))
            {
                if (Normalize(candidate.ToString()) == wanted)
                {
                    characterClass = candidate;
                    return true;
                }
            }

            characterClass = default;
            return false;
        }

        private static string Normalize(string value) =>
            new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    /// <summary>
    /// The 1.65 client only turns bracketed popup text into clickable choices
    /// when an NPC owns the conversation.  This invisible, short-lived NPC is
    /// created solely for the requesting player and remains available so the
    /// player can add several companions without retyping the command.
    /// </summary>
    public sealed class TemporaryGroupSpawnMenu : GameNPC
    {
        private const string ActiveMenuProperty = "OfflineDAoC.ActiveSpawnClassMenu";
        private const int MenuLifetimeMilliseconds = 600_000;
        private const ushort InvisibleModel = 150;

        private readonly GamePlayer _owner;
        private readonly GameObject _previousTarget;
        private readonly bool _specs;
        private ECSGameTimer _expirationTimer;
        private bool _closed;

        private TemporaryGroupSpawnMenu(GamePlayer owner, bool specs)
        {
            _owner = owner;
            _specs = specs;
            _previousTarget = owner.TargetObject;
            Name = "Helper Class Menu";
            Realm = owner.Realm;
            Level = 1;
            Model = InvisibleModel;
            Size = 1;
            Flags = eFlags.PEACE | eFlags.DONTSHOWNAME;
            X = owner.X;
            Y = owner.Y;
            Z = owner.Z;
            Heading = owner.Heading;
            CurrentRegionID = owner.CurrentRegionID;
        }

        public static string BuildMenuText(eRealm realm)
        {
            string[] choices = TemporaryGroupClassCatalog.ForRealm(realm)
                .Select(entry => $"[{entry.CharacterClass}]")
                .ToArray();
            string links = string.Join('\n', choices.Chunk(4).Select(chunk => string.Join("   ", chunk)));
            return $"Choose a {GlobalConstants.RealmToName(realm)} helper to add to your group:\n\n{links}\n\nClick one class name.";
        }

        /// <summary>/spawn specs: every class with its builds on one line, named after the spec line each trains highest.</summary>
        public static string BuildSpecMenuText(eRealm realm)
        {
            IEnumerable<string> lines = TemporaryGroupClassCatalog.ForRealm(realm).Select(entry =>
            {
                IReadOnlyList<TemporaryGroupSpecCatalog.Option> options = TemporaryGroupSpecCatalog.For(entry.CharacterClass);
                return options.Count == 0
                    ? $"[{entry.CharacterClass}]"
                    : string.Join("   ", options.Select(option => $"[{entry.CharacterClass} ({option.Label})]"));
            });
            return $"Choose a {GlobalConstants.RealmToName(realm)} helper and their build:\n\n{string.Join('\n', lines)}\n\n" +
                   "Click one. In a macro: /spawn <class> <spec>, for example /spawn healer augmentation.";
        }

        private string MenuText() => _specs ? BuildSpecMenuText(_owner.Realm) : BuildMenuText(_owner.Realm);

        internal static bool Open(GamePlayer player, bool specs = false)
        {
            if (player?.CurrentRegion == null)
                return false;

            player.TempProperties.GetProperty<TemporaryGroupSpawnMenu>(ActiveMenuProperty)?.Close(true);

            var menu = new TemporaryGroupSpawnMenu(player, specs);
            if (!menu.AddToWorld())
                return false;

            player.TempProperties.SetProperty(ActiveMenuProperty, menu);
            player.TargetObject = menu;
            player.Out.SendChangeTarget(menu);
            menu._expirationTimer = new ECSGameTimer(menu, _ =>
            {
                menu.Close(true);
                return 0;
            }, MenuLifetimeMilliseconds);
            player.Out.SendMessage(menu.MenuText(), eChatType.CT_Say, eChatLoc.CL_PopupWindow);
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            // This private menu is only usable by its owner. Do not invoke the
            // normal NPC whisper throttle: each link is an explicit UI click,
            // and the player may legitimately add several classes in a row.
            if (!ReferenceEquals(source, _owner) || string.IsNullOrWhiteSpace(text))
                return false;

            if (!SpawnTemporaryGroupBotCommandHandler.TryResolve(_owner.Realm, text, out eCharacterClass characterClass,
                    out TemporaryGroupSpecCatalog.Option spec, out _))
                return false;

            SpawnTemporaryGroupBotCommandHandler.Spawn(_owner.Client, characterClass, spec);

            // A class can be clicked repeatedly and other classes can be added
            // without typing /spawn again. Reassert the invisible conversation
            // target and reopen the same popup because this client closes an
            // NPC speech choice after the link is clicked.
            if (!_closed && _owner.ObjectState is eObjectState.Active &&
                _owner.CurrentRegion == CurrentRegion)
            {
                _owner.TargetObject = this;
                _owner.Out.SendChangeTarget(this);
                _owner.Out.SendMessage(MenuText(), eChatType.CT_Say, eChatLoc.CL_PopupWindow);
            }
            return true;
        }

        private void Close(bool restoreTarget)
        {
            if (_closed)
                return;

            _closed = true;
            _expirationTimer?.Stop();
            _expirationTimer = null;

            if (ReferenceEquals(_owner?.TempProperties.GetProperty<TemporaryGroupSpawnMenu>(ActiveMenuProperty), this))
                _owner.TempProperties.RemoveProperty(ActiveMenuProperty);

            if (restoreTarget && _owner?.ObjectState is eObjectState.Active && ReferenceEquals(_owner.TargetObject, this))
            {
                GameObject target = _previousTarget?.ObjectState is eObjectState.Active &&
                                    _previousTarget.CurrentRegion == _owner.CurrentRegion
                    ? _previousTarget
                    : null;
                _owner.TargetObject = target;
                _owner.Out.SendChangeTarget(target);
            }

            Delete();
        }
    }

    [CmdAttribute("&classes", ePrivLevel.Player, "Lists your realm's Classic + SI classes, their helper builds and party roles", "/classes")]
    public sealed class ClassesCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            foreach (string line in Lines(client.Player.Realm))
                DisplayMessage(client, line);
        }

        /// <summary>
        /// What /classes prints (#473): one line per class with the builds /spawn accepts (TemporaryGroupSpecCatalog, the
        /// same names /spawn specs offers) or "single build", and the party role labelled as a role so it can't be read as
        /// a build; then how to pick a build.
        /// </summary>
        public static IReadOnlyList<string> Lines(eRealm realm)
        {
            var lines = new List<string> { $"{GlobalConstants.RealmToName(realm)} Classic + SI classes:" };
            foreach ((eCharacterClass characterClass, string role) in TemporaryGroupClassCatalog.ForRealm(realm))
                lines.Add($"{characterClass} - builds: {BuildText(characterClass)} - role: {role}");
            lines.Add("Use /spawn <class> [build] to create a temporary same-level helper, for example " +
                      $"/spawn {Example(realm)}. Without a build you get a random one. /spawn specs opens the build menu.");
            return lines;
        }

        /// <summary>The class's build names, or "single build".</summary>
        public static string BuildText(eCharacterClass characterClass) => BuildText(TemporaryGroupSpecCatalog.Describe(characterClass));

        /// <summary>The described builds as /classes shows them: "single build" when there are none to choose from.</summary>
        public static string BuildText(string describedBuilds) =>
            string.IsNullOrEmpty(describedBuilds) ? "single build" : describedBuilds;

        private static string Example(eRealm realm)
        {
            foreach ((eCharacterClass characterClass, _) in TemporaryGroupClassCatalog.ForRealm(realm))
            {
                IReadOnlyList<TemporaryGroupSpecCatalog.Option> options = TemporaryGroupSpecCatalog.For(characterClass);
                if (options.Count > 0)
                    return $"{characterClass.ToString().ToLowerInvariant()} {options[0].Label.ToLowerInvariant()}";
            }
            return "<class>";
        }
    }

    [CmdAttribute("&spawn", ePrivLevel.Player, "Opens the helper class menu or creates a temporary helper",
        "/spawn [class name] [spec]", "/spawn specs")]
    public sealed class SpawnTemporaryGroupBotCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            if (args.Length < 2)
            {
                if (player.Group != null && player.Group.MemberCount >= player.Group.MaximumMemberCount)
                {
                    DisplayMessage(client, "Your group is full.");
                    return;
                }

                if (!TemporaryGroupSpawnMenu.Open(player))
                    DisplayMessage(client, "The helper class menu could not open here. Use /spawn <class name> instead.");
                return;
            }

            string requested = string.Join(' ', args.Skip(1));
            if (args.Length == 2 && args[1].Equals("specs", StringComparison.OrdinalIgnoreCase))
            {
                if (player.Group != null && player.Group.MemberCount >= player.Group.MaximumMemberCount)
                {
                    DisplayMessage(client, "Your group is full.");
                    return;
                }
                if (!TemporaryGroupSpawnMenu.Open(player, specs: true))
                    DisplayMessage(client, "The helper menu could not open here. Use /spawn <class name> <spec> instead.");
                return;
            }

            if (!TryResolve(player.Realm, requested, out eCharacterClass characterClass, out TemporaryGroupSpecCatalog.Option spec,
                    out string problem))
            {
                DisplayMessage(client, problem);
                return;
            }

            Spawn(client, characterClass, spec);
        }

        /// <summary>
        /// "healer", "healer augmentation" or "Healer (Augmentation)" (a menu click). The spec is one of the class's builds
        /// named after the line it trains highest (TemporaryGroupSpecCatalog); none means a random build, as before.
        /// </summary>
        public static bool TryResolve(eRealm realm, string text, out eCharacterClass characterClass,
            out TemporaryGroupSpecCatalog.Option spec, out string problem)
        {
            spec = null;
            problem = null;
            text = text?.Trim() ?? "";
            string classText = text;
            string specText = null;
            int open = text.IndexOf('(');
            int space = text.IndexOf(' ');
            if (open > 0 && text.EndsWith(')'))
            {
                classText = text[..open].Trim();
                specText = text[(open + 1)..^1].Trim();
            }
            else if (space > 0 && !TemporaryGroupClassCatalog.TryResolve(realm, text, out _))
            {
                classText = text[..space];
                specText = text[(space + 1)..].Trim();
            }

            if (!TemporaryGroupClassCatalog.TryResolve(realm, classText, out characterClass))
            {
                problem = $"'{classText}' is not a Classic + SI {GlobalConstants.RealmToName(realm)} class. Type /classes or /spawn specs.";
                return false;
            }
            if (string.IsNullOrEmpty(specText))
                return true;
            if (TemporaryGroupSpecCatalog.TryResolve(characterClass, specText, out spec))
                return true;
            string known = TemporaryGroupSpecCatalog.Describe(characterClass);
            // A party role from /classes ("buffer", "tank") is not a build (#473): say so and name the builds.
            string asRole = IsRoleWord(specText) ? $"'{specText}' is a party role, not a build. " : $"'{specText}' is not a {characterClass} build. ";
            problem = known.Length == 0
                ? $"{(IsRoleWord(specText) ? asRole : "")}The {characterClass} helper has a single build. Use /spawn {characterClass.ToString().ToLowerInvariant()}."
                : $"{asRole}{characterClass} builds: {known}. Type /classes for every class's builds.";
            return false;
        }

        /// <summary>Whether the text is one of the party role words /classes shows (Tank, Healer, Buffer, Attacker...).</summary>
        public static bool IsRoleWord(string text)
        {
            string wanted = TemporaryGroupSpecCatalog.Normalize(text ?? "");
            return wanted.Length > 0 && RoleWords.Contains(wanted);
        }

        private static readonly HashSet<string> RoleWords = new(
            Enum.GetValues<eCharacterClass>()
                .SelectMany(characterClass => BotPartyRoles.Label(characterClass).Split('/'))
                .Append("Song Support")
                .Select(TemporaryGroupSpecCatalog.Normalize));

        internal static void Spawn(GameClient client, eCharacterClass characterClass, TemporaryGroupSpecCatalog.Option spec = null)
        {
            GamePlayer player = client?.Player;
            if (player == null || !TemporaryGroupClassCatalog.ForRealm(player.Realm)
                    .Any(entry => entry.CharacterClass == characterClass))
                return;

            if (player.Group != null && player.Group.MemberCount >= player.Group.MaximumMemberCount)
            {
                client.Out.SendMessage("Your group is full.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            // Play with friends (1.2): a server-wide companion budget while more than one player is online.
            if (CompanionBudget.BlocksSpawn(player, ClientService.Instance.GetPlayers(), out string budget) != null)
            {
                client.Out.SendMessage(budget, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            eGender gender = Random.Shared.Next(2) == 0 ? eGender.Male : eGender.Female;
            AutonomousBotIdentityGenerator.Identity identity =
                AutonomousBotIdentityGenerator.GenerateForClass(player.Realm, gender, characterClass);
            byte gearLevel = player.Level == 50 ? (byte)50 :
                (byte)Random.Shared.Next(Math.Max(1, player.Level - 10), player.Level + 1);
            GameBot helper;

            try
            {
                helper = new GameBot(
                    player,
                    (byte)characterClass,
                    identity.Name,
                    (byte)identity.Race,
                    (byte)identity.Gender,
                    temporaryGroupHelper: true,
                    equipmentLevel: gearLevel,
                    chosenSpec: spec == null ? null : TemporaryGroupSpecCatalog.Create(characterClass, spec));
            }
            catch (Exception exception)
            {
                client.Out.SendMessage($"Could not create the {characterClass} helper: {exception.Message}",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            double angle = Random.Shared.NextDouble() * Math.PI * 2;
            int distance = Random.Shared.Next(110, 221);
            Vector3 current = new(player.X, player.Y, player.Z);
            Vector3 desired = new(
                player.X + (float)(Math.Cos(angle) * distance),
                player.Y + (float)(Math.Sin(angle) * distance),
                player.Z);
            Vector3 spawn = PathfindingProvider.Instance.GetMoveAlongSurface(
                player.CurrentZone,
                current,
                desired,
                PathfindingProvider.Instance.DefaultFilters) ?? current;
            helper.X = (int)Math.Round(spawn.X);
            helper.Y = (int)Math.Round(spawn.Y);
            helper.Z = (int)Math.Round(spawn.Z);
            helper.Heading = player.Heading;
            helper.CurrentRegionID = player.CurrentRegionID;

            if (!helper.AddToWorld())
            {
                helper.Delete();
                client.Out.SendMessage("The temporary helper could not enter this region.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (player.Group == null)
            {
                var group = new Group(player);
                GroupMgr.AddGroup(group);
                group.AddMember(player);
            }

            if (!player.Group.AddMember(helper))
            {
                helper.Delete();
                client.Out.SendMessage("The temporary helper could not join your group.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            helper.EnterPlayerLedGroup(player);
            helper.Follow(player, BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);
            if (helper.Brain is BotBrain brain)
                brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);

            client.Out.SendMessage(
                $"{helper.Name}, level {helper.Level} {characterClass}{(spec == null ? "" : $" ({spec.Label})")}, joined fully equipped: armor/accessories level {Math.Max(1, helper.Level - 10)}–{helper.Level}, weapons/offhand level {Math.Max(1, helper.Level - 2)}–{helper.Level}. " +
                "This helper takes no loot, is never saved, and vanishes when removed from the group.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }

    [CmdAttribute("&pull", ePrivLevel.Player, "Orders your group's bots and their pets to engage your target", "/pull")]
    public sealed class PullGroupCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            if (player.TargetObject is not GameLiving target || !target.IsAlive)
            {
                DisplayMessage(client, "Select a living enemy first, then type /pull.");
                return;
            }
            if (!GameServer.ServerRules.IsAllowedToAttack(player, target, false))
                return;

            DisplayMessage(client, PlayerLedPullCoordinator.Begin(player, target));
        }
    }
}
