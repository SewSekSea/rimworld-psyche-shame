using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;

namespace Shame
{
    /// <summary>One person who was there, and how much of it they are still carrying.</summary>
    public class Mark : IExposable
    {
        public Pawn witness;
        public float weight;

        public Mark()
        {
        }

        public Mark(Pawn witness, float weight)
        {
            this.witness = witness;
            this.weight = weight;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref witness, "witness");
            Scribe_Values.Look(ref weight, "weight", 0f);
        }
    }

    /// <summary>
    /// Everything one colonist is carrying, and the list of who it belongs to.
    ///
    /// The total is kept here rather than added up when it is asked for. The mood thought is
    /// evaluated constantly and the social one is evaluated for every pair in the colony, so a
    /// worker that summed the list every time would be doing real work on every frame for
    /// something that changes a handful of times a day.
    /// </summary>
    public class Sheet : IExposable
    {
        public Pawn subject;
        public List<Mark> marks = new List<Mark>();
        public bool announced;

        private float total;

        public Sheet()
        {
        }

        public Sheet(Pawn subject)
        {
            this.subject = subject;
        }

        public float Total => total;

        public int Count => marks.Count;

        public float WeightOf(Pawn witness)
        {
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i].witness == witness)
                {
                    return marks[i].weight;
                }
            }
            return 0f;
        }

        /// <summary>Add to one witness's weight, or start them off. Returns what they now carry.</summary>
        public float Add(Pawn witness, float amount)
        {
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i].witness == witness)
                {
                    marks[i].weight += amount;
                    if (marks[i].weight > Tuning.MaxPerWitness)
                    {
                        marks[i].weight = Tuning.MaxPerWitness;
                    }
                    Recount();
                    return marks[i].weight;
                }
            }
            marks.Add(new Mark(witness, Mathf.Min(amount, Tuning.MaxPerWitness)));
            TrimToLimit();
            Recount();
            return WeightOf(witness);
        }

        /// <summary>
        /// Take some of it off one witness. Returns true if that was the last of theirs, which is
        /// the moment they stop being somebody who saw and become somebody who stayed.
        /// </summary>
        public bool Ease(Pawn witness, float amount)
        {
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i].witness != witness)
                {
                    continue;
                }
                marks[i].weight -= amount;
                if (marks[i].weight <= 0f)
                {
                    marks.RemoveAt(i);
                    Recount();
                    return true;
                }
                Recount();
                return false;
            }
            return false;
        }

        /// <summary>Take somebody off the list outright, with nothing left behind where they were.</summary>
        public void Drop(Pawn witness)
        {
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                if (marks[i].witness == witness)
                {
                    marks.RemoveAt(i);
                }
            }
            Recount();
        }

        /// <summary>Drop the dead, the departed and anything that resolved to nothing on load.</summary>
        public void Clean()
        {
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                Mark m = marks[i];
                if (m == null || m.witness == null || m.witness.Dead || m.weight <= 0f)
                {
                    marks.RemoveAt(i);
                }
            }
            Recount();
        }

        /// <summary>
        /// Keep the list to a readable length by dropping the lightest.
        ///
        /// Losing the lightest rather than the oldest is deliberate. The person who happened to
        /// walk past once is the one a colonist stops thinking about; the person who has watched
        /// them come apart four times is not, however long ago it started.
        /// </summary>
        private void TrimToLimit()
        {
            while (marks.Count > Tuning.MaxWitnesses)
            {
                int lightest = 0;
                for (int i = 1; i < marks.Count; i++)
                {
                    if (marks[i].weight < marks[lightest].weight)
                    {
                        lightest = i;
                    }
                }
                marks.RemoveAt(lightest);
            }
        }

        public void Recount()
        {
            float sum = 0f;
            for (int i = 0; i < marks.Count; i++)
            {
                sum += marks[i].weight;
            }
            total = sum > Tuning.MaxTotal ? Tuning.MaxTotal : sum;
            if (total < Tuning.Stages[1])
            {
                // Back out of the worst of it, so if they ever go under again it is news again.
                announced = false;
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref subject, "subject");
            Scribe_Collections.Look(ref marks, "marks", LookMode.Deep);
            Scribe_Values.Look(ref announced, "announced", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (marks == null)
                {
                    marks = new List<Mark>();
                }
                Recount();
            }
        }
    }

    /// <summary>
    /// The ledger of who saw what.
    ///
    /// Unlike the rest of the set this cannot be derived from anything the base game records --
    /// RimWorld does not remember who was standing there when somebody came apart. So it is saved,
    /// and adding the mod to a colony in progress starts everyone at nothing. That is honest: the
    /// colonist who broke down last winter did it in front of nobody, as far as this is concerned.
    /// </summary>
    public class GameComponent_Shame : GameComponent
    {
        private static GameComponent_Shame instance;

        private List<Sheet> sheets = new List<Sheet>();

        /// <summary>Derived, never saved. Rebuilt from the list whenever the list changes shape.</summary>
        private readonly Dictionary<Pawn, Sheet> index = new Dictionary<Pawn, Sheet>();

        public GameComponent_Shame(Game game)
        {
            instance = this;
        }

        public static GameComponent_Shame Instance =>
            instance ?? Current.Game?.GetComponent<GameComponent_Shame>();

        public override void FinalizeInit()
        {
            instance = this;
            Prune();
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % Tuning.CheckInterval != 0)
            {
                return;
            }
            Prune();
        }

        // ---------- reading ----------

        public Sheet For(Pawn p)
        {
            if (p == null)
            {
                return null;
            }
            return index.TryGetValue(p, out Sheet s) ? s : null;
        }

        public float TotalFor(Pawn p)
        {
            Sheet s = For(p);
            return s == null ? 0f : s.Total;
        }

        public float WeightOf(Pawn subject, Pawn witness)
        {
            Sheet s = For(subject);
            return s == null ? 0f : s.WeightOf(witness);
        }

        public int WitnessCount(Pawn p)
        {
            Sheet s = For(p);
            return s == null ? 0 : s.Count;
        }

        /// <summary>Everyone currently on somebody's list. A copy: callers must not hold the live one.</summary>
        public List<Pawn> WitnessesOf(Pawn p)
        {
            List<Pawn> found = new List<Pawn>();
            Sheet s = For(p);
            if (s == null)
            {
                return found;
            }
            for (int i = 0; i < s.marks.Count; i++)
            {
                if (s.marks[i].witness != null)
                {
                    found.Add(s.marks[i].witness);
                }
            }
            return found;
        }

        // ---------- writing ----------

        /// <summary>
        /// Something happened where people could see it.
        ///
        /// <paramref name="certain"/> is somebody who was unquestionably there whatever the
        /// geometry says -- the surgeon's patient, the person who carried them in, the one who
        /// turned them down. They are added whether or not they are on the map now.
        /// </summary>
        public void Record(Pawn subject, Exposure kind, Pawn certain = null, float scale = 1f)
        {
            if (!Tuning.Enabled(kind) || !SH.CanBeAshamed(subject))
            {
                return;
            }

            // Build the list of who counts before touching the ledger. Picking witnesses reads
            // the map's colonist list, and that list is a single cached object the game refills on
            // every access -- so it is copied first and never enumerated live. See CODE REVIEW.
            List<Pawn> seen = Seers(subject, kind, certain);
            if (seen.Count == 0)
            {
                return;
            }

            Sheet sheet = SheetFor(subject);
            float baseWeight = Tuning.BaseWeight(kind) * Tuning.Severity * scale;
            for (int i = 0; i < seen.Count; i++)
            {
                Pawn w = seen[i];
                float amount = baseWeight * Closeness(subject, w);
                if (sheet.WeightOf(w) > 0f)
                {
                    amount *= Tuning.RepeatFactor;
                }
                sheet.Add(w, amount);
            }

            Announce(subject, sheet, kind);
            Dirty(subject);
        }

        /// <summary>
        /// Somebody who was there came back anyway.
        ///
        /// This is the only thing in the mod that takes anything off. It has to be one of the
        /// people who saw: kindness from somebody who was not there does nothing here, because
        /// they are not contradicting anything.
        /// </summary>
        public bool Ease(Pawn subject, Pawn witness, float amount)
        {
            Sheet s = For(subject);
            if (s == null || witness == null || amount <= 0f || s.WeightOf(witness) <= 0f)
            {
                return false;
            }
            bool cleared = s.Ease(witness, amount);
            if (cleared && subject.needs?.mood != null && !witness.Dead)
            {
                subject.needs.mood.thoughts.memories.TryGainMemory(SH_DefOf.SH_TheyStayed, witness);
            }
            if (s.Count == 0)
            {
                index.Remove(subject);
                sheets.Remove(s);
            }
            Dirty(subject);
            return cleared;
        }

        /// <summary>Wipe somebody's list entirely. Used when a colonist stops being able to hold any of it.</summary>
        public void Clear(Pawn subject)
        {
            Sheet s = For(subject);
            if (s == null)
            {
                return;
            }
            index.Remove(subject);
            sheets.Remove(s);
            Dirty(subject);
        }

        /// <summary>
        /// Housekeeping, in two passes, and the order is not optional.
        ///
        /// Pass one is the books and reads nothing outside them: the dead, the departed and
        /// anything that resolved to nothing on load. Pass two asks another mod a question that
        /// costs an opinion read, and reading an opinion makes the game evaluate every installed
        /// mod's social thought workers -- including this one's, which is about to be asked for
        /// the very table being rebuilt. So pass two runs only once pass one has finished and the
        /// index is complete, works out everything it wants to change without changing anything,
        /// and then applies the lot. That is the rule the September review produced, and three of
        /// the four bugs it found were this exact shape.
        ///
        /// Exposed so the tests can force the housekeeping without waiting on the tick.
        /// </summary>
        public void Prune()
        {
            // ---- pass one: the books ----
            index.Clear();
            if (sheets == null)
            {
                sheets = new List<Sheet>();
            }
            for (int i = sheets.Count - 1; i >= 0; i--)
            {
                Sheet s = sheets[i];
                if (s == null || s.subject == null || s.subject.Dead)
                {
                    sheets.RemoveAt(i);
                    continue;
                }
                s.Clean();
                if (s.Count == 0)
                {
                    sheets.RemoveAt(i);
                    continue;
                }
                index[s.subject] = s;
            }

            // ---- pass two: who has stopped counting ----
            //
            // A colonist can become a full Sociopath after the fact -- Squad PTSD hardens people
            // into it -- and somebody who has stopped being able to care what a person thinks
            // should stop carrying what that person saw, rather than going on paying for it.
            // Asking whose opinion is still real to them is Sociopath's question, and answering it
            // costs an opinion read, which is why it happens here and nowhere near a thought worker.
            List<Sheet> settled = new List<Sheet>(sheets);
            List<Sheet> dropFrom = null;
            List<Pawn> dropWho = null;
            for (int i = 0; i < settled.Count; i++)
            {
                Sheet s = settled[i];
                if (!Bridges.IsFullSociopath(s.subject))
                {
                    continue;
                }
                for (int m = 0; m < s.marks.Count; m++)
                {
                    Pawn w = s.marks[m].witness;
                    if (w == null || Bridges.InsideTheirCircle(s.subject, w))
                    {
                        continue;
                    }
                    if (dropFrom == null)
                    {
                        dropFrom = new List<Sheet>();
                        dropWho = new List<Pawn>();
                    }
                    dropFrom.Add(s);
                    dropWho.Add(w);
                }
            }
            if (dropFrom == null)
            {
                return;
            }
            for (int i = 0; i < dropFrom.Count; i++)
            {
                // Dropped outright rather than eased off: a colonist who stopped being able to
                // care is not the same as a witness who came back, and only the second of those
                // earns anybody a good memory.
                dropFrom[i].Drop(dropWho[i]);
            }
            for (int i = sheets.Count - 1; i >= 0; i--)
            {
                if (sheets[i].Count == 0)
                {
                    index.Remove(sheets[i].subject);
                    sheets.RemoveAt(i);
                }
            }
        }

        // ---------- the work ----------

        private Sheet SheetFor(Pawn subject)
        {
            if (index.TryGetValue(subject, out Sheet s))
            {
                return s;
            }
            s = new Sheet(subject);
            sheets.Add(s);
            index[subject] = s;
            return s;
        }

        /// <summary>
        /// Who could see it.
        ///
        /// The map's colonist list is copied before anything else happens. That property hands out
        /// one cached list per faction and clears it on every read, so a loop over the live one
        /// that does anything at all is a crash waiting for the right tick -- it took Mettle down
        /// in play.
        /// </summary>
        private static List<Pawn> Seers(Pawn subject, Exposure kind, Pawn certain)
        {
            List<Pawn> found = new List<Pawn>();

            if (certain != null && certain != subject && SH.Counts(certain) && Admits(subject, certain, kind))
            {
                found.Add(certain);
            }

            Map map = subject.Map;
            if (!subject.Spawned || map == null)
            {
                return found;
            }

            // Read from AllPawnsSpawned, not FreeColonistsSpawned, and this is not a style choice.
            // FreeColonistsSpawned hands out one cached list per faction and calls Clear() on it
            // on every read, so reading it from in here would invalidate the enumerator of anybody
            // ELSE who happened to be looping it when the thing that triggered this happened -- and
            // several mods in this set loop it while handing out memories, which is one of the
            // routes into this code. AllPawnsSpawned is the real backing list and touching it
            // disturbs nothing. SH.Counts does the filtering that the other property would have.
            List<Pawn> nearby = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
            List<Pawn> eligible = new List<Pawn>();
            for (int i = 0; i < nearby.Count; i++)
            {
                Pawn other = nearby[i];
                if (other == subject || found.Contains(other) || !SH.Counts(other) || !other.Awake())
                {
                    continue;
                }
                if (other.Position.DistanceTo(subject.Position) > Tuning.SightRadius)
                {
                    continue;
                }
                if (!GenSight.LineOfSight(subject.Position, other.Position, map))
                {
                    continue;
                }
                if (!Admits(subject, other, kind))
                {
                    continue;
                }
                eligible.Add(other);
            }

            // The nearest few. Somebody across the room saw less of it than the person beside them,
            // and a colonist does not carry nineteen separate humiliations out of one evening.
            eligible.Sort((a, b) => a.Position.DistanceToSquared(subject.Position)
                .CompareTo(b.Position.DistanceToSquared(subject.Position)));
            for (int i = 0; i < eligible.Count && found.Count < Tuning.MaxWitnessesPerEvent; i++)
            {
                found.Add(eligible[i]);
            }
            return found;
        }

        /// <summary>
        /// Does this person's having seen it actually cost the subject anything?
        ///
        /// Two exemptions, both of them the same idea from different mods: being seen by somebody
        /// who has already seen the worst of you and did not leave is not an exposure. Squad knows
        /// who stood next to whom in a fight. A Sociopath, by their own mod's account, has a
        /// handful of people who are real to them and nobody else -- everyone outside that is
        /// furniture, and furniture cannot humiliate anybody.
        /// </summary>
        private static bool Admits(Pawn subject, Pawn other, Exposure kind)
        {
            if (Bridges.IsFullSociopath(subject) && !Bridges.InsideTheirCircle(subject, other))
            {
                return false;
            }
            if (kind == Exposure.Breakdown && Bridges.FoughtTogether(subject, other))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// How much worse it is that it was this particular person.
        ///
        /// Deliberately built from relations and crew membership rather than from opinion. Reading
        /// an opinion makes the game evaluate every installed mod's social thought workers,
        /// including this mod's own, in the middle of writing the very table they read.
        /// </summary>
        private static float Closeness(Pawn subject, Pawn witness)
        {
            float f = 1f;
            if (subject.relations != null && witness.relations != null)
            {
                if (LovePartnerRelationUtility.LovePartnerRelationExists(subject, witness) ||
                    subject.relations.FamilyByBlood.Contains(witness))
                {
                    f = 1.5f;
                }
            }
            if (f < 1.3f && Bridges.SameCrew(subject, witness))
            {
                f = 1.3f;
            }
            return f;
        }

        /// <summary>
        /// One letter, the first time a colonist goes under.
        ///
        /// Everything in this mod happens quietly in the social tab, which is exactly the way a
        /// player never finds out it is there. The letter is the one moment it says so out loud.
        /// </summary>
        private static void Announce(Pawn subject, Sheet sheet, Exposure kind)
        {
            if (sheet.announced || sheet.Total < Tuning.Stages[2])
            {
                return;
            }
            sheet.announced = true;
            if (!PawnUtility.ShouldSendNotificationAbout(subject))
            {
                return;
            }
            string what;
            switch (kind)
            {
                case Exposure.Breakdown: what = "falling apart"; break;
                case Exposure.Fight: what = "screaming at somebody"; break;
                case Exposure.Insulted: what = "being spoken to like that"; break;
                case Exposure.Rejected: what = "being turned down"; break;
                case Exposure.Carried: what = "being carried in"; break;
                default: what = "the whole thing"; break;
            }
            Find.LetterStack.ReceiveLetter(
                "Ashamed: " + subject.LabelShortCap,
                subject.LabelShortCap + " has stopped treating it as something that happened and started " +
                "treating it as something they are.\n\n" +
                "Too many people have seen too much - the last of it was " + what + " - and " +
                subject.LabelShort + " now finds it hard to be in a room with any of them.\n\n" +
                "Time will not touch this. It does not fade and there is nothing to confess. The only " +
                "thing that takes any of it off is one of the people who was there seeking " +
                subject.LabelShort + " out and talking to them anyway. Check the social tab to see who is on the list.",
                LetterDefOf.NegativeEvent, subject);
        }

        /// <summary>
        /// Make the game look again.
        ///
        /// Situational thoughts are cached for a hundred ticks and the social ones are cached per
        /// pair. Without this a colonist carries on at yesterday's figure for a while after
        /// something has visibly just happened to them.
        /// </summary>
        private static void Dirty(Pawn subject)
        {
            subject?.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref sheets, "sheets", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (sheets == null)
                {
                    sheets = new List<Sheet>();
                }
                Prune();
            }
        }
    }
}
