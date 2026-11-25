using System;
using DOL.AI;
using DOL.AI.Brain;
using DOL.GS;

namespace DOL.GS
{
    public abstract class TimeDependentSpawnNpc : GameNPC
    {
        private const eFlags INACTIVE_FLAGS = eFlags.CANTTARGET | eFlags.DONTSHOWNAME | eFlags.PEACE;

        // Charmed, its brain isn't a TimeDependentSpawnBrain: it stays visible (OpenDAoC-Core 7ab320291). No brain yet still
        // means asleep (UT_NightSpawnFlags: a spawn saved asleep must wake up again).
        public TimeDependentSpawnBrain TimeDependentBrain => Brain as TimeDependentSpawnBrain;
        private bool IsShown => TimeDependentBrain?.IsVisible ?? Brain != null;
        public override eFlags Flags => IsShown ? base.Flags : base.Flags | INACTIVE_FLAGS;

        // Saved without the asleep flags, so a spawn saved at the wrong time of day still wakes up.
        protected override eFlags FlagsToSave => base.Flags;
        public override ushort Model => IsShown ? base.Model : (ushort) 1;

        public override bool AddToWorld()
        {
            SetOwnBrain(CreateBrain());
            return base.AddToWorld();
        }

        protected virtual ABrain CreateBrain()
        {
            throw new NotImplementedException();
        }
    }

    public class DaySpawn : TimeDependentSpawnNpc
    {
        protected override ABrain CreateBrain()
        {
            return new DaySpawnBrain();
        }
    }

    public class NightSpawn : TimeDependentSpawnNpc
    {
        protected override ABrain CreateBrain()
        {
            return new NightSpawnBrain();
        }
    }
}

namespace DOL.AI.Brain
{
    public abstract class TimeDependentSpawnBrain : StandardMobBrain
    {
        public bool IsVisible { get; private set; }

        public override void Think()
        {
            if (!Body.InCombat)
            {
                bool previousVisibility = IsVisible;
                IsVisible = ShouldBeVisible();

                if (previousVisibility != IsVisible)
                    ClientService.CreateObjectForPlayers(Body);
            }

            base.Think();
        }

        protected virtual bool ShouldBeVisible()
        {
            throw new NotImplementedException();
        }
    }

    public class DaySpawnBrain : TimeDependentSpawnBrain
    {
        protected override bool ShouldBeVisible()
        {
            return !Body.CurrentRegion.IsNightTime;
        }
    }

    public class NightSpawnBrain : TimeDependentSpawnBrain
    {
        protected override bool ShouldBeVisible()
        {
            return Body.CurrentRegion.IsNightTime;
        }
    }
}
