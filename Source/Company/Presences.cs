using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Company
{
    /// <summary>One colonist, and the person who keeps coming back to them.</summary>
    public class Presence : IExposable
    {
        public Pawn griever;
        public Pawn lost;
        public int beganTick;
        public int lastVisitTick;
        public int visits;

        public Presence() { }

        public Presence(Pawn griever, Pawn lost)
        {
            this.griever = griever;
            this.lost = lost;
            beganTick = Find.TickManager.TicksGame;
            lastVisitTick = -99999;
        }

        /// <summary>Still a thing that can happen: they are alive, the other is not.</summary>
        public bool Valid => griever != null && lost != null && !griever.Dead && lost.Dead;

        public void ExposeData()
        {
            Scribe_References.Look(ref griever, "griever");
            Scribe_References.Look(ref lost, "lost");
            Scribe_Values.Look(ref beganTick, "beganTick", 0);
            Scribe_Values.Look(ref lastVisitTick, "lastVisitTick", -99999);
            Scribe_Values.Look(ref visits, "visits", 0);
        }
    }

    /// <summary>
    /// Who is being visited, and what happens when they are.
    ///
    /// The mod is careful about one thing above all: this is not treated as a symptom. There is no
    /// mental break, no penalty for it happening, nothing that frames the colonist as having gone
    /// wrong. Seeing someone you have lost is ordinary, it is extremely common, and the people it
    /// happens to are usually more frightened of what it means than of the thing itself.
    ///
    /// What it costs is elsewhere: while somebody keeps coming back, the grief does not move.
    /// </summary>
    public class GameComponent_Company : GameComponent
    {
        private List<Presence> presences = new List<Presence>();
        private static GameComponent_Company instance;

        public GameComponent_Company(Game game)
        {
            instance = this;
        }

        public static GameComponent_Company Instance =>
            instance ?? Current.Game?.GetComponent<GameComponent_Company>();

        public override void FinalizeInit() => instance = this;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref presences, "presences", LookMode.Deep);
            if (presences == null)
            {
                presences = new List<Presence>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                presences.RemoveAll(p => p == null || p.griever == null || p.lost == null);
            }
        }

        /// <summary>Who, if anyone, is coming back to this colonist.</summary>
        public Presence For(Pawn griever)
        {
            for (int i = 0; i < presences.Count; i++)
            {
                if (presences[i]?.griever == griever && presences[i].Valid)
                {
                    return presences[i];
                }
            }
            return null;
        }

        public int Count => presences.Count;

        /// <summary>Exposed so the tests can set one up without waiting on chance.</summary>
        public Presence Begin(Pawn griever, Pawn lost)
        {
            Presence existing = For(griever);
            if (existing != null)
            {
                return existing;
            }
            Presence p = new Presence(griever, lost);
            presences.Add(p);
            griever.needs?.mood?.thoughts?.memories?.TryGainMemory(CO_DefOf.CO_FirstTime);
            return p;
        }

        public override void GameComponentTick()
        {
            if (!Tuning.Enabled || Find.TickManager.TicksGame % Tuning.CheckInterval != 0)
            {
                return;
            }
            Prune();
            ConsiderNewPresences();
            RunVisits();
        }

        /// <summary>
        /// A presence ends when the grief it belongs to does. Losing them a second time is its own
        /// small grief, which is why stopping is not simply a relief.
        /// </summary>
        private void Prune()
        {
            for (int i = presences.Count - 1; i >= 0; i--)
            {
                Presence p = presences[i];
                bool over = p == null || !p.Valid;
                if (!over)
                {
                    // Still raw enough for them to keep coming back?
                    float closeness;
                    Pawn stillGrieving = Bridges.FindSomeoneTheyLost(p.griever, out closeness);
                    over = stillGrieving != p.lost;
                }
                if (!over)
                {
                    continue;
                }
                if (p != null && p.lost != null && p.lost.Dead &&
                    p.griever != null && !p.griever.Dead && p.visits > 0)
                {
                    p.griever.needs?.mood?.thoughts?.memories?.TryGainMemory(CO_DefOf.CO_TheyStopped);
                }
                presences.RemoveAt(i);
            }
        }

        private void ConsiderNewPresences()
        {
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                foreach (Pawn p in maps[m].mapPawns.FreeColonistsSpawned)
                {
                    if (!CO.CanBeVisited(p) || For(p) != null)
                    {
                        continue;
                    }
                    float closeness;
                    Pawn lost = Bridges.FindSomeoneTheyLost(p, out closeness);
                    if (lost == null || closeness < Tuning.MinCloseness)
                    {
                        continue;
                    }
                    // Closer losses bring people back more readily.
                    if (!Rand.Chance(Tuning.ChancePerCheck * closeness))
                    {
                        continue;
                    }
                    Begin(p, lost);
                }
            }
        }

        private void RunVisits()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < presences.Count; i++)
            {
                Presence p = presences[i];
                if (p == null || !p.Valid || !CO.Counts(p.griever))
                {
                    continue;
                }
                if (now - p.lastVisitTick < Tuning.MinTicksBetweenVisits || !p.griever.Awake())
                {
                    continue;
                }
                Visit(p, now);
            }
        }

        /// <summary>They stop what they are doing and talk to somebody who is not there.</summary>
        private void Visit(Presence p, int now)
        {
            p.lastVisitTick = now;
            p.visits++;

            // Built by hand rather than by def, so the comfort slider can scale this one visit
            // without altering the definition for everybody.
            Thought_Memory comfort = (Thought_Memory)ThoughtMaker.MakeThought(CO_DefOf.CO_TheyWereHere);
            if (comfort != null)
            {
                comfort.moodPowerFactor = Tuning.Comfort;
                p.griever.needs.mood.thoughts.memories.TryGainMemory(comfort);
            }

            // The cost: while they keep coming back, the loss does not move on.
            Bridges.HoldGriefStill(p.griever, p.lost,
                (int)(Tuning.MinTicksBetweenVisits * Tuning.HoldsGriefBack));

            MoteMaker.ThrowText(p.griever.DrawPos, p.griever.Map,
                "\"" + p.lost.LabelShort + "\"", 3.2f);

            if (Tuning.Witnesses)
            {
                NoticeIt(p);
            }
        }

        /// <summary>
        /// Anybody close enough sees it. Somebody who has been through it themselves does not find
        /// it unsettling — they recognise it, and think better of them for it.
        /// </summary>
        private void NoticeIt(Presence p)
        {
            Map map = p.griever.Map;
            if (map == null)
            {
                return;
            }
            foreach (Pawn other in map.mapPawns.FreeColonistsSpawned)
            {
                if (other == p.griever || !CO.Counts(other) || !other.Awake())
                {
                    continue;
                }
                if (!other.Position.InHorDistOf(p.griever.Position, Tuning.WitnessRadius))
                {
                    continue;
                }
                if (!GenSight.LineOfSight(other.Position, p.griever.Position, map))
                {
                    continue;
                }
                ThoughtDef def = Bridges.HasBeenThroughIt(other)
                    ? CO_DefOf.CO_IKnowWhatThatIs
                    : CO_DefOf.CO_SawThemTalking;
                other.needs.mood.thoughts.memories.TryGainMemory(def, p.griever);
            }
        }
    }
}
