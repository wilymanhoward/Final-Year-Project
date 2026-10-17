using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class SetupStatusModelTests
    {
        [Test]
        public void Placement_RedUntilPlacedAndOnTheFloor()
        {
            Assert.AreEqual(StatusLight.Bad, SetupStatusModel.Placement(PlacementMethod.None, true));
            Assert.AreEqual(StatusLight.Bad, SetupStatusModel.Placement(PlacementMethod.Manual, false));
            Assert.AreEqual(StatusLight.Ok, SetupStatusModel.Placement(PlacementMethod.Manual, true));
        }

        [Test]
        public void Network_FollowsTheConditionRule()
        {
            Assert.AreEqual(StatusLight.Ok, SetupStatusModel.Network(StudyCondition.B, true));
            Assert.AreEqual(StatusLight.Bad, SetupStatusModel.Network(StudyCondition.B, false));   // team needs a partner
            Assert.AreEqual(StatusLight.Bad, SetupStatusModel.Network(StudyCondition.A, true));    // solo must not have one
            Assert.AreEqual(StatusLight.Off, SetupStatusModel.Network(StudyCondition.A, false));
        }

        [Test]
        public void Data_AndRun()
        {
            Assert.AreEqual(StatusLight.Ok, SetupStatusModel.Data(0));
            Assert.AreEqual(StatusLight.Warn, SetupStatusModel.Data(3));
            Assert.AreEqual(StatusLight.Ok, SetupStatusModel.Run(true, false, true));
            Assert.AreEqual(StatusLight.Warn, SetupStatusModel.Run(false, true, true));
            Assert.AreEqual(StatusLight.Bad, SetupStatusModel.Run(false, false, false));
            Assert.AreEqual(StatusLight.Off, SetupStatusModel.Run(false, false, true));
        }

        [Test]
        public void StatusMessages_GetAColour()
        {
            Assert.AreEqual(StatusLight.Bad, SetupStatusModel.MessageLight("Anchor failed - use Manual"));
            Assert.AreEqual(StatusLight.Bad, SetupStatusModel.MessageLight("Floor looks wrong (head 2.05 m above it)"));
            Assert.AreEqual(StatusLight.Bad, SetupStatusModel.MessageLight("Could not reach 192.168.1.5"));
            Assert.AreEqual(StatusLight.Warn, SetupStatusModel.MessageLight("Waiting for tracking to settle..."));
            Assert.AreEqual(StatusLight.Warn, SetupStatusModel.MessageLight("Searching for the host"));
            Assert.AreEqual(StatusLight.Ok, SetupStatusModel.MessageLight("Placed (anchor saved)"));
            Assert.AreEqual(StatusLight.Off, SetupStatusModel.MessageLight(""));
        }

        [Test]
        public void Chip()
        {
            Assert.AreEqual("RUNNING 03:07", SetupStatusModel.Chip(true, false, false, 187.9));
            Assert.AreEqual("ARMED", SetupStatusModel.Chip(false, true, false, 0));
            Assert.AreEqual("ENDED", SetupStatusModel.Chip(false, false, true, 600));
            Assert.AreEqual("READY", SetupStatusModel.Chip(false, false, false, 0));
        }
    }
}
