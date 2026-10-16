using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>
    /// The two study cases, transcribed from docs/cases.md (keep them in sync). Case X has the reference layout
    /// (start mark = CaseRoot at the centre of the play area, facing the front wall; everything within the central
    /// 3 x 3 m of the 4 x 4 m area); Case Y uses the same layout
    /// mirrored left-right (x -> -x), so the display, desk and door are on the other side.
    /// </summary>
    public static class CaseSpecs
    {
        private static CaseSpec _x, _y;

        public static CaseSpec X => _x ?? (_x = BuildX());
        public static CaseSpec Y => _y ?? (_y = BuildY());

        public static CaseSpec For(CaseId id) => id == CaseId.X ? X : Y;

        // ---- Reference layout (Case X positions; Case Y = mirrored) ----

        private struct Spot
        {
            public float X, Z, Yaw;
            public HeightBand Band;
            public bool Grab;
            public Spot(float x, float z, HeightBand band, float yaw, bool grab) { X = x; Z = z; Band = band; Yaw = yaw; Grab = grab; }
        }

        // Yaw = side people stand on to read/take it (readable side): 180 = towards the start from the front, 90 = towards +x.
        private static readonly Dictionary<ClueSlot, Spot> Layout = new Dictionary<ClueSlot, Spot>
        {
            { ClueSlot.K1, new Spot(-0.75f, 0.78f, HeightBand.Eye, 180f, false) },   // lock panel, right end of the display front
            { ClueSlot.K2, new Spot(1.00f, 0.62f, HeightBand.Table, 180f, true) },   // on the front desk
            { ClueSlot.K3, new Spot(-0.70f, 0.74f, HeightBand.Floor, 180f, true) },  // at the display's front foot (visible)
            { ClueSlot.K4, new Spot(-0.95f, -0.82f, HeightBand.Table, 0f, true) },   // on the office desk
            { ClueSlot.R1, new Spot(-0.50f, 1.15f, HeightBand.Floor, 90f, true) },   // beside the display (right side)
            { ClueSlot.R2, new Spot(0.55f, -1.30f, HeightBand.Floor, 0f, true) },    // near the door
            { ClueSlot.R3, new Spot(-1.35f, -0.12f, HeightBand.Eye, 90f, false) },   // on the monitor/noticeboard
        };

        private static List<PropSpec> Props(string display, string desk, string office, string stand, string door) => new List<PropSpec>
        {
            new PropSpec { Role = "display", Name = display, X = -0.95f, Z = 1.05f, Width = 0.6f, Depth = 0.5f, Height = 1.6f },
            new PropSpec { Role = "desk", Name = desk, X = 1.00f, Z = 0.65f, Width = 0.9f, Depth = 0.5f, Height = PlayArea.TableHeight },
            new PropSpec { Role = "office", Name = office, X = -0.95f, Z = -0.85f, Width = 0.9f, Depth = 0.5f, Height = PlayArea.TableHeight },
            new PropSpec { Role = "stand", Name = stand, X = -1.40f, Z = -0.12f, Width = 0.1f, Depth = 0.4f, Height = 1.7f },
            new PropSpec { Role = "door", Name = door, X = 1.15f, Z = -1.48f, Width = 0.8f, Depth = 0.04f, Height = 2.0f },
        };

        private static List<ZoneSpec> Zones(string prefix, string glass, string spill, string path) => new List<ZoneSpec>
        {
            // In front of the display's left end only, so the lock panel (K1) can be read without reaching over it.
            new ZoneSpec { Id = prefix + "Z1", Name = glass, X = -1.28f, Z = 0.42f, Width = 0.36f, Depth = 0.4f, Yaw = 0f },
            // Beside the desk's inner end, not in front of it, so the desk clue (K2) is reached from the front.
            new ZoneSpec { Id = prefix + "Z2", Name = spill, X = 0.30f, Z = 0.65f, Width = 0.40f, Depth = 0.5f, Yaw = 0f },
            new ZoneSpec { Id = prefix + "Z3", Name = path, X = 0.95f, Z = -0.85f, Width = 0.35f, Depth = 0.9f, Yaw = -40f },
        };

        private static ClueSpec Clue(string prefix, ClueSlot slot, string obj, string text, string pointsAt = null)
        {
            Spot s = Layout[slot];
            return new ClueSpec
            {
                Slot = slot, Id = prefix + slot, ObjectName = obj, InspectText = text, Height = s.Band,
                X = s.X, Z = s.Z, FacingYaw = s.Yaw, Grabbable = s.Grab, PointsAtSuspectId = pointsAt,
            };
        }

        private static CaseSpec BuildX()
        {
            var c = new CaseSpec
            {
                Case = CaseId.X,
                Title = "The Missing Design Award",
                Briefing = "Last night, the gold Design of the Year award vanished from the display case at Kite Design Studio, " +
                           "just hours before the company's big exhibition. The case was found open this morning. Four people had " +
                           "a reason to be near the studio last night. Search the room, tag the evidence, and name the person who " +
                           "took the award.",
                BoardX = 0f, BoardZ = -1.45f, BoardYaw = 0f,
            };
            c.Suspects.Add(new SuspectSpec { Id = "X_grace", Name = "Grace Wong", Role = "Studio manager", IsCulprit = true,
                Alibi = "\"I locked up at 7:00 pm and went straight home.\" (No one can confirm.)" });
            c.Suspects.Add(new SuspectSpec { Id = "X_kumar", Name = "Kumar Raj", Role = "Night cleaner",
                Alibi = "Clocked out at 9:05 pm; security confirms he left the building." });
            c.Suspects.Add(new SuspectSpec { Id = "X_lena", Name = "Lena Ortiz", Role = "Rival designer next door",
                Alibi = "At a restaurant 7:00-10:00 pm; the restaurant confirms her booking." });
            c.Suspects.Add(new SuspectSpec { Id = "X_aiman", Name = "Aiman Hakim", Role = "Junior designer",
                Alibi = "At the gym 9:15-10:30 pm; the gym's check-in record confirms it." });

            c.Clues.Add(Clue("X_", ClueSlot.K1, "Display case lock panel", "\"Case opened: 21:42.\""));
            c.Clues.Add(Clue("X_", ClueSlot.K2, "Sign-in sheet", "Last entry: \"G. Wong, in 21:35.\" Someone tried to scribble it out."));
            c.Clues.Add(Clue("X_", ClueSlot.K3, "Staff lanyard clip", "Broken clip with a tag: \"Staff #004, G. Wong.\""));
            c.Clues.Add(Clue("X_", ClueSlot.K4, "Printed email", "From a pawn shop to Grace: \"We can offer RM 15,000 if the award is real gold.\""));
            c.Clues.Add(Clue("X_", ClueSlot.R1, "Cleaning glove", "A yellow rubber glove from the cleaning trolley.", "X_kumar"));
            c.Clues.Add(Clue("X_", ClueSlot.R2, "Torn sketch", "A drawing of the award, signed \"L. Ortiz.\"", "X_lena"));
            c.Clues.Add(Clue("X_", ClueSlot.R3, "Sticky note", "\"This award should have been mine. - A.\"", "X_aiman"));

            c.Zones.AddRange(Zones("X_", "Broken glass", "Spilled coffee", "Muddy footprints"));
            c.Props.AddRange(Props("Display case", "Front desk", "Manager's desk", "Monitor", "Door"));
            return c;
        }

        private static CaseSpec BuildY()
        {
            var c = new CaseSpec
            {
                Case = CaseId.Y,
                Title = "The Missing Keris",
                Briefing = "Yesterday evening, an antique keris disappeared from its display case in the Heritage Gallery's " +
                           "Melaka Room, one week before it was due to go on loan overseas. The case was found open this morning. " +
                           "Four people had a reason to be near the room last night. Search the room, tag the evidence, and name " +
                           "the person who took the keris.",
                BoardX = 0f, BoardZ = -1.45f, BoardYaw = 0f,
            };
            c.Suspects.Add(new SuspectSpec { Id = "Y_farid", Name = "Farid Rahman", Role = "Tour guide", IsCulprit = true,
                Alibi = "\"My last tour ended at 7:30 pm and I went home.\" (No one can confirm.)" });
            c.Suspects.Add(new SuspectSpec { Id = "Y_tan", Name = "Mr Tan", Role = "Security guard",
                Alibi = "On gate duty 8:00-9:00 pm; the front-desk camera shows him there." });
            c.Suspects.Add(new SuspectSpec { Id = "Y_sofia", Name = "Dr Sofia Idris", Role = "Curator",
                Alibi = "Hosting a donor dinner across town 7:00-9:30 pm; the guests confirm it." });
            c.Suspects.Add(new SuspectSpec { Id = "Y_lim", Name = "Mr Lim", Role = "Private collector",
                Alibi = "On a flight that landed at KLIA at 9:00 pm; the airline confirms it." });

            c.Clues.Add(Clue("Y_", ClueSlot.K1, "Display case alarm panel", "\"Case 3 opened: 20:17.\""));
            c.Clues.Add(Clue("Y_", ClueSlot.K2, "Key logbook", "\"Gallery key out 20:05, F.R.\" No return time written."));
            c.Clues.Add(Clue("Y_", ClueSlot.K3, "Laser pointer", "Engraved: \"Farid, Guide Team.\""));
            c.Clues.Add(Clue("Y_", ClueSlot.K4, "Printed chat message", "To Farid: \"Still selling the keris? I can pay cash this week.\""));
            c.Clues.Add(Clue("Y_", ClueSlot.R1, "Torch", "A security-issue torch, battery flat.", "Y_tan"));
            c.Clues.Add(Clue("Y_", ClueSlot.R2, "Business card", "Mr Lim's card; on the back: \"That keris will be mine.\"", "Y_lim"));
            c.Clues.Add(Clue("Y_", ClueSlot.R3, "Noticeboard note", "\"Move keris to storage before loan. - S.\"", "Y_sofia"));

            c.Zones.AddRange(Zones("Y_", "Shattered display glass", "Water from overturned vase", "Wet floor varnish"));
            c.Props.AddRange(Props("Display plinth", "Reception table", "Tour desk", "Noticeboard", "Entrance"));
            Mirror(c);
            return c;
        }

        /// <summary>Case Y layout = Case X mirrored left-right (x -> -x, yaw -> -yaw).</summary>
        private static void Mirror(CaseSpec c)
        {
            foreach (var cl in c.Clues) { cl.X = -cl.X; cl.FacingYaw = -cl.FacingYaw; }
            foreach (var z in c.Zones) { z.X = -z.X; z.Yaw = -z.Yaw; }
            foreach (var p in c.Props) p.X = -p.X;
            c.BoardX = -c.BoardX;
            c.BoardYaw = -c.BoardYaw;
        }
    }
}
