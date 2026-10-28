using HarmonyLib;
using RimWorld;
using Verse;

namespace Shame
{
    [DefOf]
    public static class SH_DefOf
    {
        public static ThoughtDef SH_Shame;
        public static ThoughtDef SH_TheySawMe;
        public static ThoughtDef SH_TheyStayed;

        static SH_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SH_DefOf));
        }
    }

    /// <summary>
    /// What kind of thing was seen. Kept as an enum rather than a weight at the call site so the
    /// settings can price each one separately and the letters can say which it was.
    /// </summary>
    public enum Exposure
    {
        Breakdown,
        Fight,
        Insulted,
        Rejected,
        Carried,
        Botched
    }

    /// <summary>All the tuning in one place. The ones a player can change come from the settings menu.</summary>
    public static class Tuning
    {
        private static ShameSettings S => ShameMod.Settings;

        /// <summary>Multiplier on what every exposure weighs.</summary>
        public static float Severity => S.severity;

        /// <summary>Multiplier on the opinion cost of a witness.</summary>
        public static float Power => S.power;

        /// <summary>How much of one witness's weight an ordinary good conversation takes off.</summary>
        public static float Repair => S.repair;

        public static bool Breakdowns => S.breakdowns;
        public static bool Fights => S.fights;
        public static bool Insults => S.insults;
        public static bool Rejection => S.rejection;
        public static bool Carried => S.carried;
        public static bool Botched => S.botched;

        /// <summary>What each kind of thing costs, per witness, before the slider.</summary>
        public static float BaseWeight(Exposure e)
        {
            switch (e)
            {
                case Exposure.Breakdown: return 10f;
                case Exposure.Fight: return 6f;
                case Exposure.Insulted: return 4f;
                case Exposure.Rejected: return 5f;
                case Exposure.Carried: return 5f;
                case Exposure.Botched: return 8f;
                default: return 0f;
            }
        }

        /// <summary>Is this kind of exposure switched on?</summary>
        public static bool Enabled(Exposure e)
        {
            switch (e)
            {
                case Exposure.Breakdown: return Breakdowns;
                case Exposure.Fight: return Fights;
                case Exposure.Insulted: return Insults;
                case Exposure.Rejected: return Rejection;
                case Exposure.Carried: return Carried;
                case Exposure.Botched: return Botched;
                default: return false;
            }
        }

        /// <summary>How far away somebody can be and still have seen it.</summary>
        public const float SightRadius = 12f;

        /// <summary>
        /// The most people one event can put on the list.
        ///
        /// Without this a breakdown in the middle of a twenty-colonist dining hall banks nineteen
        /// witnesses at once and the colonist is at the bottom stage before anybody has had a
        /// chance to speak to them. Shame is not arithmetic about how many pairs of eyes there
        /// were; the nearest few are enough to make the point.
        /// </summary>
        public const int MaxWitnessesPerEvent = 3;

        /// <summary>The most people the list can hold at all, oldest and lightest dropped first.</summary>
        public const int MaxWitnesses = 12;

        /// <summary>
        /// A ceiling on the total.
        ///
        /// Nothing here decays, so without a cap a long colony is a number that only ever goes up.
        /// The stages top out well below this; the headroom exists so that a colonist at the
        /// bottom has something left to repair rather than an unmovable wall.
        /// </summary>
        public const float MaxTotal = 80f;

        /// <summary>
        /// A ceiling on what any one person can be carrying.
        ///
        /// Without it, the colonist who has watched somebody come apart twenty times holds a
        /// number no amount of talking could ever work off, which turns the one route out of this
        /// into a dead end for exactly the person most likely to be offered it.
        /// </summary>
        public const float MaxPerWitness = 30f;

        /// <summary>Thresholds for the three stages of SH_Shame.</summary>
        public static readonly float[] Stages = { 8f, 25f, 50f };

        /// <summary>Thresholds for the three stages of SH_TheySawMe, on one witness's own weight.</summary>
        public static readonly float[] WitnessStages = { 0f, 10f, 22f };

        /// <summary>
        /// A witness who is already on the list counts for less the next time.
        ///
        /// The person who has seen it before is not a fresh humiliation, and without this the mod
        /// has a spiral in it: shame drops the colonist's opinion of a witness, a low opinion makes
        /// a social fight with that witness likelier, and a social fight is another exposure in
        /// front of the same person.
        /// </summary>
        public const float RepeatFactor = 0.5f;

        /// <summary>How often the ledger prunes the dead and the departed.</summary>
        public const int CheckInterval = 2500;
    }

    [StaticConstructorOnStartup]
    public static class SH
    {
        static SH()
        {
            new Harmony("shame.witness").PatchAll();
            Bridges.Connect();
            Log.Message("[Shame] Loaded. Keeping the list of who was there." + Bridges.Report());
        }

        /// <summary>Somebody this mod has an opinion about at all.</summary>
        public static bool Counts(Pawn p) =>
            p != null && !p.Dead && p.needs?.mood != null &&
            p.IsColonist && !p.IsPrisoner && p.RaceProps.Humanlike &&
            !p.DevelopmentalStage.Juvenile();

        /// <summary>
        /// Who cannot be ashamed.
        ///
        /// A Psychopath has no picture of how they appear to anybody, so there is nothing for a
        /// witness to have damaged. A full Sociopath has one, but only for the few people inside
        /// their circle -- and the mod handles that where witnesses are chosen rather than here,
        /// because a Sociopath absolutely can be ashamed in front of the two people who matter.
        ///
        /// A full Narcissist is the interesting one and is NOT excluded here. They do not carry
        /// the mood - see ThoughtWorker_Shame - but they keep every witness on the list. The
        /// humiliation does not land on them. It lands on the person who watched it.
        /// </summary>
        public static bool CanBeAshamed(Pawn p)
        {
            if (!Counts(p))
            {
                return false;
            }
            if (p.story?.traits != null && p.story.traits.HasTrait(TraitDefOf.Psychopath))
            {
                return false;
            }
            // Somebody who has genuinely come through something has stopped being the worst
            // evening of their life. Same grace note Resentment gives the same trait.
            return !Bridges.HasComeThroughIt(p);
        }

        // ---------- did the memory actually land ----------
        //
        // TryGainMemory has three outcomes, not two: added, merged into an existing memory, or
        // refused. A merge discards the object that was offered, so the obvious check -- is my
        // object in the list -- reports a merge as a refusal. Four mods in this set had that bug.
        // The merge is therefore watched directly, from a patch on the merge itself.

        private static readonly System.Collections.Generic.HashSet<Thought_Memory> mergedOffers =
            new System.Collections.Generic.HashSet<Thought_Memory>();

        private const int MaxPendingMerges = 64;

        public static void NoteMerged(Thought_Memory offered)
        {
            if (offered == null)
            {
                return;
            }
            if (mergedOffers.Count > MaxPendingMerges)
            {
                mergedOffers.Clear();
            }
            mergedOffers.Add(offered);
        }

        /// <summary>True if the colonist took it on, whether added outright or merged into one they had.</summary>
        public static bool WasAccepted(MemoryThoughtHandler handler, Thought_Memory offered)
        {
            if (offered == null)
            {
                return false;
            }
            // Removed rather than read, so one merge cannot be counted twice.
            if (mergedOffers.Remove(offered))
            {
                return true;
            }
            System.Collections.Generic.List<Thought_Memory> memories = handler?.Memories;
            return memories != null && memories.Contains(offered);
        }

        /// <summary>Which stage of a staged threshold table a value falls in, or -1 for none.</summary>
        public static int StageFor(float value, float[] thresholds)
        {
            int stage = -1;
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (value >= thresholds[i])
                {
                    stage = i;
                }
            }
            return stage;
        }
    }
}
