using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// #531 step 2e: how deep each side's back line stood in the RvR fight rehearsal (#520). Once a second while a side's
    /// board is in contact, each living back-line member's distance behind its tanks' line (RvrPositioning.Line, facing
    /// the enemy centroid) is sampled, apart for casters and healers. The row gives each side's mean and deepest.
    /// </summary>
    public sealed class RvrFightDepth
    {
        private readonly Dictionary<(eRealm Realm, bool Healer), List<float>> _samples = new();
        private long _nextAt;

        public void Tick(long now, IEnumerable<List<GameBot>> sides)
        {
            if (now < _nextAt)
                return;
            _nextAt = now + 1_000;
            foreach (List<GameBot> side in sides)
            {
                GameBot asker = side.FirstOrDefault(b => b.IsAlive);
                RvrFightBoard board = asker == null ? null : RvrFightBoards.Peek(asker);
                if (board is not { InContact: true } || board.EnemyCentroid is not { } centroid)
                    continue;
                var members = board.Members.Where(m => m?.Living != null && !m.IsPet && m.Living.IsAlive).ToList();
                Vector2? line = RvrPositioning.Line(members.Select(m => (RvrPeelPlan.At(m), RvrPositioning.StandFor(m.Role, m.Class))).ToList());
                if (line is not Vector2 anchor)
                    continue;
                double front = RvrPositioning.Bearing(anchor, new Vector2(centroid.X, centroid.Y));
                foreach (RvrCombatant m in members.Where(m => RvrPositioning.StandFor(m.Role, m.Class) == RvrStand.Back))
                {
                    var key = (m.Realm, RvrPositioning.CountsAsHealer(m.Role, m.Class));
                    if (!_samples.TryGetValue(key, out List<float> list))
                        _samples[key] = list = new List<float>();
                    list.Add(-RvrPositioning.Ahead(RvrPeelPlan.At(m), anchor, front));
                }
            }
        }

        public IReadOnlyList<float> Samples(eRealm realm, bool healer) =>
            _samples.TryGetValue((realm, healer), out List<float> list) ? list : Array.Empty<float>();

        /// <summary>"casters=mean/deepest healers=mean/deepest" for a side, or "-" where nothing was sampled.</summary>
        public string Text(eRealm realm)
        {
            string Of(bool healer)
            {
                IReadOnlyList<float> s = Samples(realm, healer);
                return s.Count == 0 ? "-" : $"{s.Average():0}/{s.Max():0}";
            }
            return $"casters={Of(false)} healers={Of(true)}";
        }
    }
}
