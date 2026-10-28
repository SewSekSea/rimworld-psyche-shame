using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Shame
{
    /// <summary>
    /// The five ways somebody ends up on a list.
    ///
    /// Every one of them is something the base game already does. Nothing here is a new event,
    /// a new job or a new decision -- the mod only stands behind the game noting who was close
    /// enough to see it happen.
    /// </summary>
    public static class Sources
    {
        /// <summary>
        /// Coming apart where people could see.
        ///
        /// Restricted to breakdowns that are about the colonist -- mood-caused, or the aggressive
        /// ones. A colonist fleeing a burning base is not humiliated by it, and a state forced by
        /// a mod for its own reasons is not this mod's business.
        /// </summary>
        public static void Breakdown(Pawn p, MentalStateDef def, bool causedByMood, Pawn otherPawn)
        {
            if (p == null || def == null)
            {
                return;
            }
            if (def == MentalStateDefOf.SocialFighting)
            {
                // A row in the kitchen. Both of them are in it and each is the other's witness,
                // so this fires once from each side and neither has to be told about the other.
                GameComponent_Shame.Instance?.Record(p, Exposure.Fight, otherPawn);
                return;
            }
            if (!causedByMood && !def.IsAggro)
            {
                return;
            }
            GameComponent_Shame.Instance?.Record(p, Exposure.Breakdown, null, def.IsExtreme ? 1.5f : 1f);
        }
    }

    /// <summary>
    /// Breakdowns and social fights.
    ///
    /// A postfix, and it checks the handler rather than the return value: a prefix from another
    /// mod can cancel the original and every postfix still runs, so the only honest question is
    /// whether the colonist is actually in the state now.
    /// </summary>
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_TryStartMentalState
    {
        public static void Postfix(MentalStateHandler __instance, MentalStateDef stateDef,
            bool causedByMood, Pawn otherPawn, bool __result, Pawn ___pawn)
        {
            if (!__result || !__instance.InMentalState || __instance.CurStateDef != stateDef)
            {
                return;
            }
            Sources.Breakdown(___pawn, stateDef, causedByMood, otherPawn);
        }
    }

    /// <summary>
    /// Conversations, in both directions.
    ///
    /// An insult puts the person who delivered it, and anyone standing close enough to hear, onto
    /// the list. An ordinary decent conversation takes some of it off -- but only when the person
    /// starting it is one of the people already on the list. Somebody who was not there is not
    /// contradicting anything, however kind they are being.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class Patch_TryInteractWith
    {
        public static void Postfix(Pawn recipient, InteractionDef intDef, bool __result, Pawn ___pawn)
        {
            if (!__result || intDef == null || recipient == null || ___pawn == null)
            {
                return;
            }
            GameComponent_Shame ledger = GameComponent_Shame.Instance;
            if (ledger == null)
            {
                return;
            }

            if (intDef == InteractionDefOf.Insult)
            {
                ledger.Record(recipient, Exposure.Insulted, ___pawn);
                return;
            }

            // The initiator has to be the one who was there. The shamed colonist going over to
            // somebody does not count -- that is them trying, and trying is not the thing that
            // works here. It has to be the other person deciding to come.
            float worth = 0f;
            if (intDef == InteractionDefOf.DeepTalk)
            {
                worth = Tuning.Repair * 2f;
            }
            else if (intDef == InteractionDefOf.Chitchat)
            {
                worth = Tuning.Repair;
            }
            if (worth > 0f)
            {
                ledger.Ease(recipient, ___pawn, worth);
            }
        }
    }

    /// <summary>
    /// The memories the base game hands out, read for four more things.
    ///
    /// Three of them put somebody on a list: being turned down (a romance rebuffed or a proposal
    /// refused), being carried in unconscious, and a surgeon opening somebody up and making it
    /// worse. The fourth runs the other way -- the rest of this set writing a memory that means
    /// one person went and sat with another.
    ///
    /// Like Guilt, this confirms the memory genuinely landed before acting on it. A prefix from
    /// another mod can cancel the original while every postfix still runs, and a colonist whose
    /// mod says they did not feel a rejection should not be quietly humiliated by it here.
    /// </summary>
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory),
        new[] { typeof(Thought_Memory), typeof(Pawn) })]
    [HarmonyAfter("sociopath.trait")]
    public static class Patch_TryGainMemory
    {
        /// <summary>
        /// Marks that this call reached the original method at all. If an earlier prefix cancels,
        /// this never runs and the state arrives false, which is itself the answer.
        /// </summary>
        public static void Prefix(out bool __state)
        {
            __state = true;
        }

        public static void Postfix(MemoryThoughtHandler __instance, Thought_Memory newThought,
            Pawn otherPawn, bool __state)
        {
            if (!__state || newThought?.def == null || __instance?.pawn == null)
            {
                return;
            }
            GameComponent_Shame ledger = GameComponent_Shame.Instance;
            if (ledger == null)
            {
                return;
            }
            Pawn holder = __instance.pawn;
            Pawn other = otherPawn ?? newThought.otherPawn;
            ThoughtDef def = newThought.def;

            if (!Watched.Interesting(def) && !Bridges.IsReachingOut(def))
            {
                return;
            }
            if (!SH.WasAccepted(__instance, newThought))
            {
                return;
            }

            if (Bridges.IsReachingOut(def))
            {
                // Somebody sat with them. Worth more than passing the time of day, because it
                // was not passing: they came over on purpose.
                if (other != null)
                {
                    ledger.Ease(holder, other, Tuning.Repair * 2f);
                }
                return;
            }

            if (def == ThoughtDefOf.BotchedMySurgery)
            {
                // The one that runs backwards. This memory belongs to the patient; the person
                // it is about is the one who has to live with having done it.
                ledger.Record(other, Exposure.Botched, holder);
                return;
            }
            if (def == ThoughtDefOf.RescuedMe || def == ThoughtDefOf.RescuedMeByOfferingHelp)
            {
                ledger.Record(holder, Exposure.Carried, other);
                return;
            }
            if (def == ThoughtDefOf.RebuffedMyRomanceAttempt || def == ThoughtDefOf.RejectedMyProposal)
            {
                ledger.Record(holder, Exposure.Rejected, other);
            }
            // Named rather than left as a fall-through on purpose: anything added to Watched later
            // and not given a case here does nothing, which is noticeable, rather than quietly
            // being filed as a rejection, which is not.
        }
    }

    /// <summary>
    /// Watches for the base game merging an offered memory into an existing one. That path
    /// discards the offered object, so it is the only place a merge can be seen reliably.
    /// </summary>
    [HarmonyPatch(typeof(Thought_Memory), nameof(Thought_Memory.TryMergeWithExistingMemory))]
    public static class Patch_TryMergeWithExistingMemory
    {
        public static void Postfix(Thought_Memory __instance, bool __result)
        {
            if (__result && __instance?.def != null &&
                (Watched.Interesting(__instance.def) || Bridges.IsReachingOut(__instance.def)))
            {
                SH.NoteMerged(__instance);
            }
        }
    }

    /// <summary>Which of the base game's memories this mod has any use for.</summary>
    public static class Watched
    {
        public static bool Interesting(ThoughtDef def)
        {
            if (def == null)
            {
                return false;
            }
            if (Tuning.Rejection &&
                (def == ThoughtDefOf.RebuffedMyRomanceAttempt || def == ThoughtDefOf.RejectedMyProposal))
            {
                return true;
            }
            if (Tuning.Carried &&
                (def == ThoughtDefOf.RescuedMe || def == ThoughtDefOf.RescuedMeByOfferingHelp))
            {
                return true;
            }
            return Tuning.Botched && def == ThoughtDefOf.BotchedMySurgery;
        }
    }
}
