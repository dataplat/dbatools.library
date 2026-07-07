using System;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    [TestClass]
    public class GetDbaServiceCommandTest
    {
        [TestMethod]
        public void MapServiceType_KnownId_ReturnsName()
        {
            Assert.AreEqual("Engine",    GetDbaServiceCommand.MapServiceType(1));
            Assert.AreEqual("Agent",     GetDbaServiceCommand.MapServiceType(2));
            Assert.AreEqual("FullText",  GetDbaServiceCommand.MapServiceType(3));
            Assert.AreEqual("FullText",  GetDbaServiceCommand.MapServiceType(9));
            Assert.AreEqual("SSIS",      GetDbaServiceCommand.MapServiceType(4));
            Assert.AreEqual("SSAS",      GetDbaServiceCommand.MapServiceType(5));
            Assert.AreEqual("SSRS",      GetDbaServiceCommand.MapServiceType(6));
            Assert.AreEqual("Browser",   GetDbaServiceCommand.MapServiceType(7));
            Assert.AreEqual("PolyBase",  GetDbaServiceCommand.MapServiceType(10));
            Assert.AreEqual("PolyBase",  GetDbaServiceCommand.MapServiceType(11));
            Assert.AreEqual("Launchpad", GetDbaServiceCommand.MapServiceType(12));
            Assert.AreEqual("Unknown",   GetDbaServiceCommand.MapServiceType(8));
        }

        [TestMethod]
        public void MapServiceType_UnknownId_ReturnsUnknown()
        {
            Assert.AreEqual("Unknown", GetDbaServiceCommand.MapServiceType(99));
            Assert.AreEqual("Unknown", GetDbaServiceCommand.MapServiceType(0));
        }

        [TestMethod]
        public void ServiceIdMap_ContainsAllExpectedEntries()
        {
            // Verify the map covers all expected SQL service types
            int expectedCount = 10; // Engine, Agent, FullText, SSIS, SSAS, SSRS, Browser, PolyBase, Launchpad, Unknown
            Assert.AreEqual(expectedCount, GetDbaServiceCommand.ServiceIdMap.Length);
        }

        [TestMethod]
        public void ServiceIdMap_FullTextHasTwoIds()
        {
            // FullText covers WMI type 3 and 9 (older SQL Search service)
            foreach (var (name, ids) in GetDbaServiceCommand.ServiceIdMap)
            {
                if (name == "FullText")
                {
                    Assert.AreEqual(2, ids.Length);
                    Assert.IsTrue(Array.IndexOf(ids, 3) >= 0);
                    Assert.IsTrue(Array.IndexOf(ids, 9) >= 0);
                    return;
                }
            }
            Assert.Fail("FullText entry not found in ServiceIdMap");
        }

        [TestMethod]
        public void ServiceIdMap_PolyBaseHasTwoIds()
        {
            // PolyBase covers WMI type 10 (DataMovement) and 11 (Engine)
            foreach (var (name, ids) in GetDbaServiceCommand.ServiceIdMap)
            {
                if (name == "PolyBase")
                {
                    Assert.AreEqual(2, ids.Length);
                    Assert.IsTrue(Array.IndexOf(ids, 10) >= 0);
                    Assert.IsTrue(Array.IndexOf(ids, 11) >= 0);
                    return;
                }
            }
            Assert.Fail("PolyBase entry not found in ServiceIdMap");
        }
    }
}
