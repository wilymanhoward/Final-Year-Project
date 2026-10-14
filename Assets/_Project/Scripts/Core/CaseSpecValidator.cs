using System;
using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>
    /// Checks the case specs against the Phase D rules (docs/cases.md): counts, height bands, everything inside the
    /// play area, no clue inside (or within 10 cm of) a zone, every clue reachable without reaching over a zone, red herrings pointing at distinct innocent suspects,
    /// similar mean clue distance from the start, and the mirror rule (Case Y = Case X mirrored left-right, with the
    /// display, desk and door on different sides). Pure C#; returns readable errors (empty = valid).
    /// </summary>
    public static class CaseSpecValidator
    {
        public const int Suspects = 4;
        public const int ClueCount = 7;
        public const int ZoneCount = 3;
        public const float ZoneMargin = 0.10f;
        public const float MaxMeanDistanceDifference = 0.15f;
        public const float MirrorTolerance = 0.01f;
        public const float MovedAtLeast = 0.5f;
        /// <summary>Reach check: where a person stands to read/take a clue (headset test 2: hands sway ~30 cm while crouching).</summary>
        public const float ReachDistance = 0.45f;
        /// <summary>The straight reach from the standing spot to the clue keeps this far from every zone.</summary>
        public const float ReachZoneMargin = 0.05f;
        /// <summary>Body clearance: the standing spot keeps this far from every zone.</summary>
        public const float StandZoneClearance = 0.30f;
        /// <summary>People approach a clue from its natural side: the standing spot must be within this angle of its facing.</summary>
        public const float FacingCone = 60f;

        /// <summary>Required height band per slot.</summary>
        public static HeightBand RequiredBand(ClueSlot slot)
        {
            switch (slot)
            {
                case ClueSlot.K1: case ClueSlot.R3: return HeightBand.Eye;
                case ClueSlot.K2: case ClueSlot.K4: return HeightBand.Table;
                default: return HeightBand.Floor;
            }
        }

        public static List<string> Validate(CaseSpec c)
        {
            var e = new List<string>();
            string n = "Case " + c.Case + ": ";
            if (c.Suspects.Count != Suspects) e.Add(n + $"{c.Suspects.Count} suspects (need {Suspects})");
            int culprits = c.Suspects.FindAll(s => s.IsCulprit).Count;
            if (culprits != 1) e.Add(n + $"{culprits} culprits (need 1)");
            if (c.Clues.Count != ClueCount) e.Add(n + $"{c.Clues.Count} clues (need {ClueCount})");
            if (c.Zones.Count != ZoneCount) e.Add(n + $"{c.Zones.Count} zones (need {ZoneCount})");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in c.Suspects) CheckId(e, n, ids, s.Id);
            foreach (var z in c.Zones) CheckId(e, n, ids, z.Id);

            var herringTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (ClueSlot slot in Enum.GetValues(typeof(ClueSlot)))
            {
                var cl = c.Clue(slot);
                if (cl == null) { e.Add(n + "missing clue " + slot); continue; }
                CheckId(e, n, ids, cl.Id);
                if (cl.Height != RequiredBand(slot)) e.Add(n + $"{slot} is at {cl.Height}, must be {RequiredBand(slot)}");
                if (!Inside(cl.X, cl.Z)) e.Add(n + $"{slot} is outside the play area");
                foreach (var z in c.Zones)
                    if (InZone(cl.X, cl.Z, z, ZoneMargin)) e.Add(n + $"{slot} is inside or within {ZoneMargin * 100:F0} cm of zone '{z.Name}'");
                if (!IsReachable(c, cl)) e.Add(n + $"{slot} can't be reached without reaching over a zone (no spot {ReachDistance:F2} m away " +
                                               $"on its natural side with {StandZoneClearance * 100:F0} cm body clearance)");
                if (PlayArea.IsKey(slot))
                {
                    if (cl.PointsAtSuspectId != null) e.Add(n + $"{slot} is key evidence but points at a suspect");
                }
                else
                {
                    var target = c.Suspects.Find(s => s.Id == cl.PointsAtSuspectId);
                    if (target == null) e.Add(n + $"{slot} doesn't point at a suspect");
                    else if (target.IsCulprit) e.Add(n + $"{slot} points at the culprit (red herrings point at innocents)");
                    else if (!herringTargets.Add(target.Id)) e.Add(n + $"{slot} points at {target.Name} twice");
                }
            }
            foreach (var z in c.Zones)
                if (!Inside(z.X, z.Z)) e.Add(n + $"zone '{z.Name}' is outside the play area");
            if (!Inside(c.BoardX, c.BoardZ)) e.Add(n + "evidence board is outside the play area");
            return e;
        }

        /// <summary>Both cases valid, comparable and mirrored.</summary>
        public static List<string> ValidatePair(CaseSpec x, CaseSpec y)
        {
            var e = new List<string>();
            e.AddRange(Validate(x));
            e.AddRange(Validate(y));

            float dx = MeanDistance(x), dy = MeanDistance(y);
            if (Math.Abs(dx - dy) > MaxMeanDistanceDifference)
                e.Add($"Mean clue distance from the start differs: X {dx:F2} m, Y {dy:F2} m");

            foreach (ClueSlot slot in Enum.GetValues(typeof(ClueSlot)))
            {
                var a = x.Clue(slot);
                var b = y.Clue(slot);
                if (a == null || b == null) continue;
                if (a.Height != b.Height) e.Add($"{slot}: height bands differ between X and Y");
                if (a.Grabbable != b.Grabbable) e.Add($"{slot}: grabbable in one case but not the other");
                if (!IsMirror(a.X, a.Z, b.X, b.Z)) e.Add($"{slot}: Y position isn't X mirrored");
            }
            for (int i = 0; i < Math.Min(x.Zones.Count, y.Zones.Count); i++)
            {
                var a = x.Zones[i];
                var b = y.Zones[i];
                if (!IsMirror(a.X, a.Z, b.X, b.Z) || Math.Abs(a.Width - b.Width) > MirrorTolerance || Math.Abs(a.Depth - b.Depth) > MirrorTolerance)
                    e.Add($"Zone {i + 1}: Y isn't X mirrored");
            }
            foreach (string role in new[] { "display", "desk", "door" })
            {
                var a = x.Prop(role);
                var b = y.Prop(role);
                if (a == null || b == null) { e.Add($"Prop '{role}' missing"); continue; }
                if (!IsMirror(a.X, a.Z, b.X, b.Z)) e.Add($"Prop '{role}': Y isn't X mirrored");
                if (Distance(a.X, a.Z, b.X, b.Z) < MovedAtLeast) e.Add($"Prop '{role}' is in the same place in X and Y");
            }
            return e;
        }

        /// <summary>
        /// True if there is a standing spot <see cref="ReachDistance"/> from the clue, within <see cref="FacingCone"/> of its
        /// natural side (<see cref="ClueSpec.FacingYaw"/>), inside the play area, not inside furniture, at least
        /// <see cref="StandZoneClearance"/> from every zone, and from which the straight reach to the clue stays at least
        /// <see cref="ReachZoneMargin"/> away from every zone.
        /// </summary>
        public static bool IsReachable(CaseSpec c, ClueSpec clue)
        {
            for (int deg = 0; deg < 360; deg += 15)
            {
                if (Math.Abs(AngleDiff(deg, clue.FacingYaw)) > FacingCone) continue;
                double a = deg * Math.PI / 180.0;
                float sx = clue.X + (float)(Math.Sin(a) * ReachDistance);
                float sz = clue.Z + (float)(Math.Cos(a) * ReachDistance);
                if (!Inside(sx, sz) || InsideProp(c, sx, sz)) continue;
                bool standClear = true;
                foreach (var z in c.Zones) if (InZone(sx, sz, z, StandZoneClearance)) { standClear = false; break; }
                if (!standClear) continue;
                bool clear = true;
                for (int i = 0; i <= 20 && clear; i++)
                {
                    float t = i / 20f;
                    float px = sx + (clue.X - sx) * t, pz = sz + (clue.Z - sz) * t;
                    foreach (var z in c.Zones)
                        if (InZone(px, pz, z, ReachZoneMargin)) { clear = false; break; }
                }
                if (clear) return true;
            }
            return false;
        }

        private static bool InsideProp(CaseSpec c, float x, float z)
        {
            foreach (var p in c.Props)
                if (Math.Abs(x - p.X) < p.Width / 2 && Math.Abs(z - p.Z) < p.Depth / 2) return true;
            return false;
        }

        private static float AngleDiff(float a, float b)
        {
            float d = (a - b) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        public static float MeanDistance(CaseSpec c)
        {
            if (c.Clues.Count == 0) return 0f;
            float sum = 0f;
            foreach (var cl in c.Clues) sum += (float)Math.Sqrt(cl.X * cl.X + cl.Z * cl.Z);
            return sum / c.Clues.Count;
        }

        /// <summary>True if (x, z) is inside the zone rectangle grown by <paramref name="margin"/> (zone yaw applied).</summary>
        public static bool InZone(float x, float z, ZoneSpec zone, float margin = 0f)
        {
            double a = zone.Yaw * Math.PI / 180.0;
            double dx = x - zone.X, dz = z - zone.Z;
            // Into the zone's local frame (rotate by -yaw around +y; yaw measured from +z towards +x).
            double lx = dx * Math.Cos(a) - dz * Math.Sin(a);
            double lz = dx * Math.Sin(a) + dz * Math.Cos(a);
            return Math.Abs(lx) <= zone.Width / 2 + margin && Math.Abs(lz) <= zone.Depth / 2 + margin;
        }

        private static bool Inside(float x, float z) => Math.Abs(x) <= PlayArea.HalfSize && Math.Abs(z) <= PlayArea.HalfSize;
        private static bool IsMirror(float ax, float az, float bx, float bz) =>
            Math.Abs(ax + bx) <= MirrorTolerance && Math.Abs(az - bz) <= MirrorTolerance;
        private static float Distance(float ax, float az, float bx, float bz) =>
            (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        private static void CheckId(List<string> e, string n, HashSet<string> ids, string id)
        {
            if (!TeamMessage.IsSendableId(id)) e.Add(n + $"id '{id}' is empty or contains '|' / ','");
            else if (!ids.Add(id)) e.Add(n + $"duplicate id '{id}'");
        }
    }
}
