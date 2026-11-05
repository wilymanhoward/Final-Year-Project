using System.Collections.Generic;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>The real room from the Quest's Space Setup scan, on the floor plane (world x, z).</summary>
    public sealed class RoomModel
    {
        /// <summary>Floor outline (the walls), any winding, may be non-convex.</summary>
        public readonly List<Vector2> Floor = new List<Vector2>();

        /// <summary>Real furniture, each a convex floor outline.</summary>
        public readonly List<RoomObstacle> Obstacles = new List<RoomObstacle>();
    }

    /// <summary>One piece of real furniture (a scanned volume) projected onto the floor.</summary>
    public sealed class RoomObstacle
    {
        public string Name;
        public readonly List<Vector2> Outline = new List<Vector2>();
    }

    /// <summary>A rectangle of the case's footprint, CaseRoot-local (x, z), turned by <see cref="Yaw"/> degrees.</summary>
    public struct FitBox
    {
        public string Name;
        public Vector2 Centre;
        public Vector2 Half;
        public float Yaw;
        /// <summary>The open play area: running past a wall or over furniture is only a warning.</summary>
        public bool IsPlayArea;
    }

    /// <summary>Result of checking one placement against the room.</summary>
    public sealed class RoomFitResult
    {
        /// <summary>False when no room scan was available (nothing was checked).</summary>
        public bool Scanned;
        /// <summary>Virtual objects in a wall or in real furniture.</summary>
        public readonly List<string> Problems = new List<string>();
        /// <summary>The open play area runs past a wall or real furniture stands in it.</summary>
        public readonly List<string> Warnings = new List<string>();

        public static readonly RoomFitResult NoScan = new RoomFitResult();

        /// <summary>room_fit=ok|warn|problem|no_scan;room_problems=n;room_warnings=n (placement log row).</summary>
        public string LogDetail =>
            "room_fit=" + (!Scanned ? "no_scan" : Problems.Count > 0 ? "problem" : Warnings.Count > 0 ? "warn" : "ok")
            + ";room_problems=" + Problems.Count + ";room_warnings=" + Warnings.Count;

        /// <summary>Short text for the experimenter label.</summary>
        public string Summary()
        {
            if (!Scanned) return "Room check skipped (no room scan - run Space Setup in the Quest settings)";
            if (Problems.Count > 0) return "Room check: " + Problems.Count + " problem(s): " + First(Problems);
            if (Warnings.Count > 0) return "Room check OK, but " + First(Warnings);
            return "Room check OK (fits the room)";
        }

        private static string First(List<string> items) =>
            items.Count <= 2 ? string.Join("; ", items) : items[0] + "; " + items[1] + " (+" + (items.Count - 2) + " more)";
    }

    /// <summary>
    /// Fits the crime scene into the scanned room (user, 2026-11-04: "the wall was blocking the 3D model"). Pure logic,
    /// unit tested: <see cref="Check"/> tests one placement (used after a manual placement, warn only) and
    /// <see cref="FindBest"/> searches for the placement that fits (automatic placement): yaws lined up with the walls,
    /// the one closest to the way the experimenter faces first, positions on a 10 cm grid, then the spot closest to the
    /// room's middle. Same room + same facing = same result, so every pair gets the same placement.
    /// </summary>
    public static class RoomFit
    {
        public const float WallMargin = 0.05f;              // props may stand this close to a wall
        public const float PlayAreaWallTolerance = 0.15f;   // scan noise: the play area may poke this far past a wall
        public const float GridStep = 0.1f;
        public const float BoardWidth = 1.2f, BoardDepth = 0.12f, ClueSize = 0.15f;
        private const int MaxYaws = 8;

        /// <summary>Everything that must fit, for every case given (both cases are played in the same placement).</summary>
        public static List<FitBox> Footprint(params CaseSpec[] cases)
        {
            var boxes = new List<FitBox>
            {
                new FitBox { Name = "the play area", Half = new Vector2(PlayArea.HalfSize, PlayArea.HalfSize), IsPlayArea = true },
            };
            foreach (var c in cases)
            {
                foreach (var p in c.Props) boxes.Add(Box(p.Name, p.X, p.Z, p.Width, p.Depth, p.Yaw));
                foreach (var z in c.Zones) boxes.Add(Box(z.Name, z.X, z.Z, z.Width, z.Depth, z.Yaw));
                foreach (var k in c.Clues) boxes.Add(Box(k.ObjectName, k.X, k.Z, ClueSize, ClueSize, k.FacingYaw));
                boxes.Add(Box("Evidence board", c.BoardX, c.BoardZ, BoardWidth, BoardDepth, c.BoardYaw));
            }
            return boxes;
        }

        private static FitBox Box(string name, float x, float z, float w, float d, float yaw) =>
            new FitBox { Name = name, Centre = new Vector2(x, z), Half = new Vector2(w / 2f, d / 2f), Yaw = yaw };

        /// <summary>Checks the case placed at floor point <paramref name="position"/> (world x, z) turned by <paramref name="yaw"/>.</summary>
        public static RoomFitResult Check(RoomModel room, List<FitBox> boxes, Vector2 position, float yaw)
        {
            var result = new RoomFitResult { Scanned = true };
            if (room == null || room.Floor.Count < 3) { result.Scanned = false; return result; }
            var corners = new Vector2[4];
            foreach (var box in boxes)
            {
                WorldCorners(box, position, yaw, corners);
                float worst = float.MaxValue;
                foreach (var c in corners) worst = Mathf.Min(worst, SignedDistanceInside(c, room.Floor));
                if (box.IsPlayArea)
                {
                    if (worst < -PlayAreaWallTolerance)
                        result.Warnings.Add($"the play area goes {-worst:F1} m past a wall");
                }
                else if (worst < WallMargin)
                {
                    result.Problems.Add(box.Name + " is in a wall");
                }
                foreach (var o in room.Obstacles)
                {
                    if (!ConvexOverlap(corners, o.Outline)) continue;
                    if (box.IsPlayArea) result.Warnings.Add("real " + o.Name + " stands in the play area");
                    else result.Problems.Add(box.Name + " overlaps real " + o.Name);
                }
            }
            return result;
        }

        /// <summary>
        /// The best placement: fewest problems, then fewest warnings, then the wall-aligned yaw closest to
        /// <paramref name="preferredYaw"/> (the experimenter's facing), then closest to the room's middle.
        /// </summary>
        public static RoomFitResult FindBest(RoomModel room, List<FitBox> boxes, float preferredYaw,
            out Vector2 position, out float yaw)
        {
            position = Vector2.zero;
            yaw = preferredYaw;
            if (room == null || room.Floor.Count < 3) return RoomFitResult.NoScan;

            Vector2 min = room.Floor[0], max = room.Floor[0], centre = Vector2.zero;
            foreach (var p in room.Floor) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); centre += p; }
            centre /= room.Floor.Count;
            position = centre;

            RoomFitResult best = null;
            int bestYawIndex = 0;
            float bestDistance = float.MaxValue;
            List<float> yaws = WallYaws(room.Floor, preferredYaw);
            for (int yawIndex = 0; yawIndex < yaws.Count; yawIndex++)
            {
                float candidateYaw = yaws[yawIndex];
                // Grid lined up with the room's middle, so a symmetric room gives a centred result.
                for (float x = centre.x - Mathf.Ceil((centre.x - min.x) / GridStep) * GridStep; x <= max.x; x += GridStep)
                for (float z = centre.y - Mathf.Ceil((centre.y - min.y) / GridStep) * GridStep; z <= max.y; z += GridStep)
                {
                    var p = new Vector2(x, z);
                    if (!InsidePolygon(p, room.Floor)) continue;
                    float distance = (p - centre).magnitude;
                    if (best != null && IsClean(best) && distance >= bestDistance) continue;
                    var fit = Check(room, boxes, p, candidateYaw);
                    if (best == null || IsBetter(fit, yawIndex, distance, best, bestYawIndex, bestDistance))
                    {
                        best = fit; bestYawIndex = yawIndex; bestDistance = distance; position = p; yaw = candidateYaw;
                    }
                }
                // Yaws are in order of preference: a clean fit at this yaw beats anything at a less preferred one.
                if (best != null && IsClean(best)) break;
            }
            return best ?? Check(room, boxes, position, yaw);
        }

        private static bool IsClean(RoomFitResult r) => r.Problems.Count == 0 && r.Warnings.Count == 0;

        // Lexicographic: problems, warnings, yaw preference, distance from the room's middle.
        private static bool IsBetter(RoomFitResult a, int yawA, float distanceA, RoomFitResult b, int yawB, float distanceB)
        {
            if (a.Problems.Count != b.Problems.Count) return a.Problems.Count < b.Problems.Count;
            if (a.Warnings.Count != b.Warnings.Count) return a.Warnings.Count < b.Warnings.Count;
            if (yawA != yawB) return yawA < yawB;
            return distanceA < distanceB - 1e-4f;
        }

        /// <summary>Yaws that line the case up with the walls (each long edge, 4 ways), closest to the preferred first.</summary>
        public static List<float> WallYaws(List<Vector2> floor, float preferredYaw)
        {
            var yaws = new List<float>();
            for (int i = 0; i < floor.Count; i++)
            {
                Vector2 e = floor[(i + 1) % floor.Count] - floor[i];
                if (e.magnitude < 0.5f) continue;
                float a = Mathf.Atan2(e.x, e.y) * Mathf.Rad2Deg;
                for (int k = 0; k < 4; k++)
                {
                    float y = Mathf.DeltaAngle(0f, a + 90f * k);
                    if (!yaws.Exists(v => Mathf.Abs(Mathf.DeltaAngle(v, y)) < 3f)) yaws.Add(y);
                }
            }
            if (yaws.Count == 0) yaws.Add(preferredYaw);
            yaws.Sort((p, q) => Mathf.Abs(Mathf.DeltaAngle(preferredYaw, p)).CompareTo(Mathf.Abs(Mathf.DeltaAngle(preferredYaw, q))));
            if (yaws.Count > MaxYaws) yaws.RemoveRange(MaxYaws, yaws.Count - MaxYaws);
            return yaws;
        }

        /// <summary>Same rotation as Quaternion.Euler(0, yaw, 0) applied to (x, 0, z).</summary>
        public static Vector2 ToWorld(Vector2 local, Vector2 position, float yaw)
        {
            float r = yaw * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            return position + new Vector2(local.x * cos + local.y * sin, -local.x * sin + local.y * cos);
        }

        private static void WorldCorners(FitBox box, Vector2 position, float yaw, Vector2[] corners)
        {
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector2(i == 0 || i == 3 ? -box.Half.x : box.Half.x, i < 2 ? -box.Half.y : box.Half.y);
                corners[i] = ToWorld(ToWorld(c, box.Centre, box.Yaw), position, yaw);
            }
        }

        /// <summary>Distance to the nearest wall: positive inside the outline, negative outside.</summary>
        public static float SignedDistanceInside(Vector2 p, List<Vector2> polygon)
        {
            float d = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count], ab = b - a;
                float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                d = Mathf.Min(d, (a + ab * t - p).magnitude);
            }
            return InsidePolygon(p, polygon) ? d : -d;
        }

        public static bool InsidePolygon(Vector2 p, List<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Separating-axis test for two convex outlines (touching counts as no overlap).</summary>
        public static bool ConvexOverlap(IList<Vector2> a, IList<Vector2> b)
        {
            return b.Count >= 3 && !HasSeparatingAxis(a, b) && !HasSeparatingAxis(b, a);
        }

        private static bool HasSeparatingAxis(IList<Vector2> a, IList<Vector2> b)
        {
            for (int i = 0; i < a.Count; i++)
            {
                Vector2 edge = a[(i + 1) % a.Count] - a[i];
                var axis = new Vector2(-edge.y, edge.x);
                Project(a, axis, out float minA, out float maxA);
                Project(b, axis, out float minB, out float maxB);
                if (maxA <= minB || maxB <= minA) return true;
            }
            return false;
        }

        private static void Project(IList<Vector2> points, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            foreach (var p in points) { float d = Vector2.Dot(p, axis); min = Mathf.Min(min, d); max = Mathf.Max(max, d); }
        }

        /// <summary>Convex hull (monotone chain) of floor points, e.g. a furniture box's 8 corners projected down.</summary>
        public static List<Vector2> ConvexHull(List<Vector2> points)
        {
            var pts = new List<Vector2>(points);
            pts.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
            if (pts.Count < 3) return pts;
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 p = pts[pass == 0 ? i : pts.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}
