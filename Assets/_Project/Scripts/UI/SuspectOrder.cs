using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>
    /// Order of the suspect cards on the evidence board (user, session 6: the culprit was always the leftmost card, so the
    /// order was predictable). A seeded shuffle of the case's suspects: the seed comes from pair code + case + condition, so
    /// both headsets of a team show the same order without any message, the order differs between pairs, cases and
    /// conditions, and a run can be reproduced from its log (<c>board_order</c> row). Pure, unit tested.
    /// </summary>
    public static class SuspectOrder
    {
        public static List<T> Shuffled<T>(IReadOnlyList<T> items, string pairCode, string caseCode, string conditionCode)
        {
            var list = new List<T>();
            if (items == null) return list;
            for (int i = 0; i < items.Count; i++) list.Add(items[i]);
            var random = new System.Random(Seed(pairCode, caseCode, conditionCode));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
            return list;
        }

        /// <summary>FNV-1a over "pair|case|condition": the same on every device (unlike string.GetHashCode).</summary>
        public static int Seed(string pairCode, string caseCode, string conditionCode)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in (pairCode ?? "") + "|" + (caseCode ?? "") + "|" + (conditionCode ?? ""))
                    h = (h ^ c) * 16777619u;
                return (int)h;
            }
        }
    }
}
