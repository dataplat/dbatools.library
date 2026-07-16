using System;
using System.Collections.Generic;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DbaMessageLevel = Dataplat.Dbatools.Message.MessageLevel;

namespace Dataplat.Dbatools.Connection.Test
{
    /// <summary>
    /// TB-001 coverage for ConnectionService.RegisterConnection, the C# parity port of
    /// private/functions/Add-ConnectionHashValue.ps1: pooled values replace the
    /// ActiveConnections entry, non-pooled Server values append to it, and the debug
    /// message fires before any work exactly like the PS original. The PS helper's live
    /// call sites (retired Connect-DbaInstance.ps1:1246 SqlConnection leg, :1367 Server
    /// leg) drive the scenarios; unreachable-input divergences (null/empty key or value
    /// no-op instead of a binding error) are pinned as the port's documented behavior.
    /// </summary>
    [TestClass]
    public class ConnectionServiceRegisterConnectionTest
    {
        private const string KeyPrefix = "dbatools-tests-registerconnection-";

        private static string UniqueKey()
        {
            return KeyPrefix + Guid.NewGuid().ToString("N");
        }

        [TestCleanup]
        public void RemoveTestEntries()
        {
            lock (ConnectionHost.ActiveConnections)
            {
                List<string> stale = new List<string>();
                foreach (string key in ConnectionHost.ActiveConnections.Keys)
                {
                    if (key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
                        stale.Add(key);
                }
                foreach (string key in stale)
                    ConnectionHost.ActiveConnections.Remove(key);
            }
        }

        [TestMethod]
        public void RegisterConnection_PooledValueReplacesEntry()
        {
            string key = UniqueKey();
            object first = new object();
            object second = new object();

            ConnectionService.RegisterConnection(key, first, null);
            ConnectionService.RegisterConnection(key, second, null);

            List<object> entries = ConnectionHost.ActiveConnections[key];
            Assert.AreEqual(1, entries.Count);
            Assert.AreSame(second, entries[0]);
        }

        [TestMethod]
        public void RegisterConnection_NonPooledServerAppendsToEntry()
        {
            string key = UniqueKey();
            Server first = new Server("dbatools-tests-nonpooled");
            first.ConnectionContext.NonPooledConnection = true;
            Server second = new Server("dbatools-tests-nonpooled");
            second.ConnectionContext.NonPooledConnection = true;

            ConnectionService.RegisterConnection(key, first, null);
            ConnectionService.RegisterConnection(key, second, null);

            List<object> entries = ConnectionHost.ActiveConnections[key];
            Assert.AreEqual(2, entries.Count);
            Assert.AreSame(first, entries[0]);
            Assert.AreSame(second, entries[1]);
        }

        [TestMethod]
        public void RegisterConnection_PooledServerReplacesNonPooledBacklog()
        {
            string key = UniqueKey();
            Server nonPooled = new Server("dbatools-tests-mixed");
            nonPooled.ConnectionContext.NonPooledConnection = true;
            Server pooled = new Server("dbatools-tests-mixed");

            ConnectionService.RegisterConnection(key, nonPooled, null);
            ConnectionService.RegisterConnection(key, nonPooled, null);
            ConnectionService.RegisterConnection(key, pooled, null);

            List<object> entries = ConnectionHost.ActiveConnections[key];
            Assert.AreEqual(1, entries.Count);
            Assert.AreSame(pooled, entries[0]);
        }

        [TestMethod]
        public void RegisterConnection_NullEntryIsRecreatedOnAppend()
        {
            // PS parity: -not $connections["$Key"] treats a stored $null exactly like a
            // missing key, so the non-pooled leg re-creates the list instead of throwing.
            string key = UniqueKey();
            lock (ConnectionHost.ActiveConnections)
            {
                ConnectionHost.ActiveConnections[key] = null;
            }
            Server nonPooled = new Server("dbatools-tests-nullentry");
            nonPooled.ConnectionContext.NonPooledConnection = true;

            ConnectionService.RegisterConnection(key, nonPooled, null);

            List<object> entries = ConnectionHost.ActiveConnections[key];
            Assert.AreEqual(1, entries.Count);
            Assert.AreSame(nonPooled, entries[0]);
        }

        [TestMethod]
        public void RegisterConnection_KeyLookupIsCaseInsensitive()
        {
            string key = UniqueKey();
            ConnectionService.RegisterConnection(key.ToLowerInvariant(), new object(), null);
            ConnectionService.RegisterConnection(key.ToUpperInvariant(), new object(), null);

            int matches = 0;
            lock (ConnectionHost.ActiveConnections)
            {
                foreach (string existing in ConnectionHost.ActiveConnections.Keys)
                {
                    if (String.Equals(existing, key, StringComparison.OrdinalIgnoreCase))
                        matches++;
                }
            }
            Assert.AreEqual(1, matches);
            Assert.AreEqual(1, ConnectionHost.ActiveConnections[key].Count);
        }

        [TestMethod]
        public void RegisterConnection_EmitsDebugMessageBeforeGuard()
        {
            // The PS helper writes "Adding to connection hash" before touching the cache;
            // the port keeps that order, so even a guarded no-op call emits the message.
            List<string> seen = new List<string>();
            Action<DbaMessageLevel, string> sink = delegate (DbaMessageLevel level, string message)
            {
                Assert.AreEqual(DbaMessageLevel.Debug, level);
                seen.Add(message);
            };

            ConnectionService.RegisterConnection(UniqueKey(), new object(), sink);
            ConnectionService.RegisterConnection(null, new object(), sink);

            Assert.AreEqual(2, seen.Count);
            Assert.AreEqual("Adding to connection hash", seen[0]);
            Assert.AreEqual("Adding to connection hash", seen[1]);
        }

        [TestMethod]
        public void RegisterConnection_NullOrEmptyKeyAndNullValueAreNoOps()
        {
            string key = UniqueKey();
            int before;
            lock (ConnectionHost.ActiveConnections)
            {
                before = ConnectionHost.ActiveConnections.Count;
            }

            ConnectionService.RegisterConnection(null, new object(), null);
            ConnectionService.RegisterConnection(String.Empty, new object(), null);
            ConnectionService.RegisterConnection(key, null, null);

            lock (ConnectionHost.ActiveConnections)
            {
                Assert.AreEqual(before, ConnectionHost.ActiveConnections.Count);
                Assert.IsFalse(ConnectionHost.ActiveConnections.ContainsKey(key));
            }
        }
    }
}
