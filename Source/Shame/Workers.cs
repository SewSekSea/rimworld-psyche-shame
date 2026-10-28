using RimWorld;
using Verse;

namespace Shame
{
    /// <summary>
    /// How much of it a colonist is carrying, all told.
    ///
    /// Nothing in here decays. There is no clock anywhere in this mod. Guilt is a ledger of deeds
    /// and deeds recede; this is a ledger of people, and a person standing in your kitchen is not
    /// further away than they were last year.
    /// </summary>
    public class ThoughtWorker_Shame : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!SH.CanBeAshamed(p))
            {
                return ThoughtState.Inactive;
            }
            // A full Narcissist keeps every witness -- see ThoughtWorker_TheySawMe, where it
            // lands on them just as hard as on anyone. What they do not do is turn it inward.
            // Humiliation does not become a fact about them; it becomes a fact about whoever
            // was watching. That is the trait, stated from this side.
            if (Bridges.IsFullNarcissist(p))
            {
                return ThoughtState.Inactive;
            }
            GameComponent_Shame ledger = GameComponent_Shame.Instance;
            if (ledger == null)
            {
                return ThoughtState.Inactive;
            }

            int stage = SH.StageFor(ledger.TotalFor(p), Tuning.Stages);
            return stage < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(stage);
        }
    }

    /// <summary>
    /// What one particular person costs them.
    ///
    /// The opinion drops on the shamed colonist's side only. The witness is not told anything and
    /// very often thinks nothing of it at all -- they watched a friend have a bad night. That
    /// one-sidedness is the mod: it walks a colonist away from the only people who could end it.
    /// </summary>
    public class ThoughtWorker_TheySawMe : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (!SH.CanBeAshamed(p) || other == null || p == other)
            {
                return ThoughtState.Inactive;
            }
            GameComponent_Shame ledger = GameComponent_Shame.Instance;
            if (ledger == null)
            {
                return ThoughtState.Inactive;
            }
            float held = ledger.WeightOf(p, other);
            if (held <= 0f)
            {
                return ThoughtState.Inactive;
            }
            int stage = SH.StageFor(held, Tuning.WitnessStages);
            return stage < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(stage);
        }
    }

    /// <summary>Scaled by the settings slider, so a player can decide what being seen costs.</summary>
    public class Thought_Witnessed : Thought_SituationalSocial
    {
        public override float OpinionOffset() => base.OpinionOffset() * Tuning.Power;
    }

    /*
        Both workers above do nothing but look their answer up. That is deliberate and it is
        worth saying why, because the obvious version of this mod does not work.

        A thought worker is asked its question from inside the game's opinion machinery, and
        reading an opinion is what MAKES the game ask it. So anything a worker does that reads an
        opinion -- directly, or by calling into another mod that does -- is a call that comes
        straight back round into the worker that made it. Vanilla happens to stop the loop at
        depth two, because it stamps its cache as fresh before it asks, but that is an accident of
        the implementation and not something to build on.

        The one question this mod would like to ask a worker -- whether a witness is still real to
        a colonist who has since become a full Sociopath -- costs exactly that. So it is not asked
        here. It is asked by the ledger's housekeeping pass, which runs on a timer, nowhere near a
        thought, and which stages its answer before applying it. By the time a worker is asked
        anything, the list it reads is already the finished answer.
    */
}
