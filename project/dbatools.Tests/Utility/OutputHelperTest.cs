using System;
using System.Linq;
using System.Management.Automation;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Utility.Test
{
    [TestClass]
    public class OutputHelperTest
    {
        private static PSObject BuildObject()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("ComputerName", "server1"));
            obj.Properties.Add(new PSNoteProperty("InstanceName", "MSSQLSERVER"));
            obj.Properties.Add(new PSNoteProperty("SqlInstance", "server1"));
            obj.Properties.Add(new PSNoteProperty("Total", 4096));
            obj.Properties.Add(new PSNoteProperty("Server", "hidden"));
            return obj;
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_AttachesCuratedColumns()
        {
            PSObject obj = BuildObject();
            OutputHelper.SetDefaultDisplayPropertySet(obj, "ComputerName", "InstanceName", "SqlInstance", "Total");

            PSMemberSet standardMembers = obj.Members["PSStandardMembers"] as PSMemberSet;
            Assert.IsNotNull(standardMembers, "PSStandardMembers member set must be attached");
            PSPropertySet displaySet = standardMembers.Members["DefaultDisplayPropertySet"] as PSPropertySet;
            Assert.IsNotNull(displaySet, "DefaultDisplayPropertySet must exist");
            CollectionAssert.AreEqual(new[] { "ComputerName", "InstanceName", "SqlInstance", "Total" }, displaySet.ReferencedPropertyNames.ToArray());
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_FullObjectStaysAccessible()
        {
            PSObject obj = BuildObject();
            OutputHelper.SetDefaultDisplayPropertySet(obj, "ComputerName");
            Assert.AreEqual("hidden", obj.Properties["Server"].Value, "Non-display properties remain readable");
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySetExcluding_OmitsOnlyExcluded()
        {
            PSObject obj = BuildObject();
            OutputHelper.SetDefaultDisplayPropertySetExcluding(obj, new[] { "Server" });

            PSMemberSet standardMembers = obj.Members["PSStandardMembers"] as PSMemberSet;
            PSPropertySet displaySet = standardMembers.Members["DefaultDisplayPropertySet"] as PSPropertySet;
            CollectionAssert.AreEqual(new[] { "ComputerName", "InstanceName", "SqlInstance", "Total" }, displaySet.ReferencedPropertyNames.ToArray());
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySetExcluding_DataRowExcludesInfrastructure()
        {
            System.Data.DataTable table = new System.Data.DataTable();
            table.Columns.Add("TraceFlag", typeof(int));
            System.Data.DataRow row = table.NewRow();
            row["TraceFlag"] = 3226;
            table.Rows.Add(row);

            PSObject obj = PSObject.AsPSObject(row);
            OutputHelper.SetDefaultDisplayPropertySetExcluding(obj, new string[0]);

            PSMemberSet standardMembers = obj.Members["PSStandardMembers"] as PSMemberSet;
            PSPropertySet displaySet = standardMembers.Members["DefaultDisplayPropertySet"] as PSPropertySet;
            Assert.IsFalse(displaySet.ReferencedPropertyNames.Contains("RowError"), "DataRow infrastructure properties are auto-excluded");
            Assert.IsTrue(displaySet.ReferencedPropertyNames.Contains("TraceFlag"));
        }

        [TestMethod]
        public void InsertTypeName_PrependsDbatoolsPrefixedName()
        {
            PSObject obj = BuildObject();
            OutputHelper.InsertTypeName(obj, "Example.Type");
            Assert.AreEqual("dbatools.Example.Type", obj.TypeNames[0]);
        }

        [TestMethod]
        public void AddAliasProperty_ResolvesThroughAlias()
        {
            PSObject obj = BuildObject();
            OutputHelper.AddAliasProperty(obj, "Memory", "Total");
            Assert.AreEqual(4096, obj.Properties["Memory"].Value);
        }

        // Facet (c): the ComputerName/InstanceName/SqlInstance instance-property triple.
        // AddInstanceProperties reads through SmoServerExtensions, which prefer the ETS note
        // properties Connect-DbaInstance attaches to the SMO Server (ComputerName,
        // DomainInstanceName) over live SMO reads - so a DISCONNECTED server carrying those notes
        // exercises the triple offline, no live instance required. server.ServiceName has no note
        // fallback and is unavailable on an unconnected server, so InstanceName lands null here,
        // which is exactly what distinguishes it from the two note-backed members.
        private static Server BuildDisconnectedServer(string name, string computerNameNote, string domainInstanceNote)
        {
            Server server = new Server(name);
            PSObject wrapped = PSObject.AsPSObject(server);
            if (computerNameNote != null)
                wrapped.Properties.Add(new PSNoteProperty("ComputerName", computerNameNote));
            if (domainInstanceNote != null)
                wrapped.Properties.Add(new PSNoteProperty("DomainInstanceName", domainInstanceNote));
            return server;
        }

        [TestMethod]
        public void AddInstanceProperties_AppendsTripleInOrder()
        {
            Server server = BuildDisconnectedServer("SQLPROD01", "SQLPROD01-NB", "SQLPROD01-NB\\INSTX");
            PSObject obj = new PSObject();
            OutputHelper.AddInstanceProperties(obj, server);

            string[] names = obj.Properties.Select(p => p.Name).ToArray();
            CollectionAssert.AreEqual(new[] { "ComputerName", "InstanceName", "SqlInstance" }, names,
                "AddInstanceProperties appends exactly ComputerName, InstanceName, SqlInstance in that order");
        }

        [TestMethod]
        public void AddInstanceProperties_ComputerNameHonorsNotePreference()
        {
            Server server = BuildDisconnectedServer("SQLPROD01", "SQLPROD01-NB", "SQLPROD01-NB\\INSTX");
            PSObject obj = new PSObject();
            OutputHelper.AddInstanceProperties(obj, server);
            // GetComputerName returns the ComputerName note ahead of server.Name/NetName.
            Assert.AreEqual("SQLPROD01-NB", obj.Properties["ComputerName"].Value);
        }

        [TestMethod]
        public void AddInstanceProperties_SqlInstanceHonorsDomainInstanceNote()
        {
            Server server = BuildDisconnectedServer("SQLPROD01", "SQLPROD01-NB", "SQLPROD01-NB\\INSTX");
            PSObject obj = new PSObject();
            OutputHelper.AddInstanceProperties(obj, server);
            // GetDomainInstanceName returns the DomainInstanceName note ahead of the composed fallback.
            Assert.AreEqual("SQLPROD01-NB\\INSTX", obj.Properties["SqlInstance"].Value);
        }

        [TestMethod]
        public void AddInstanceProperties_InstanceNameSourcedFromServiceName()
        {
            Server server = BuildDisconnectedServer("SQLPROD01", "SQLPROD01-NB", "SQLPROD01-NB\\INSTX");
            PSObject obj = new PSObject();
            OutputHelper.AddInstanceProperties(obj, server);
            // InstanceName is sourced from server.ServiceName, a distinct source from the two
            // note-backed members. An unconnected server reports no ServiceName, so it is null -
            // proving InstanceName is not being copied from ComputerName or SqlInstance.
            Assert.IsNull(obj.Properties["InstanceName"].Value);
            Assert.AreNotEqual(obj.Properties["ComputerName"].Value, obj.Properties["InstanceName"].Value);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void AddInstanceProperties_NullObjectThrows()
        {
            OutputHelper.AddInstanceProperties(null, new Server("SQLPROD01"));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void AddInstanceProperties_NullServerThrows()
        {
            OutputHelper.AddInstanceProperties(new PSObject(), null);
        }
    }
}
