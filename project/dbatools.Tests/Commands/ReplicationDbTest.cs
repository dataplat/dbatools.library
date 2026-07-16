using System;
using Dataplat.Dbatools.Commands;
using Dataplat.Dbatools.Message;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DbaMessageLevel = Dataplat.Dbatools.Message.MessageLevel;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// TB-007 coverage for ReplicationDb.Connect, the C# parity port of
    /// private/functions/Connect-ReplicationDB.ps1. The helper is almost entirely live
    /// behavior (LoadProperties is a server round-trip), so the offline tests pin what is
    /// offline-observable: faults propagate UNCAUGHT (the helper has no try/catch - an
    /// unconnected ConnectionContext faults identically in both worlds) and the verbose
    /// message callback stays silent on the fault path (the helper messages only on a
    /// false return, which requires a live connection). Success and properties-not-loaded
    /// legs belong to the lab gate, like TB-003's live replication behavior.
    /// </summary>
    [TestClass]
    public class ReplicationDbTest
    {
        [TestMethod]
        public void Connect_OfflineFaultPropagatesUncaughtAndCallbackStaysSilent()
        {
            Server offline = new Server("dbatools-tests-replicationdb-offline");
            offline.ConnectionContext.ConnectTimeout = 1;
            Microsoft.SqlServer.Management.Smo.Database database = new Microsoft.SqlServer.Management.Smo.Database(offline, "dbatools_tests_repdb");
            int messages = 0;
            Action<DbaMessageLevel, string> sink = delegate (DbaMessageLevel level, string message) { messages++; };

            bool threw = false;
            try
            {
                ReplicationDb.Connect(offline, database, sink);
            }
            catch (Exception)
            {
                // PS parity: the helper would fault identically here - LoadProperties()
                // against an unreachable server propagates uncaught in both worlds.
                threw = true;
            }
            Assert.IsTrue(threw, "an offline LoadProperties fault must propagate uncaught");
            Assert.AreEqual(0, messages, "the verbose message fires only on a false LoadProperties return, never on a fault");
        }

        [TestMethod]
        public void Connect_NullArgumentsFlowLikeNonStrictPsUntilTheSharedFault()
        {
            // PS non-strict member access turns $null.Name / $null.ConnectionContext into
            // nulls, and the helper then faults at LoadProperties() on the RMO object with
            // no connection context; the port's null-conditionals reproduce the flow and
            // the same terminal fault, with the callback silent throughout.
            int messages = 0;
            Action<DbaMessageLevel, string> sink = delegate (DbaMessageLevel level, string message) { messages++; };

            bool threw = false;
            try
            {
                ReplicationDb.Connect(null, null, sink);
            }
            catch (Exception)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "LoadProperties without a connection context must fault in both worlds");
            Assert.AreEqual(0, messages, "no message on the fault path");
        }
    }
}
