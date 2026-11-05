using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>
    /// The two study cases, transcribed from docs/cases.md (keep them in sync). Case X has the reference layout
    /// (start mark = CaseRoot at the centre of the play area, facing the front wall; everything within the central
    /// 3 x 3 m of the 4 x 4 m area); Case Y has its own murder-scene layout (session 6).
    /// </summary>
    public static class CaseSpecs
    {
        private static CaseSpec _x, _y;

        public static CaseSpec X => _x ?? (_x = BuildX());
        public static CaseSpec Y => _y ?? (_y = BuildY());

        public static CaseSpec For(CaseId id) => id == CaseId.X ? X : Y;

        // ---- Case X layout ----

        private struct Spot
        {
            public float X, Z, Yaw;
            public HeightBand Band;
            public bool Grab;
            public Spot(float x, float z, HeightBand band, float yaw, bool grab) { X = x; Z = z; Band = band; Yaw = yaw; Grab = grab; }
        }

        // Room layout (session 6, for the 5 x 5 m room): furniture stands near the edges like a real room -
        // the display case is the centrepiece at the front, the reception desk on the right, the entrance in the back
        // wall (back-right corner), the office
        // desk and the monitor/noticeboard on the left, the evidence board at the back; the middle stays open.
        // Yaw = side people stand on to read/take it (readable side): 180 = towards the start from the front, 90 = towards +x.
        private static readonly Dictionary<ClueSlot, Spot> Layout = new Dictionary<ClueSlot, Spot>
        {
            { ClueSlot.K1, new Spot(-0.65f, 1.81f, HeightBand.Eye, 90f, false) },    // keypad on the display case's side glass, front corner (lock side, away from the hinge)
            { ClueSlot.K2, new Spot(1.81f, 0.56f, HeightBand.Table, -90f, true) },   // on the reception desk (faces the room)
            { ClueSlot.K3, new Spot(-0.69f, 1.61f, HeightBand.Floor, 180f, true) },  // at the display's front foot
            { ClueSlot.K4, new Spot(-1.81f, -0.94f, HeightBand.Table, 90f, true) },  // on the office desk (faces the room)
            { ClueSlot.R1, new Spot(-0.51f, 1.99f, HeightBand.Floor, 90f, true) },   // beside the display (right side)
            { ClueSlot.R2, new Spot(2.04f, -1.91f, HeightBand.Floor, 0f, true) },    // just inside the entrance
            { ClueSlot.R3, new Spot(-2.20f, 0.31f, HeightBand.Eye, 90f, true) },     // on the monitor / noticeboard (grabbable, snaps back home)
        };

        // Footprints are axis-aligned after turning (desks and the door are turned 90 degrees, so width/depth swap).
        private static List<PropSpec> Props(string display, string desk, string office, string stand, string door) => new List<PropSpec>
        {
            new PropSpec { Role = "display", Name = display, X = -0.94f, Z = 1.94f, Width = 0.6f, Depth = 0.5f, Height = 1.63f, Yaw = 180f },
            new PropSpec { Role = "desk", Name = desk, X = 1.81f, Z = 0.56f, Width = 0.5f, Depth = 0.9f, Height = PlayArea.TableHeight, Yaw = -90f },
            new PropSpec { Role = "office", Name = office, X = -1.81f, Z = -0.94f, Width = 0.5f, Depth = 0.9f, Height = PlayArea.TableHeight, Yaw = 90f },
            new PropSpec { Role = "stand", Name = stand, X = -2.25f, Z = 0.31f, Width = 0.1f, Depth = 0.4f, Height = 1.6f, Yaw = 90f },
            // Entrance in the back wall (back-right corner), away from both desks and the evidence board.
            new PropSpec { Role = "door", Name = door, X = 1.69f, Z = -2.31f, Width = 0.8f, Depth = 0.1f, Height = 2.0f, Yaw = 0f },
        };

        private static List<ZoneSpec> Zones(string prefix, string glass, string spill, string path) => new List<ZoneSpec>
        {
            // In front of the display's left end only, so the lock panel (K1) can be read without reaching over it.
            new ZoneSpec { Id = prefix + "Z1", Name = glass, X = -1.24f, Z = 1.47f, Width = 0.36f, Depth = 0.4f, Yaw = 0f },
            // Beside the desk's front end, not in front of it, so the desk clue (K2) is reached from the room side.
            new ZoneSpec { Id = prefix + "Z2", Name = spill, X = 1.81f, Z = 1.33f, Width = 0.40f, Depth = 0.5f, Yaw = 0f },
            // From the entrance towards the display.
            new ZoneSpec { Id = prefix + "Z3", Name = path, X = 1.36f, Z = -1.78f, Width = 0.35f, Depth = 0.9f, Yaw = -32f },
        };

        private static ClueSpec Clue(string prefix, ClueSlot slot, string obj, string text, string pointsAt = null)
        {
            Spot s = (prefix == "Y_" ? LayoutY : Layout)[slot];
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
                Title = "Last Coffee at Kite",
                Briefing = "Last night, Encik Rizal Hamid, founder of Kite Design Studio, was found dead beside the front desk. " +
                           "The gold Design of the Year award is gone from its smashed display case. It looks like a break-in, " +
                           "but the doctor says he was poisoned. Four people had a reason to be at the studio last night. " +
                           "Search the room, tag the evidence, and name the person who killed him.",
                BoardX = 0f, BoardZ = -2.19f, BoardYaw = 0f,
            };
            c.Suspects.Add(new SuspectSpec { Id = "X_grace", Name = "Grace Wong", Role = "Studio manager", IsCulprit = true,
                Alibi = "\"I locked up at 7:00 pm and was home by 7:30.\"" });
            c.Suspects.Add(new SuspectSpec { Id = "X_kumar", Name = "Kumar Raj", Role = "Night cleaner",
                Alibi = "\"I finished cleaning and left the building at 9:05 pm.\"" });
            c.Suspects.Add(new SuspectSpec { Id = "X_lena", Name = "Lena Ortiz", Role = "Rival designer next door",
                Alibi = "\"I was at dinner nearby from 7 until 10 pm.\"" });
            c.Suspects.Add(new SuspectSpec { Id = "X_aiman", Name = "Aiman Hakim", Role = "Junior designer",
                Alibi = "\"I was at the gym from 9:15 pm until late.\"" });

            c.Clues.Add(Clue("X_", ClueSlot.K1, "Display case lock panel", "\"Case opened: 21:42.\""));
            c.Clues.Add(Clue("X_", ClueSlot.K2, "Sign-in sheet", "Last entry: \"G.W., in 21:35.\" Someone tried to scribble it out."));
            c.Clues.Add(Clue("X_", ClueSlot.K3, "Wet mug", "A mug printed \"BEST MANAGER\", pushed under the display stand. It is still wet: someone rinsed it in a hurry."));
            c.Clues.Add(Clue("X_", ClueSlot.K4, "Printed email", "Email from a pawn shop to studio.manager@kite.my: \"We can offer RM 15,000 if the award is real gold. Bring it after closing.\""));
            c.Clues.Add(Clue("X_", ClueSlot.R1, "Cleaning glove", "A yellow cleaning glove beside the display case. It smells of glass cleaner. Encik Rizal fired Kumar this week.", "X_kumar"));
            c.Clues.Add(Clue("X_", ClueSlot.R2, "Torn sketch", "A torn drawing of the award, signed \"L. Ortiz\". Scrawled across it: \"Rizal stole my design.\"", "X_lena"));
            c.Clues.Add(Clue("X_", ClueSlot.R3, "Sticky note", "\"Rizal promised me that award. Liar. - A.\"", "X_aiman"));

            c.Zones.AddRange(Zones("X_", "Broken glass", "Spilled coffee", "Muddy footprints"));
            c.Props.AddRange(Props("Display case", "Front desk", "Manager's desk", "Monitor", "Door"));
            return c;
        }

        // Case Y "Death in the Melaka Room" (user decision, session 6): its own layout, no longer a mirror of X. The scene
        // tells the story: Datuk Hashim lies in front of the plinth, stabbed with the gallery's keris, which the killer took
        // away (user, 2026-11-04: leaving the weapon was "too dumb"); a scrap torn off his green scarf lies beside the body (K3);
        // blood pool under his head (zone 1), bloody footprints lead from it towards the entrance (zone 2), the vase
        // knocked off its stand (zone 3), the wall clock smashed in the struggle (K1). Position of the body tape: below.
        /// <summary>
        /// Case X (user, 2026-11-05: a poisoning; "the body fell down from the chair"): Encik Rizal sat on the front-desk
        /// chair with his coffee; the chair rolled back and turned (XChair*), he slid off it and lies on his side in front of
        /// it, legs by the chair, head away from the desk (taped outline turned XBodyYaw). Both stay clear of the sign-in
        /// sheet's standing spot and of the briefing detective's spot; the spilled coffee (zone 2) is just past the chair.
        /// CaseRoot-local.
        /// </summary>
        public const float XBodyX = 0.22f, XBodyZ = 1.33f, XBodyYaw = 180f;

        /// <summary>
        /// Where the detective gives the verdict after a submission (supervisor, 2026-11-05), both cases: open floor left of
        /// the evidence board, clear of every zone, prop and clue (test). CaseRoot-local.
        /// </summary>
        public const float VerdictX = -0.85f, VerdictZ = -1.70f;
        public const float XChairX = 1.25f, XChairZ = 1.25f, XChairYaw = 140f;
        /// <summary>Radius of the office chair's footprint (star base and seat).</summary>
        public const float XChairRadius = 0.30f;

        /// <summary>Case Y: where the body lies (centre of the taped outline, head towards -x), CaseRoot-local.</summary>
        public const float YBodyX = 0f, YBodyZ = 1.25f, YBodyHeadOffsetX = -0.80f;

        /// <summary>Where the briefing detective stands in Case Y: open floor between the start and the tour desk, clear of every zone.</summary>
        public const float YBriefingX = 0.9f, YBriefingZ = -0.5f;

        private static readonly Dictionary<ClueSlot, Spot> LayoutY = new Dictionary<ClueSlot, Spot>
        {
            { ClueSlot.K1, new Spot(-1.50f, 2.35f, HeightBand.Eye, 180f, false) },   // smashed wall clock, front wall left of the plinth
            { ClueSlot.K2, new Spot(1.81f, 0.56f, HeightBand.Table, -90f, true) },   // visitor logbook on the reception table (right wall)
            { ClueSlot.K3, new Spot(0.35f, 0.88f, HeightBand.Floor, 180f, true) },   // torn scarf scrap, on the floor beside the body
            { ClueSlot.K4, new Spot(1.81f, -0.94f, HeightBand.Table, -90f, true) },  // the Datuk's email on the tour desk
            { ClueSlot.R1, new Spot(0.45f, 2.15f, HeightBand.Floor, -90f, true) },   // torch beside the plinth
            { ClueSlot.R2, new Spot(-2.00f, -1.85f, HeightBand.Floor, 0f, true) },   // card just inside the entrance
            { ClueSlot.R3, new Spot(-2.20f, 0.31f, HeightBand.Eye, 90f, true) },     // note on the noticeboard (left wall; grabbable, snaps back home)
        };

        private static CaseSpec BuildY()
        {
            var c = new CaseSpec
            {
                Case = CaseId.Y,
                Title = "Death in the Melaka Room",
                Briefing = "Last night, Datuk Hashim Karim, founder of the Heritage Gallery, was found stabbed to death in the " +
                           "Melaka Room with the gallery's own keris. The keris is gone from its plinth. He had found out that " +
                           "real pieces were being swapped for " +
                           "replicas. Bloody footprints lead from the body towards the entrance. Four people had access to the " +
                           "room. Search the scene, tag the evidence, and name the person who killed him.",
                BoardX = 0f, BoardZ = -2.19f, BoardYaw = 0f,
            };
            c.Suspects.Add(new SuspectSpec { Id = "Y_farid", Name = "Farid Rahman", Role = "Tour guide", IsCulprit = true,
                Alibi = "\"I was in the staff room preparing tomorrow's tour, 7:30 to 9 pm.\"" });
            c.Suspects.Add(new SuspectSpec { Id = "Y_tan", Name = "Mr Tan", Role = "Security guard",
                Alibi = "\"I was on gate duty from 8 to 8:40 pm.\"" });
            c.Suspects.Add(new SuspectSpec { Id = "Y_sofia", Name = "Dr Sofia Idris", Role = "Curator",
                Alibi = "\"I was on a video call with a lender from 7:50 pm.\"" });
            c.Suspects.Add(new SuspectSpec { Id = "Y_lim", Name = "Mr Lim", Role = "Private collector",
                Alibi = "\"I was having dinner at my hotel from 8 to 9 pm.\"" });

            c.Clues.Add(Clue("Y_", ClueSlot.K1, "Smashed wall clock", "Knocked off the wall in the struggle. The hands stopped at 8:17."));
            c.Clues.Add(Clue("Y_", ClueSlot.K2, "Visitor logbook", "\"Gallery key out 20:05, F.R.\" No return time written."));
            c.Clues.Add(Clue("Y_", ClueSlot.K3, "Torn scarf scrap", "A scrap of green tour-guide scarf, torn off in the struggle. A spot of blood on it."));
            c.Clues.Add(Clue("Y_", ClueSlot.K4, "Printed email", "Email from the Datuk to the tour guide: \"I know about the replicas. Meet me in the Melaka Room at 8. - H.\""));
            c.Clues.Add(Clue("Y_", ClueSlot.R1, "Torch", "A security-issue torch, battery flat. Mr Tan argued with the Datuk this week.", "Y_tan"));
            c.Clues.Add(Clue("Y_", ClueSlot.R2, "Business card", "Mr Lim's card; on the back: \"That keris will be mine.\"", "Y_lim"));
            c.Clues.Add(Clue("Y_", ClueSlot.R3, "Noticeboard note", "\"Datuk, I will resign if the keris is sold. - S.\"", "Y_sofia"));

            c.Zones.Add(new ZoneSpec { Id = "Y_Z1", Name = "Pool of blood", X = YBodyX + YBodyHeadOffsetX - 0.10f, Z = YBodyZ, Width = 0.6f, Depth = 0.6f, Yaw = 0f });
            c.Zones.Add(new ZoneSpec { Id = "Y_Z2", Name = "Bloody footprints", X = -1.25f, Z = -0.25f, Width = 0.45f, Depth = 2.2f, Yaw = 13.4f });
            c.Zones.Add(new ZoneSpec { Id = "Y_Z3", Name = "Broken vase", X = 1.55f, Z = 1.45f, Width = 0.45f, Depth = 0.45f, Yaw = 0f });

            // Footprints are axis-aligned after turning (desks and the door are turned 90 degrees, so width/depth swap).
            c.Props.Add(new PropSpec { Role = "display", Name = "Display plinth", X = 0.94f, Z = 1.94f, Width = 0.6f, Depth = 0.5f, Height = 1.63f, Yaw = 180f });
            c.Props.Add(new PropSpec { Role = "desk", Name = "Reception table", X = 1.81f, Z = 0.56f, Width = 0.5f, Depth = 0.9f, Height = PlayArea.TableHeight, Yaw = -90f });
            c.Props.Add(new PropSpec { Role = "office", Name = "Tour desk", X = 1.81f, Z = -0.94f, Width = 0.5f, Depth = 0.9f, Height = PlayArea.TableHeight, Yaw = -90f });
            c.Props.Add(new PropSpec { Role = "stand", Name = "Noticeboard", X = -2.25f, Z = 0.31f, Width = 0.1f, Depth = 0.4f, Height = 1.6f, Yaw = 90f });
            c.Props.Add(new PropSpec { Role = "door", Name = "Entrance", X = -1.69f, Z = -2.31f, Width = 0.8f, Depth = 0.1f, Height = 2.0f, Yaw = 0f });
            return c;
        }
    }
}
