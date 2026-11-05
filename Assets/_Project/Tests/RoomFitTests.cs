using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class RoomFitTests
    {
        private static readonly List<FitBox> Both = RoomFit.Footprint(CaseSpecs.X, CaseSpecs.Y);

        private static RoomModel SquareRoom(float size, Vector2 centre, float yaw = 0f)
        {
            var room = new RoomModel();
            float h = size / 2f;
            foreach (var c in new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) })
                room.Floor.Add(RoomFit.ToWorld(c, centre, yaw));
            return room;
        }

        private static RoomObstacle Box(string name, Vector2 centre, float size)
        {
            var o = new RoomObstacle { Name = name };
            float h = size / 2f;
            o.Outline.AddRange(RoomFit.ConvexHull(new List<Vector2>
            {
                centre + new Vector2(-h, -h), centre + new Vector2(h, -h), centre + new Vector2(h, h), centre + new Vector2(-h, h),
                centre,   // inner point: the hull drops it
            }));
            return o;
        }

        [Test]
        public void EmptyRoom_SceneGoesInTheMiddle_LinedUpWithTheWalls()
        {
            var room = SquareRoom(6f, new Vector2(10f, 5f));
            var fit = RoomFit.FindBest(room, Both, 10f, out Vector2 at, out float yaw);
            Assert.IsEmpty(fit.Problems, string.Join("; ", fit.Problems));
            Assert.IsEmpty(fit.Warnings, string.Join("; ", fit.Warnings));
            Assert.AreEqual(10f, at.x, 0.06f);
            Assert.AreEqual(5f, at.y, 0.06f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, yaw), 0.5f, "the wall direction closest to the experimenter's facing");
        }

        [Test]
        public void TurnedRoom_YawFollowsTheWallsAndTheFacing()
        {
            var room = SquareRoom(6f, Vector2.zero, 30f);
            RoomFit.FindBest(room, Both, 115f, out _, out float yaw);
            Assert.AreEqual(0f, Mathf.DeltaAngle(120f, yaw), 0.5f);   // 30 + 90, the one nearest 115
        }

        [Test]
        public void SmallRoom_PropsInTheWall_AreProblems()
        {
            var fit = RoomFit.Check(SquareRoom(4f, Vector2.zero), Both, Vector2.zero, 0f);
            Assert.IsTrue(fit.Problems.Exists(p => p.Contains("is in a wall")), string.Join("; ", fit.Problems));
            Assert.IsTrue(fit.Warnings.Exists(w => w.Contains("play area goes")));
            StringAssert.StartsWith("room_fit=problem;room_problems=", fit.LogDetail);
        }

        [Test]
        public void RealTable_WhereTheDisplayStands_IsAvoided()
        {
            var room = SquareRoom(7f, Vector2.zero);
            room.Obstacles.Add(Box("table", new Vector2(-0.9f, 2.0f), 0.6f));
            var atCentre = RoomFit.Check(room, Both, Vector2.zero, 0f);
            Assert.IsTrue(atCentre.Problems.Exists(p => p.EndsWith("overlaps real table")), string.Join("; ", atCentre.Problems));
            Assert.IsTrue(atCentre.Warnings.Contains("real table stands in the play area"));

            var best = RoomFit.FindBest(room, Both, 0f, out Vector2 at, out _);
            Assert.IsEmpty(best.Problems, string.Join("; ", best.Problems));
            Assert.IsEmpty(best.Warnings, string.Join("; ", best.Warnings));
            Assert.AreEqual("room_fit=ok;room_problems=0;room_warnings=0", best.LogDetail);
            Assert.Greater(at.magnitude, 0.5f, "moved away from the table");
        }

        [Test]
        public void NoRoom_IsNoScan()
        {
            var fit = RoomFit.FindBest(null, Both, 0f, out _, out _);
            Assert.IsFalse(fit.Scanned);
            Assert.AreEqual("room_fit=no_scan;room_problems=0;room_warnings=0", fit.LogDetail);
        }

        [Test]
        public void Geometry_Helpers()
        {
            var square = SquareRoom(2f, Vector2.zero).Floor;
            Assert.AreEqual(1f, RoomFit.SignedDistanceInside(Vector2.zero, square), 1e-5f);
            Assert.AreEqual(-0.5f, RoomFit.SignedDistanceInside(new Vector2(1.5f, 0f), square), 1e-5f);
            Assert.AreEqual(4, Box("x", Vector2.zero, 1f).Outline.Count);
            Assert.IsTrue(RoomFit.ConvexOverlap(square, Box("a", new Vector2(1.2f, 0f), 1f).Outline));
            Assert.IsFalse(RoomFit.ConvexOverlap(square, Box("b", new Vector2(2.6f, 0f), 1f).Outline));
            Vector2 p = RoomFit.ToWorld(new Vector2(0f, 1f), Vector2.zero, 90f);   // +Z turned 90 = +X, like Quaternion.Euler
            Assert.AreEqual(1f, p.x, 1e-5f);
            Assert.AreEqual(0f, p.y, 1e-5f);
        }

        [Test]
        public void PlacementRecord_LogsAutoMethodAndRoomFit()
        {
            var r = new PlacementRecord(PlacementMethod.AutoAnchor, false, "u", Vector3.zero, 0f, System.DateTime.UtcNow,
                "room_fit=ok;room_problems=0;room_warnings=0");
            Assert.AreEqual("auto_anchor", r.MethodCode);
            StringAssert.EndsWith(";room_fit=ok;room_problems=0;room_warnings=0", r.FormatDetail());
            StringAssert.DoesNotContain("room_fit", new PlacementRecord(PlacementMethod.Manual, false, null, Vector3.zero, 0f,
                System.DateTime.UtcNow).FormatDetail());
        }
    }
}
