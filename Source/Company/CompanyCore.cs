using HarmonyLib;
using RimWorld;
using Verse;

namespace Company
{
    [DefOf]
    public static class CO_DefOf
    {
        public static ThoughtDef CO_TheyWereHere;
        public static ThoughtDef CO_FirstTime;
        public static ThoughtDef CO_TheyStopped;
        public static ThoughtDef CO_SawThemTalking;
        public static ThoughtDef CO_IKnowWhatThatIs;

        static CO_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(CO_DefOf));
        }
    }

    /// <summary>All the tuning numbers in one place. The ones players can change come from the settings menu.</summary>
    public static class Tuning
    {
        private static CompanySettings S => CompanyMod.Settings;

        /// <summary>Chance, each time the ledger looks, that a raw close loss brings someone back.</summary>
        public static float ChancePerCheck => S.chancePerCheck;

        /// <summary>How much of the grief's progress a presence holds still, as a fraction.</summary>
        public static float HoldsGriefBack => S.holdsGriefBack;

        /// <summary>Multiplier on the comfort of a visit.</summary>
        public static float Comfort => S.comfort;

        /// <summary>How close the loss has to have been before anyone comes back.</summary>
        public static float MinCloseness => S.minCloseness;

        public static bool Witnesses => S.witnesses;
        public static bool Enabled => S.enabled;

        /// <summary>How often the ledger considers whether anybody is visited.</summary>
        public const int CheckInterval = 7500;

        /// <summary>How near another colonist has to be to see it happen.</summary>
        public const float WitnessRadius = 9f;

        /// <summary>A visit does not repeat inside this, however long the grief runs.</summary>
        public const int MinTicksBetweenVisits = 30000;
    }

    [StaticConstructorOnStartup]
    public static class CO
    {
        static CO()
        {
            new Harmony("company.presence").PatchAll();
            Bridges.Connect();
            Log.Message("[Company] Loaded. Watching for who keeps coming back." + Bridges.Report());
        }

        public static bool Counts(Pawn p) =>
            p != null && !p.Dead && p.Spawned && p.needs?.mood != null && p.IsColonist && !p.IsPrisoner;

        /// <summary>
        /// Can this colonist be visited at all?
        ///
        /// You cannot be haunted by somebody you never let matter. A Psychopath is reached by
        /// nobody; a Sociopath only by their own small circle, and the Sociopath mod already
        /// refuses to record a loss outside it, so by the time a loss exists here that test has
        /// been passed. Nothing else is excluded — this is ordinary grief, not a disorder, and the
        /// mod should not decide that some colonists are too sensible for it.
        /// </summary>
        public static bool CanBeVisited(Pawn p)
        {
            if (!Counts(p))
            {
                return false;
            }
            return !p.story.traits.HasTrait(TraitDefOf.Psychopath);
        }
    }
}
