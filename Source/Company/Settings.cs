using UnityEngine;
using Verse;

namespace Company
{
    public class CompanySettings : ModSettings
    {
        public float chancePerCheck = 0.10f;
        public float holdsGriefBack = 0.75f;
        public float comfort = 1f;
        public float minCloseness = 0.55f;

        public bool witnesses = true;
        public bool enabled = true;

        public override void ExposeData()
        {
            CompanySettings d = new CompanySettings();
            Scribe_Values.Look(ref chancePerCheck, "chancePerCheck", d.chancePerCheck);
            Scribe_Values.Look(ref holdsGriefBack, "holdsGriefBack", d.holdsGriefBack);
            Scribe_Values.Look(ref comfort, "comfort", d.comfort);
            Scribe_Values.Look(ref minCloseness, "minCloseness", d.minCloseness);
            Scribe_Values.Look(ref witnesses, "witnesses", true);
            Scribe_Values.Look(ref enabled, "enabled", true);
        }

        public void Reset()
        {
            CompanySettings d = new CompanySettings();
            chancePerCheck = d.chancePerCheck;
            holdsGriefBack = d.holdsGriefBack;
            comfort = d.comfort;
            minCloseness = d.minCloseness;
            witnesses = enabled = true;
        }
    }

    public class CompanyMod : Mod
    {
        public static CompanySettings Settings = new CompanySettings();

        public CompanyMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<CompanySettings>();
        }

        public override string SettingsCategory() => "Company";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            CompanySettings s = Settings;
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.CheckboxLabeled("Company", ref s.enabled,
                "Colonists who lose somebody close may start seeing them.");

            list.Gap(12f);
            list.Label("How often, and how close");
            s.chancePerCheck = list.SliderLabeled(
                $"Chance it happens at all: {s.chancePerCheck:P0} per check", s.chancePerCheck, 0.01f, 0.5f);
            list.Gap(4f);
            s.minCloseness = list.SliderLabeled(
                $"Only for losses at least {s.minCloseness:P0} close", s.minCloseness, 0.2f, 1f);

            list.Gap(12f);
            list.Label("What it does");
            s.comfort = list.SliderLabeled(
                $"A visit comforts them at {s.comfort:P0}", s.comfort, 0.25f, 2f);
            list.Gap(4f);
            s.holdsGriefBack = list.SliderLabeled(
                $"And holds the grief still at {s.holdsGriefBack:P0}", s.holdsGriefBack, 0f, 1f);
            list.Gap(4f);
            list.CheckboxLabeled("Other colonists notice", ref s.witnesses,
                "Anybody nearby sees them talking to somebody who is not there. Colonists who have been through it themselves recognise it instead of being unsettled by it.");

            list.Gap(12f);
            list.Label("Seeing somebody you have lost is a common part of grief, not a symptom of " +
                       "anything. Nothing here treats it as one: there is no breakdown, no penalty " +
                       "for it happening, and no cure to research. It passes on its own, unless the " +
                       "loss underneath it never resolves.");

            list.Gap(12f);
            if (list.ButtonText("Reset to defaults"))
            {
                s.Reset();
            }

            list.End();
        }
    }
}
