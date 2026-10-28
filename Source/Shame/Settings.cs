using UnityEngine;
using Verse;

namespace Shame
{
    public class ShameSettings : ModSettings
    {
        public float severity = 1f;
        public float power = 1f;
        public float repair = 5f;

        public bool breakdowns = true;
        public bool fights = true;
        public bool insults = true;
        public bool rejection = true;
        public bool carried = true;
        public bool botched = true;

        public override void ExposeData()
        {
            ShameSettings d = new ShameSettings();
            Scribe_Values.Look(ref severity, "severity", d.severity);
            Scribe_Values.Look(ref power, "power", d.power);
            Scribe_Values.Look(ref repair, "repair", d.repair);
            Scribe_Values.Look(ref breakdowns, "breakdowns", true);
            Scribe_Values.Look(ref fights, "fights", true);
            Scribe_Values.Look(ref insults, "insults", true);
            Scribe_Values.Look(ref rejection, "rejection", true);
            Scribe_Values.Look(ref carried, "carried", true);
            Scribe_Values.Look(ref botched, "botched", true);
        }

        public void Reset()
        {
            ShameSettings d = new ShameSettings();
            severity = d.severity;
            power = d.power;
            repair = d.repair;
            breakdowns = fights = insults = rejection = carried = botched = true;
        }
    }

    public class ShameMod : Mod
    {
        public static ShameSettings Settings = new ShameSettings();

        public ShameMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ShameSettings>();
        }

        public override string SettingsCategory() => "Shame";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ShameSettings s = Settings;
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("How hard it lands");
            s.severity = list.SliderLabeled($"Being seen weighs {s.severity:P0}", s.severity, 0.25f, 2f);
            list.Gap(4f);
            s.power = list.SliderLabeled(
                $"A colonist thinks {s.power:P0} less of somebody who was there",
                s.power, 0.25f, 2f);
            list.Gap(4f);
            s.repair = Mathf.Round(list.SliderLabeled(
                $"A conversation started by one of them takes off {s.repair:0}",
                s.repair, 1f, 15f));

            list.Gap(12f);
            list.Label("What counts as being seen");
            list.CheckboxLabeled("Coming apart", ref s.breakdowns,
                "A mental breakdown with people close enough to watch it. The heaviest of these by a distance.");
            list.CheckboxLabeled("A row in public", ref s.fights,
                "A social fight. It counts for both of them, whoever started it.");
            list.CheckboxLabeled("Being spoken to like that", ref s.insults,
                "An insult delivered in front of other people.");
            list.CheckboxLabeled("Being turned down", ref s.rejection,
                "A romance rebuffed or a proposal refused.");
            list.CheckboxLabeled("Being carried in", ref s.carried,
                "Going down and being picked up and carried to a bed by somebody else.");
            list.CheckboxLabeled("Making it worse", ref s.botched,
                "A surgeon botching an operation. This one lands on the surgeon, and the patient is the witness.");

            list.Gap(12f);
            list.Label("Nothing here fades on a clock, and there is no setting for that. " +
                       "Shame is not a memory of an event, it is a conclusion about yourself, and " +
                       "time does not argue with a conclusion. The only thing that takes any of it " +
                       "off is one of the people who was actually there coming over of their own " +
                       "accord.\n\n" +
                       "Which means kindness from somebody who was not there is worth nothing to " +
                       "this. It will still lift their mood. It will not touch what is underneath.");

            list.Gap(12f);
            if (list.ButtonText("Reset to defaults"))
            {
                s.Reset();
            }

            list.End();
        }
    }
}
