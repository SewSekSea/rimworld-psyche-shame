using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Shame
{
    /// <summary>
    /// What else is installed.
    ///
    /// None of it is required. The mod is whole on its own -- the base game supplies breakdowns,
    /// social fights, insults, rejections and rescues, which is every source it has. What the rest
    /// of the set adds is judgement about WHO counts as a witness, and that is where all the
    /// interesting answers live: Squad knows who was in it with you, Sociopath knows whose opinion
    /// is real to you, Crew knows who your own people are, Solace and Mettle know who came back.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Bridges
    {
        private static TraitDef weathered;
        private static TraitDef sociopath;
        private static TraitDef narcissist;
        private static ThoughtDef foughtBeside;

        /// <summary>Memories from other mods in the set that mean somebody came and sat with them.</summary>
        private static readonly List<ThoughtDef> reachedOut = new List<ThoughtDef>();

        private static MethodInfo innerCircle;
        private static MethodInfo sameCrew;

        private static int found;

        public static void Connect()
        {
            found = 0;
            reachedOut.Clear();

            weathered = DefDatabase<TraitDef>.GetNamedSilentFail("MT_Weathered");
            if (weathered != null) { found++; }

            sociopath = DefDatabase<TraitDef>.GetNamedSilentFail("SP_Sociopath");
            narcissist = DefDatabase<TraitDef>.GetNamedSilentFail("NR_Narcissist");
            if (narcissist != null) { found++; }

            foughtBeside = DefDatabase<ThoughtDef>.GetNamedSilentFail("SQ_FoughtBeside");
            if (foughtBeside != null) { found++; }

            // Being comforted by somebody who was there, and being sat with by somebody who has
            // been through it themselves. Both are the same thing as far as this mod is concerned:
            // one of the people who saw came back of their own accord.
            //
            // SL_HollowComfort is deliberately not in this list. Solace's own account of it is that
            // it lifts the mood and heals nothing underneath, and this is the underneath.
            Adopt("SL_Comforted");
            Adopt("MT_SatWithMe");

            try
            {
                Type sp = AccessTools.TypeByName("Sociopath.SP");
                if (sp != null)
                {
                    innerCircle = sp.GetMethod("InInnerCircle",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Pawn), typeof(Pawn) }, null);
                    if (innerCircle != null) { found++; }
                }
            }
            catch
            {
                innerCircle = null;
            }

            try
            {
                Type crews = AccessTools.TypeByName("Crew.GameComponent_Crews");
                if (crews != null)
                {
                    sameCrew = crews.GetMethod("SameCrew",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Pawn), typeof(Pawn) }, null);
                    if (sameCrew != null) { found++; }
                }
            }
            catch
            {
                sameCrew = null;
            }
        }

        private static void Adopt(string defName)
        {
            ThoughtDef d = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (d != null)
            {
                reachedOut.Add(d);
                found++;
            }
        }

        public static string Report() =>
            found == 0
                ? " Running alone: breakdowns and rejections are enough for it."
                : $" {found} of the set found: it will know better who was really watching.";

        /// <summary>
        /// Somebody who has come through the worst of it.
        ///
        /// The same grace note Resentment gives the same trait, for the same reason. Shame is the
        /// belief that the worst evening of your life is the true one, and a colonist who has
        /// genuinely come out the other side of something has the one piece of evidence that
        /// contradicts it. They were there too, and they know how it ended.
        /// </summary>
        public static bool HasComeThroughIt(Pawn p) =>
            weathered != null && p?.story?.traits != null && p.story.traits.HasTrait(weathered);

        /// <summary>
        /// A full Sociopath. Not the mild degree -- that one still has a working sense of how
        /// they look to people.
        /// </summary>
        public static bool IsFullSociopath(Pawn p)
        {
            if (sociopath == null || p?.story?.traits == null)
            {
                return false;
            }
            Trait t = p.story.traits.GetTrait(sociopath);
            return t != null && t.Degree >= 2;
        }

        /// <summary>
        /// A full Narcissist. They keep the list -- see ThoughtWorker_Shame for what they do
        /// with it instead of carrying it.
        /// </summary>
        public static bool IsFullNarcissist(Pawn p)
        {
            if (narcissist == null || p?.story?.traits == null)
            {
                return false;
            }
            Trait t = p.story.traits.GetTrait(narcissist);
            return t != null && t.Degree >= 2;
        }

        /// <summary>
        /// Is this person real to the sociopath?
        ///
        /// Without Sociopath installed the question does not arise and everyone counts, which is
        /// the right default -- it is the base game's answer.
        /// </summary>
        public static bool InsideTheirCircle(Pawn sociopathPawn, Pawn other)
        {
            if (innerCircle == null)
            {
                return true;
            }
            try
            {
                return (bool)innerCircle.Invoke(null, new object[] { sociopathPawn, other });
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Your own people seeing it is worse than a stranger seeing it.</summary>
        public static bool SameCrew(Pawn a, Pawn b)
        {
            if (sameCrew == null || a == null || b == null)
            {
                return false;
            }
            try
            {
                return (bool)sameCrew.Invoke(null, new object[] { a, b });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Did these two stand next to each other in a fight?
        ///
        /// Somebody who has already seen you at the worst moment of your life and stayed where
        /// they were is not a fresh humiliation. Squad writes a memory when colonists fight side
        /// by side, and where it exists a breakdown does not count as witnessed by that person.
        /// </summary>
        public static bool FoughtTogether(Pawn a, Pawn b)
        {
            if (foughtBeside == null || a?.needs?.mood == null || b == null)
            {
                return false;
            }
            List<Thought_Memory> memories = a.needs.mood.thoughts.memories.Memories;
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i].def == foughtBeside && memories[i].otherPawn == b)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Is this memory another mod in the set telling us somebody went to them?
        ///
        /// Read as data rather than by calling into those mods, so it costs nothing when they are
        /// absent and cannot break when they change.
        /// </summary>
        public static bool IsReachingOut(ThoughtDef def)
        {
            for (int i = 0; i < reachedOut.Count; i++)
            {
                if (reachedOut[i] == def)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
