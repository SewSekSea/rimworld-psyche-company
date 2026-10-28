using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Company
{
    /// <summary>
    /// Reading the rest of the set without depending on any of it.
    ///
    /// Grief already knows who a colonist has lost, how close they were and how raw it still is.
    /// Company asks it rather than working any of that out again, and falls back to the base game's
    /// own death memories when Grief is not installed, so the mod stands on its own.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Bridges
    {
        private static Type griefComp;
        private static Type lossType;
        private static MethodInfo instanceGetter;
        private static MethodInfo openLosses;
        private static FieldInfo lostField;
        private static FieldInfo closenessField;
        private static FieldInfo reliefField;
        private static FieldInfo everBuriedField;
        private static MethodInfo stageOf;

        private static TraitDef weathered;

        /// <summary>Base game death memories, used when Grief is absent.</summary>
        private static readonly List<ThoughtDef> vanillaLossThoughts = new List<ThoughtDef>();

        public static bool GriefPresent => openLosses != null;
        public static bool MettlePresent => weathered != null;

        public static void Connect()
        {
            try
            {
                griefComp = AccessTools.TypeByName("Grief.GameComponent_Grief");
                lossType = AccessTools.TypeByName("Grief.Loss");
                if (griefComp != null && lossType != null)
                {
                    instanceGetter = AccessTools.PropertyGetter(griefComp, "Instance");
                    openLosses = AccessTools.Method(griefComp, "OpenLosses");
                    lostField = AccessTools.Field(lossType, "lost");
                    closenessField = AccessTools.Field(lossType, "closeness");
                    reliefField = AccessTools.Field(lossType, "reliefTicks");
                    everBuriedField = AccessTools.Field(lossType, "everBuried");
                    Type stages = AccessTools.TypeByName("Grief.Stages");
                    if (stages != null) { stageOf = AccessTools.Method(stages, "Of"); }
                }
            }
            catch
            {
                openLosses = null;
            }

            weathered = DefDatabase<TraitDef>.GetNamedSilentFail("MT_Weathered");

            foreach (string n in new[] { "MySpouseDied", "MyLoverDied", "MyFianceDied", "MyChildDied",
                                         "MyParentDied", "MySiblingDied", "PawnWithGoodOpinionDied" })
            {
                ThoughtDef d = DefDatabase<ThoughtDef>.GetNamedSilentFail(n);
                if (d != null) { vanillaLossThoughts.Add(d); }
            }
        }

        public static string Report()
        {
            string s = "";
            s += GriefPresent
                ? " Grief found: how raw a loss is comes from there."
                : " Grief absent: falling back to the base game's death memories.";
            if (MettlePresent) { s += " Mettle found: people who have been through it recognise it."; }
            return s;
        }

        /// <summary>Somebody who has come through things themselves, and knows what they are seeing.</summary>
        public static bool HasBeenThroughIt(Pawn p) =>
            weathered != null && p?.story?.traits != null && p.story.traits.HasTrait(weathered);

        /// <summary>
        /// Somebody this colonist lost, close enough and recent enough to still be coming back.
        /// Returns null when there is nobody.
        /// </summary>
        public static Pawn FindSomeoneTheyLost(Pawn griever, out float closeness)
        {
            closeness = 0f;
            if (griever == null)
            {
                return null;
            }
            return GriefPresent
                ? FromGrief(griever, out closeness)
                : FromVanillaMemories(griever, out closeness);
        }

        private static Pawn FromGrief(Pawn griever, out float closeness)
        {
            closeness = 0f;
            try
            {
                object comp = instanceGetter?.Invoke(null, null);
                if (comp == null) { return null; }
                object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(lossType));
                openLosses.Invoke(comp, new[] { griever, list });

                Pawn best = null;
                float bestCloseness = 0f;
                foreach (object l in (IEnumerable)list)
                {
                    if (l == null) { continue; }
                    // Only a loss that is still raw. Stage 0 is shock, 1 is the worst of it, and 5
                    // is a loss with no grave, which never settles and is exactly the one that
                    // keeps someone company indefinitely.
                    int stage = stageOf != null ? (int)stageOf.Invoke(null, new[] { l }) : 1;
                    if (stage != 0 && stage != 1 && stage != 5) { continue; }

                    float c = (float)closenessField.GetValue(l);
                    if (c > bestCloseness)
                    {
                        bestCloseness = c;
                        best = lostField.GetValue(l) as Pawn;
                    }
                }
                closeness = bestCloseness;
                return best;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Without Grief, the base game's own death memories are all there is to go on.</summary>
        private static Pawn FromVanillaMemories(Pawn griever, out float closeness)
        {
            closeness = 0f;
            List<Thought_Memory> memories = griever.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null) { return null; }
            Pawn best = null;
            for (int i = 0; i < memories.Count; i++)
            {
                Thought_Memory m = memories[i];
                if (m?.otherPawn == null || !m.otherPawn.Dead || !vanillaLossThoughts.Contains(m.def))
                {
                    continue;
                }
                // Family and partners first; a liked colleague is a weaker pull.
                float c = m.def.defName == "PawnWithGoodOpinionDied" ? 0.5f : 0.9f;
                if (c > closeness)
                {
                    closeness = c;
                    best = m.otherPawn;
                }
            }
            return best;
        }

        /// <summary>
        /// Hold the grief where it is.
        ///
        /// This is the cost of the whole mod, and it is deliberately not a mood penalty. Being
        /// visited feels better; it simply stops the loss moving on while it happens. Implemented
        /// by winding back the relief Grief has accumulated, so the clock does not advance.
        /// </summary>
        public static void HoldGriefStill(Pawn griever, Pawn lost, int ticks)
        {
            if (!GriefPresent || griever == null || lost == null || ticks <= 0)
            {
                return;
            }
            try
            {
                object comp = instanceGetter?.Invoke(null, null);
                if (comp == null) { return; }
                object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(lossType));
                openLosses.Invoke(comp, new[] { griever, list });
                foreach (object l in (IEnumerable)list)
                {
                    if (l == null || !ReferenceEquals(lostField.GetValue(l), lost)) { continue; }
                    int relief = (int)reliefField.GetValue(l);
                    reliefField.SetValue(l, relief - ticks);
                    return;
                }
            }
            catch
            {
                // Grief's shape changed; the presence still works, it just stops costing anything.
            }
        }

        /// <summary>Was the body ever laid to rest? A loss with no grave is the one that never lets go.</summary>
        public static bool WasBuried(Pawn griever, Pawn lost)
        {
            if (!GriefPresent) { return true; }
            try
            {
                object comp = instanceGetter?.Invoke(null, null);
                if (comp == null) { return true; }
                object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(lossType));
                openLosses.Invoke(comp, new[] { griever, list });
                foreach (object l in (IEnumerable)list)
                {
                    if (l != null && ReferenceEquals(lostField.GetValue(l), lost))
                    {
                        return (bool)everBuriedField.GetValue(l);
                    }
                }
            }
            catch
            {
            }
            return true;
        }
    }
}
