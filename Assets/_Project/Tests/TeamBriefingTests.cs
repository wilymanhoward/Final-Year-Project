using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class TeamBriefingTests
    {
        private static float Dist(float x0, float z0, float x1, float z1) =>
            (float)System.Math.Sqrt((x0 - x1) * (x0 - x1) + (z0 - z1) * (z0 - z1));

        [Test]
        public void SpotIsInFrontOfThePair_FacingThem()
        {
            // Both at the start mark area, side by side, facing the front wall (+z).
            var s = BriefingSpot.Compute(new PlayerPose2D(-0.4f, 0f, 0f), new PlayerPose2D(0.4f, 0f, 0f));
            Assert.AreEqual(0f, s.X, 1e-3f);
            Assert.AreEqual(1.6f, s.Z, 1e-3f);
            Assert.AreEqual(180f, System.Math.Abs(s.Yaw), 1e-2f, "faces back towards the pair");
            Assert.AreEqual(-0.4f, s.AX, 1e-4f);
            Assert.AreEqual(0.4f, s.BX, 1e-4f);
        }

        [Test]
        public void SpotStaysInsideAndAwayFromBothPlayers()
        {
            var s = BriefingSpot.Compute(new PlayerPose2D(1.8f, 1.8f, 45f), new PlayerPose2D(2.1f, 1.2f, 30f));
            Assert.LessOrEqual(System.Math.Abs(s.X), 2.2f);
            Assert.LessOrEqual(System.Math.Abs(s.Z), 2.2f);

            var apart = BriefingSpot.Compute(new PlayerPose2D(-1.5f, 0f, 90f), new PlayerPose2D(1.5f, 0f, -90f));   // facing each other
            Assert.GreaterOrEqual(Dist(apart.X, apart.Z, -1.5f, 0f), 1.0f);
            Assert.GreaterOrEqual(Dist(apart.X, apart.Z, 1.5f, 0f), 1.0f);
        }

        [Test]
        public void BriefingMessagesRoundTrip()
        {
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.ReqBriefingReady("P02", new PlayerPose2D(0.25f, -1.5f, 87.5f)), out var r));
            Assert.AreEqual(TeamMessageKind.ReqBriefingReady, r.Kind);
            Assert.AreEqual("P02", r.By);
            Assert.AreEqual(-1.5f, r.Pose.Z, 1e-3f);
            Assert.AreEqual(87.5f, r.Pose.Yaw, 1e-3f);

            var spot = new BriefingSpot(0.1f, 1.6f, 180f, -0.4f, 0f, 0.4f, 0.05f);
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.BriefingGo(spot), out var g));
            Assert.AreEqual(TeamMessageKind.BriefingGo, g.Kind);
            Assert.AreEqual(1.6f, g.Spot.Z, 1e-3f);
            Assert.AreEqual(0.05f, g.Spot.BZ, 1e-3f);
            Assert.IsFalse(TeamMessage.TryParse("BriefingGo|1|2|x|4|5|6|7", out _));
        }

        [Test]
        public void CardShowsWaitingForPartner()
        {
            var v = ParticipantCardModel.Compute(true, SessionPhase.NotStarted, null, true, "T", "s", BriefingStage.WaitingForPartner, false);
            Assert.IsTrue(v.Visible);
            Assert.IsFalse(v.ShowBegin || v.ShowStart || v.ShowReplay);
            StringAssert.Contains("partner", v.Body);
        }
    }
}
