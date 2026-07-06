using System;
using System.Linq;
using System.Management.Automation;
using Dataplat.Dbatools.Utility;
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
    }
}
