using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class SuspectOrderTests
    {
        private static readonly string[] Ids = { "culprit", "b", "c", "d" };

        [Test]
        public void SameInputsGiveTheSameOrder_AllSuspectsKept()
        {
            var a = SuspectOrder.Shuffled(Ids, "G01", "X", "B");
            var b = SuspectOrder.Shuffled(Ids, "G01", "X", "B");
            CollectionAssert.AreEqual(a, b, "both headsets of a team must agree");
            CollectionAssert.AreEquivalent(Ids, a);
        }

        [Test]
        public void TheCulpritIsNotAlwaysFirst_AndOrdersDiffer()
        {
            var firsts = new HashSet<string>();
            var orders = new HashSet<string>();
            for (int i = 1; i <= 40; i++)
            {
                var o = SuspectOrder.Shuffled(Ids, "G" + i.ToString("00"), "X", "A");
                firsts.Add(o[0]);
                orders.Add(string.Join(",", o));
            }
            Assert.Greater(firsts.Count, 1, "the first card varies between pairs");
            Assert.Greater(orders.Count, 5, "many different orders");
            Assert.IsTrue(Enumerable.Range(1, 40).Any(i => SuspectOrder.Shuffled(Ids, "G" + i.ToString("00"), "X", "A")[0] != "culprit"));
        }

        [Test]
        public void NullAndEmptyAreSafe()
        {
            Assert.IsEmpty(SuspectOrder.Shuffled<string>(null, "G01", "X", "A"));
            Assert.IsEmpty(SuspectOrder.Shuffled(new string[0], "G01", "X", "A"));
        }
    }
}
