using System;
using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>One button slot on the evidence board.</summary>
    public readonly struct BoardSlot
    {
        public readonly bool Visible;
        public readonly string Id;
        public readonly string Label;
        public readonly bool Selected;

        public BoardSlot(string id, string label, bool selected)
        {
            Visible = true;
            Id = id;
            Label = label;
            Selected = selected;
        }

        public static readonly BoardSlot Empty = default;
    }

    /// <summary>What the evidence board shows: suspect slots, evidence slots (tagged clues), a summary line.</summary>
    public sealed class EvidenceBoardLayout
    {
        public BoardSlot[] Suspects;
        public BoardSlot[] Evidence;
        public string Summary;
        public bool CanSubmit;
        /// <summary>Tagged clues that didn't fit into the evidence slots.</summary>
        public int HiddenEvidence;
    }

    /// <summary>
    /// Pure layout logic for the fixed evidence board (Phase C2): suspects from the case definition, evidence =
    /// tagged clues only (user decision), sorted by display name so slots don't jump around, a selection summary.
    /// </summary>
    public static class EvidenceBoardModel
    {
        public static EvidenceBoardLayout Build(
            IReadOnlyList<SuspectInfo> suspects,
            IEnumerable<string> taggedClueIds,
            Func<string, string> displayName,
            string selectedSuspectId,
            Func<string, bool> isEvidenceSelected,
            int suspectSlots,
            int evidenceSlots,
            int minEvidence)
        {
            var layout = new EvidenceBoardLayout
            {
                Suspects = new BoardSlot[Math.Max(0, suspectSlots)],
                Evidence = new BoardSlot[Math.Max(0, evidenceSlots)],
            };

            string suspectName = null;
            int si = 0;
            if (suspects != null)
            {
                foreach (SuspectInfo s in suspects)
                {
                    if (s == null || string.IsNullOrEmpty(s.id)) continue;
                    string name = string.IsNullOrEmpty(s.displayName) ? s.id : s.displayName;
                    bool selected = s.id == selectedSuspectId;
                    if (selected) suspectName = name;
                    if (si < layout.Suspects.Length) layout.Suspects[si++] = new BoardSlot(s.id, SuspectCard(name, s.role, s.alibi), selected);
                }
            }

            var tagged = new List<(string id, string name)>();
            if (taggedClueIds != null)
            {
                foreach (string id in taggedClueIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    string name = displayName != null ? displayName(id) : null;
                    tagged.Add((id, string.IsNullOrEmpty(name) ? id : name));
                }
            }
            tagged.Sort((a, b) =>
            {
                int c = string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.CompareOrdinal(a.id, b.id);
            });

            int chosen = 0;
            for (int i = 0; i < tagged.Count; i++)
            {
                bool selected = isEvidenceSelected != null && isEvidenceSelected(tagged[i].id);
                if (selected) chosen++;
                if (i < layout.Evidence.Length) layout.Evidence[i] = new BoardSlot(tagged[i].id, tagged[i].name, selected);
            }
            layout.HiddenEvidence = Math.Max(0, tagged.Count - layout.Evidence.Length);

            layout.CanSubmit = selectedSuspectId != null && chosen >= minEvidence;
            string summary = "Suspect: " + (suspectName ?? "-") + "   |   Evidence chosen: " + chosen;
            if (tagged.Count == 0) summary += "\nTag clues in the room to add them here.";
            else if (!layout.CanSubmit) summary += "\nChoose a suspect and at least " + minEvidence + " evidence item(s).";
            if (layout.HiddenEvidence > 0) summary += "\n" + layout.HiddenEvidence + " more tagged clue(s) don't fit - untag some.";
            layout.Summary = summary;
            return layout;
        }

        /// <summary>Suspect card text: "Name - Role" and the alibi on the next line (each part optional).</summary>
        public static string SuspectCard(string name, string role, string alibi)
        {
            string head = string.IsNullOrEmpty(role) ? name : name + " - " + role;
            return string.IsNullOrEmpty(alibi) ? head : head + "\n" + alibi;
        }
    }
}
