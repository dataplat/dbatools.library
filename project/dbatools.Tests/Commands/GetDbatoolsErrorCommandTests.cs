using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbatoolsErrorCommandTests
    {
        #region SelectedProperties
        [TestMethod]
        public void SelectedProperties_ContainsExpectedProperties()
        {
            // Arrange
            string[] expected = new string[]
            {
                "CategoryInfo",
                "ErrorDetails",
                "Exception",
                "FullyQualifiedErrorId",
                "InvocationInfo",
                "PipelineIterationInfo",
                "PSMessageDetails",
                "ScriptStackTrace",
                "TargetObject"
            };

            // Act
            string[] actual = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.SelectedProperties;

            // Assert
            Assert.AreEqual(expected.Length, actual.Length, "Property count mismatch");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], actual[i], String.Format("Property at index {0} mismatch", i));
            }
        }

        [TestMethod]
        public void SelectedProperties_HasNineProperties()
        {
            // Act
            string[] props = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.SelectedProperties;

            // Assert
            Assert.AreEqual(9, props.Length);
        }
        #endregion

        #region FilterDbatoolsErrors
        [TestMethod]
        public void FilterDbatoolsErrors_NullInput_ReturnsEmptyList()
        {
            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.FilterDbatoolsErrors(null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterDbatoolsErrors_EmptyCollection_ReturnsEmptyList()
        {
            // Arrange
            ArrayList errors = new ArrayList();

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.FilterDbatoolsErrors(errors);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterDbatoolsErrors_MatchesDbatoolsErrors()
        {
            // Arrange
            ArrayList errors = new ArrayList();
            ErrorRecord dbatoolsError = new ErrorRecord(
                new Exception("test error"),
                "dbatools_Get-DbaDatabase",
                ErrorCategory.ConnectionError,
                null
            );
            errors.Add(dbatoolsError);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.FilterDbatoolsErrors(errors);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("dbatools_Get-DbaDatabase", result[0].Properties["FullyQualifiedErrorId"].Value);
        }

        [TestMethod]
        public void FilterDbatoolsErrors_ExcludesNonDbatoolsErrors()
        {
            // Arrange
            ArrayList errors = new ArrayList();
            ErrorRecord otherError = new ErrorRecord(
                new Exception("other error"),
                "SomeOtherModule_DoStuff",
                ErrorCategory.InvalidOperation,
                null
            );
            errors.Add(otherError);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.FilterDbatoolsErrors(errors);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FilterDbatoolsErrors_CaseInsensitiveMatch()
        {
            // Arrange
            ArrayList errors = new ArrayList();
            ErrorRecord upperCase = new ErrorRecord(
                new Exception("test"),
                "DBATOOLS_SomeCmd",
                ErrorCategory.NotSpecified,
                null
            );
            errors.Add(upperCase);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.FilterDbatoolsErrors(errors);

            // Assert
            Assert.AreEqual(1, result.Count, "Should match dbatools case-insensitively");
        }

        [TestMethod]
        public void FilterDbatoolsErrors_SkipsNullItems()
        {
            // Arrange
            ArrayList errors = new ArrayList();
            errors.Add(null);
            ErrorRecord dbatoolsError = new ErrorRecord(
                new Exception("test"),
                "dbatools_Test",
                ErrorCategory.NotSpecified,
                null
            );
            errors.Add(dbatoolsError);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.FilterDbatoolsErrors(errors);

            // Assert
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void FilterDbatoolsErrors_OutputHasExpectedProperties()
        {
            // Arrange
            ArrayList errors = new ArrayList();
            Exception ex = new Exception("test exception");
            ErrorRecord record = new ErrorRecord(
                ex,
                "dbatools_Get-DbaDatabase",
                ErrorCategory.ConnectionError,
                "sql01"
            );
            errors.Add(record);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.FilterDbatoolsErrors(errors);

            // Assert
            Assert.AreEqual(1, result.Count);
            PSObject obj = result[0];
            Assert.IsNotNull(obj.Properties["CategoryInfo"]);
            Assert.IsNotNull(obj.Properties["Exception"]);
            Assert.IsNotNull(obj.Properties["FullyQualifiedErrorId"]);
            Assert.IsNotNull(obj.Properties["ScriptStackTrace"]);
            Assert.IsNotNull(obj.Properties["TargetObject"]);
            Assert.AreEqual("sql01", obj.Properties["TargetObject"].Value);
            Assert.AreEqual(ex, obj.Properties["Exception"].Value);
        }

        [TestMethod]
        public void FilterDbatoolsErrors_MixedErrors_ReturnsOnlyDbatools()
        {
            // Arrange
            ArrayList errors = new ArrayList();
            errors.Add(new ErrorRecord(new Exception("e1"), "dbatools_Cmd1", ErrorCategory.NotSpecified, null));
            errors.Add(new ErrorRecord(new Exception("e2"), "OtherModule_Cmd", ErrorCategory.NotSpecified, null));
            errors.Add(new ErrorRecord(new Exception("e3"), "dbatools_Cmd2", ErrorCategory.NotSpecified, null));

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.FilterDbatoolsErrors(errors);

            // Assert
            Assert.AreEqual(2, result.Count);
        }
        #endregion

        #region ApplyPaging
        [TestMethod]
        public void ApplyPaging_NullInput_ReturnsEmptyList()
        {
            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(null, 1, 0, 0);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ApplyPaging_EmptyInput_ReturnsEmptyList()
        {
            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(new List<PSObject>(), 1, 0, 0);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ApplyPaging_FirstOne_ReturnsFirstItem()
        {
            // Arrange
            List<PSObject> items = CreateTestItems(5);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(items, 1, 0, 0);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("item0", result[0].Properties["Name"].Value);
        }

        [TestMethod]
        public void ApplyPaging_FirstThree_ReturnsFirstThreeItems()
        {
            // Arrange
            List<PSObject> items = CreateTestItems(5);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(items, 3, 0, 0);

            // Assert
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("item0", result[0].Properties["Name"].Value);
            Assert.AreEqual("item2", result[2].Properties["Name"].Value);
        }

        [TestMethod]
        public void ApplyPaging_LastOne_ReturnsLastItem()
        {
            // Arrange
            List<PSObject> items = CreateTestItems(5);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(items, 0, 1, 0);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("item4", result[0].Properties["Name"].Value);
        }

        [TestMethod]
        public void ApplyPaging_SkipTwo_SkipsFirstTwo()
        {
            // Arrange
            List<PSObject> items = CreateTestItems(5);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(items, 0, 0, 2);

            // Assert
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("item2", result[0].Properties["Name"].Value);
        }

        [TestMethod]
        public void ApplyPaging_SkipExceedsCount_ReturnsEmptyList()
        {
            // Arrange
            List<PSObject> items = CreateTestItems(3);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(items, 0, 0, 10);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ApplyPaging_SkipAndFirst_CombinesCorrectly()
        {
            // Arrange: 5 items (item0..item4), skip 1, first 2 => item1, item2
            List<PSObject> items = CreateTestItems(5);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(items, 2, 0, 1);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("item1", result[0].Properties["Name"].Value);
            Assert.AreEqual("item2", result[1].Properties["Name"].Value);
        }

        [TestMethod]
        public void ApplyPaging_FirstLargerThanCount_ReturnsAll()
        {
            // Arrange
            List<PSObject> items = CreateTestItems(3);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(items, 10, 0, 0);

            // Assert
            Assert.AreEqual(3, result.Count);
        }

        [TestMethod]
        public void ApplyPaging_ZeroFirstAndLast_ReturnsAll()
        {
            // Arrange
            List<PSObject> items = CreateTestItems(4);

            // Act
            List<PSObject> result = Dataplat.Dbatools.Commands.GetDbatoolsErrorCommand.ApplyPaging(items, 0, 0, 0);

            // Assert
            Assert.AreEqual(4, result.Count);
        }

        private static List<PSObject> CreateTestItems(int count)
        {
            List<PSObject> items = new List<PSObject>();
            for (int i = 0; i < count; i++)
            {
                PSObject obj = new PSObject();
                obj.Properties.Add(new PSNoteProperty("Name", String.Format("item{0}", i)));
                items.Add(obj);
            }
            return items;
        }
        #endregion
    }
}
