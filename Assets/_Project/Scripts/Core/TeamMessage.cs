using System;
using System.Collections.Generic;
using System.Globalization;

namespace FYP.Detective
{
    /// <summary>Kinds of team-state messages. Req* go client → host; the others go host → clients.</summary>
    public enum TeamMessageKind
    {
        ReqStart, ReqEnd, ReqFound, ReqTag, ReqSubmit, ReqOwnership,
        Start, End, Found, Tag, Submit,
        /// <summary>A contamination entry of the sender's body (both directions).</summary>
        Contamination,
    }

    /// <summary>
    /// One team-state message, as plain text "kind|field|field..." (sent as a Netcode named message). Ids may not
    /// contain '|' or ','; <see cref="TryParse"/> rejects anything malformed. Pure C#, unit tested.
    /// Fields per kind:
    ///   ReqStart                     (none)
    ///   ReqEnd / End                 reason
    ///   ReqFound / Found             clue, by
    ///   ReqTag / Tag                 clue, 1|0, by
    ///   ReqSubmit / Submit           by, suspect, evidence (comma-separated, may be empty)
    ///   ReqOwnership                 clue
    ///   Start                        caseCode, conditionCode
    ///   Contamination                zone, bodyPart, by
    /// </summary>
    public sealed class TeamMessage
    {
        public const string Name = "fyp.team";

        public TeamMessageKind Kind { get; private set; }
        public string ClueId { get; private set; }
        public string By { get; private set; }
        public bool Tagged { get; private set; }
        public SessionEndReason Reason { get; private set; }
        public string SuspectId { get; private set; }
        public IReadOnlyList<string> EvidenceIds { get; private set; } = Array.Empty<string>();
        public string CaseCode { get; private set; }
        public string ConditionCode { get; private set; }
        public string ZoneId { get; private set; }
        public BodyPart Part { get; private set; }

        private static readonly char[] Forbidden = { '|', ',' };

        // ---- Builders ----

        public static string ReqStart() => Join(TeamMessageKind.ReqStart);
        public static string ReqEnd(SessionEndReason r) => Join(TeamMessageKind.ReqEnd, r.ToString());
        public static string End(SessionEndReason r) => Join(TeamMessageKind.End, r.ToString());
        public static string ReqFound(string clue, string by) => Join(TeamMessageKind.ReqFound, Id(clue), Id(by));
        public static string Found(string clue, string by) => Join(TeamMessageKind.Found, Id(clue), Id(by));
        public static string ReqTag(string clue, bool tagged, string by) => Join(TeamMessageKind.ReqTag, Id(clue), tagged ? "1" : "0", Id(by));
        public static string Tag(string clue, bool tagged, string by) => Join(TeamMessageKind.Tag, Id(clue), tagged ? "1" : "0", Id(by));
        public static string ReqSubmit(string by, string suspect, IEnumerable<string> evidence) => Join(TeamMessageKind.ReqSubmit, Id(by), Id(suspect), Ids(evidence));
        public static string Submit(string by, string suspect, IEnumerable<string> evidence) => Join(TeamMessageKind.Submit, Id(by), Id(suspect), Ids(evidence));
        public static string ReqOwnership(string clue) => Join(TeamMessageKind.ReqOwnership, Id(clue));
        public static string Start(string caseCode, string conditionCode) => Join(TeamMessageKind.Start, Id(caseCode), Id(conditionCode));
        public static string Contamination(string zone, BodyPart part, string by) => Join(TeamMessageKind.Contamination, Id(zone), part.ToString(), Id(by));

        /// <summary>True if <paramref name="id"/> can be sent (non-empty, no '|' or ',').</summary>
        public static bool IsSendableId(string id) => !string.IsNullOrEmpty(id) && id.IndexOfAny(Forbidden) < 0;

        // ---- Parser ----

        public static bool TryParse(string text, out TeamMessage message)
        {
            message = null;
            if (string.IsNullOrEmpty(text)) return false;
            string[] f = text.Split('|');
            if (!Enum.TryParse(f[0], false, out TeamMessageKind kind) || !Enum.IsDefined(typeof(TeamMessageKind), kind)) return false;
            if (int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return false;   // "3" isn't a name
            var m = new TeamMessage { Kind = kind };
            switch (kind)
            {
                case TeamMessageKind.ReqStart:
                    if (f.Length != 1) return false;
                    break;
                case TeamMessageKind.ReqEnd:
                case TeamMessageKind.End:
                    if (f.Length != 2 || !TryReason(f[1], out SessionEndReason r)) return false;
                    m.Reason = r;
                    break;
                case TeamMessageKind.ReqFound:
                case TeamMessageKind.Found:
                    if (f.Length != 3 || !IsSendableId(f[1]) || !IsSendableId(f[2])) return false;
                    m.ClueId = f[1]; m.By = f[2];
                    break;
                case TeamMessageKind.ReqTag:
                case TeamMessageKind.Tag:
                    if (f.Length != 4 || !IsSendableId(f[1]) || (f[2] != "1" && f[2] != "0") || !IsSendableId(f[3])) return false;
                    m.ClueId = f[1]; m.Tagged = f[2] == "1"; m.By = f[3];
                    break;
                case TeamMessageKind.ReqSubmit:
                case TeamMessageKind.Submit:
                    if (f.Length != 4 || !IsSendableId(f[1]) || !IsSendableId(f[2])) return false;
                    m.By = f[1]; m.SuspectId = f[2];
                    m.EvidenceIds = f[3].Length == 0 ? Array.Empty<string>() : f[3].Split(',');
                    foreach (string e in m.EvidenceIds) if (e.Length == 0) return false;
                    break;
                case TeamMessageKind.ReqOwnership:
                    if (f.Length != 2 || !IsSendableId(f[1])) return false;
                    m.ClueId = f[1];
                    break;
                case TeamMessageKind.Start:
                    if (f.Length != 3 || !IsSendableId(f[1]) || !IsSendableId(f[2])) return false;
                    m.CaseCode = f[1]; m.ConditionCode = f[2];
                    break;
                case TeamMessageKind.Contamination:
                    if (f.Length != 4 || !IsSendableId(f[1]) || !Enum.TryParse(f[2], false, out BodyPart part)
                        || !Enum.IsDefined(typeof(BodyPart), part) || !IsSendableId(f[3])) return false;
                    m.ZoneId = f[1]; m.Part = part; m.By = f[3];
                    break;
                default:
                    return false;
            }
            message = m;
            return true;
        }

        private static bool TryReason(string s, out SessionEndReason r)
        {
            return Enum.TryParse(s, false, out r) && Enum.IsDefined(typeof(SessionEndReason), r)
                && !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        private static string Join(TeamMessageKind kind, params string[] fields)
        {
            return fields.Length == 0 ? kind.ToString() : kind + "|" + string.Join("|", fields);
        }

        private static string Id(string id)
        {
            if (!IsSendableId(id)) throw new ArgumentException("Id must be non-empty without '|' or ',': " + id);
            return id;
        }

        private static string Ids(IEnumerable<string> ids)
        {
            if (ids == null) return string.Empty;
            var list = new List<string>();
            foreach (string id in ids) list.Add(Id(id));
            return string.Join(",", list);
        }
    }
}
