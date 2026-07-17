using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Coverage for InvokeDbaQueryCommand.DataRowToPSObject (TB-066), the DBNullScrubber
    /// the PS Invoke-DbaAsync source embeds verbatim as a C# Add-Type class and this port
    /// lifts to a native static method - so it is byte-identical C#-from-C#, faithful by
    /// construction. The scrubber turns a DataRow into a PSObject, mapping DBNull cells to
    /// a real null note property (the "convenient results you can use comparisons with"
    /// contract) while preserving column order and non-null values, and returns an empty
    /// PSObject for a detached row. The -As output modes that call it and the live query
    /// execution ride the Invoke-DbaQuery gate; this pin locks the scrubber against drift.
    /// </summary>
    [TestClass]
    public class DBNullScrubberTest
    {
        private static DataTable MakeTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Note", typeof(string));
            return table;
        }

        [TestMethod]
        public void DataRowToPSObject_ScrubsDBNullToNullAndPreservesValues()
        {
            DataTable table = MakeTable();
            DataRow row = table.NewRow();
            row["Id"] = 7;
            row["Name"] = "widget";
            row["Note"] = System.DBNull.Value;
            table.Rows.Add(row);

            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(row);

            Assert.AreEqual(3, result.Properties.Match("*").Count, "one note property per column");
            Assert.AreEqual(7, result.Properties["Id"].Value, "non-null int preserved");
            Assert.AreEqual("widget", result.Properties["Name"].Value, "non-null string preserved");
            Assert.IsNull(result.Properties["Note"].Value, "DBNull scrubbed to a real null");
        }

        [TestMethod]
        public void DataRowToPSObject_PreservesColumnOrder()
        {
            DataTable table = MakeTable();
            DataRow row = table.NewRow();
            row["Id"] = 1;
            row["Name"] = "a";
            row["Note"] = "b";
            table.Rows.Add(row);

            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(row);

            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            foreach (PSPropertyInfo p in result.Properties)
                names.Add(p.Name);
            CollectionAssert.AreEqual(new[] { "Id", "Name", "Note" }, names, "column order preserved");
        }

        [TestMethod]
        public void DataRowToPSObject_DetachedRowYieldsEmptyObject()
        {
            DataTable table = MakeTable();
            // A row that was never added (or was removed) is Detached; the scrubber guards
            // against it and returns an empty PSObject rather than throwing.
            DataRow detached = table.NewRow();
            detached["Id"] = 9;

            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(detached);

            Assert.AreEqual(0, result.Properties.Match("*").Count, "detached row yields no properties");
        }

        [TestMethod]
        public void DataRowToPSObject_AllNullRowIsAllNullProperties()
        {
            DataTable table = MakeTable();
            DataRow row = table.NewRow();
            // All cells DBNull.
            table.Rows.Add(row);

            PSObject result = InvokeDbaQueryCommand.DataRowToPSObject(row);

            Assert.AreEqual(3, result.Properties.Match("*").Count, "one property per column");
            Assert.IsNull(result.Properties["Id"].Value, "DBNull int scrubbed");
            Assert.IsNull(result.Properties["Name"].Value, "DBNull string scrubbed");
            Assert.IsNull(result.Properties["Note"].Value, "DBNull string scrubbed");
        }
    }
}
