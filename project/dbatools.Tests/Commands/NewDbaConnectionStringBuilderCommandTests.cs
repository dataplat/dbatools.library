using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaConnectionStringBuilderCommandTests
    {
        #region ShouldSerialize
        [TestMethod]
        public void ShouldSerialize_NullBuilder_ReturnsFalse()
        {
            bool result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(null, "Application Name");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldSerialize_EmptyBuilder_ReturnsFalseForAppName()
        {
            // An empty builder should not serialize "Application Name" since nothing was set
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder();
            bool result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "Application Name");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldSerialize_WithAppName_ReturnsTrueForAppName()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder("Application Name=TestApp");
            bool result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "Application Name");
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldSerialize_WithWorkstationId_ReturnsTrueForWorkstationId()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder("Workstation ID=mycomputer");
            bool result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "Workstation ID");
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldSerialize_EmptyBuilder_ReturnsFalseForPooling()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder();
            bool result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "Pooling");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldSerialize_WithPoolingExplicit_ReturnsTrueForPooling()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder("Pooling=False");
            bool result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "Pooling");
            Assert.IsTrue(result);
        }
        #endregion

        #region SetBuilderValue
        [TestMethod]
        public void SetBuilderValue_NullBuilder_DoesNotThrow()
        {
            // Should not throw when builder is null
            Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.SetBuilderValue(null, "Application Name", "test");
        }

        [TestMethod]
        public void SetBuilderValue_SetsApplicationName()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder();
            Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.SetBuilderValue(builder, "Application Name", "MyApp");
            Assert.AreEqual("MyApp", builder.ApplicationName);
        }

        [TestMethod]
        public void SetBuilderValue_SetsDataSource()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder();
            Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.SetBuilderValue(builder, "Data Source", "localhost,1433");
            Assert.AreEqual("localhost,1433", builder.DataSource);
        }

        [TestMethod]
        public void SetBuilderValue_SetsIntegratedSecurity()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder();
            Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.SetBuilderValue(builder, "Integrated Security", true);
            Assert.IsTrue(builder.IntegratedSecurity);
        }

        [TestMethod]
        public void SetBuilderValue_SetsPoolingFalse()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder();
            Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.SetBuilderValue(builder, "Pooling", false);
            Assert.IsFalse(builder.Pooling);
        }
        #endregion

        #region GetBuilderValue
        [TestMethod]
        public void GetBuilderValue_NullBuilder_ReturnsNull()
        {
            object result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.GetBuilderValue(null, "Application Name");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetBuilderValue_ReturnsSetValue()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder();
            builder.ApplicationName = "TestApp";
            object result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.GetBuilderValue(builder, "Application Name");
            Assert.AreEqual("TestApp", result);
        }

        [TestMethod]
        public void GetBuilderValue_ReturnsDataSource()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder("Data Source=myserver,1433");
            object result = Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.GetBuilderValue(builder, "Data Source");
            Assert.AreEqual("myserver,1433", result);
        }
        #endregion

        #region ConnectionStringParsing
        [TestMethod]
        public void ShouldSerialize_ParsesFullConnectionString()
        {
            string connStr = "Data Source=localhost,1433;Initial Catalog=MyDb;UID=sa;PWD=test123;Column Encryption Setting=enabled";
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);

            Assert.IsTrue(Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "Data Source"));
            Assert.IsTrue(Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "Initial Catalog"));
            Assert.IsTrue(Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "User ID"));
            Assert.IsFalse(Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.ShouldSerialize(builder, "Application Name"));
        }

        [TestMethod]
        public void SetBuilderValue_OverridesExistingValue()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder("Application Name=OldApp");
            Dataplat.Dbatools.Commands.NewDbaConnectionStringBuilderCommand.SetBuilderValue(builder, "Application Name", "NewApp");
            Assert.AreEqual("NewApp", builder.ApplicationName);
        }
        #endregion
    }
}
