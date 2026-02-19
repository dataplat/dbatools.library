using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaComputerCertificateCommandTests
    {
        #region ConvertPSObjectsToStringArray
        [TestMethod]
        public void ConvertPSObjectsToStringArray_NullInput_ReturnsNull()
        {
            var result = GetDbaComputerCertificateCommand.ConvertPSObjectsToStringArray(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ConvertPSObjectsToStringArray_EmptyCollection_ReturnsNull()
        {
            var collection = new Collection<PSObject>();
            var result = GetDbaComputerCertificateCommand.ConvertPSObjectsToStringArray(collection);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ConvertPSObjectsToStringArray_SingleItem_ReturnsSingleElementArray()
        {
            var collection = new Collection<PSObject>();
            collection.Add(new PSObject("LocalMachine"));
            var result = GetDbaComputerCertificateCommand.ConvertPSObjectsToStringArray(collection);
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("LocalMachine", result[0]);
        }

        [TestMethod]
        public void ConvertPSObjectsToStringArray_MultipleItems_ReturnsAllElements()
        {
            var collection = new Collection<PSObject>();
            collection.Add(new PSObject("LocalMachine"));
            collection.Add(new PSObject("CurrentUser"));
            var result = GetDbaComputerCertificateCommand.ConvertPSObjectsToStringArray(collection);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("LocalMachine", result[0]);
            Assert.AreEqual("CurrentUser", result[1]);
        }

        [TestMethod]
        public void ConvertPSObjectsToStringArray_NullElement_ReturnsNullInArray()
        {
            var collection = new Collection<PSObject>();
            collection.Add(new PSObject("My"));
            collection.Add(null);
            collection.Add(new PSObject("Root"));
            var result = GetDbaComputerCertificateCommand.ConvertPSObjectsToStringArray(collection);
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("My", result[0]);
            Assert.IsNull(result[1]);
            Assert.AreEqual("Root", result[2]);
        }
        #endregion

        #region GetCertRetrievalScript
        [TestMethod]
        public void GetCertRetrievalScript_ReturnsNonEmptyScript()
        {
            var script = GetDbaComputerCertificateCommand.GetCertRetrievalScript();
            Assert.IsFalse(String.IsNullOrWhiteSpace(script));
        }

        [TestMethod]
        public void GetCertRetrievalScript_ContainsPathHandling()
        {
            var script = GetDbaComputerCertificateCommand.GetCertRetrievalScript();
            Assert.IsTrue(script.Contains("if ($Path)"));
            Assert.IsTrue(script.Contains("ReadAllBytes"));
        }

        [TestMethod]
        public void GetCertRetrievalScript_ContainsThumbprintFiltering()
        {
            var script = GetDbaComputerCertificateCommand.GetCertRetrievalScript();
            Assert.IsTrue(script.Contains("if ($Thumbprint)"));
            Assert.IsTrue(script.Contains("Where-Object Thumbprint -in $Thumbprint"));
        }

        [TestMethod]
        public void GetCertRetrievalScript_ContainsServiceTypeFiltering()
        {
            var script = GetDbaComputerCertificateCommand.GetCertRetrievalScript();
            // Server Authentication OID (escaped regex pattern in the script)
            Assert.IsTrue(script.Contains(@"1\.3\.6\.1\.5\.5\.7\.3\.1"));
            Assert.IsTrue(script.Contains("$Type -eq 'Service'"));
        }

        [TestMethod]
        public void GetCertRetrievalScript_ContainsNotePropertyAdditions()
        {
            var script = GetDbaComputerCertificateCommand.GetCertRetrievalScript();
            Assert.IsTrue(script.Contains("NotePropertyName Algorithm"));
            Assert.IsTrue(script.Contains("NotePropertyName ComputerName"));
            Assert.IsTrue(script.Contains("NotePropertyName Name"));
            Assert.IsTrue(script.Contains("NotePropertyName Store"));
            Assert.IsTrue(script.Contains("NotePropertyName Folder"));
        }

        [TestMethod]
        public void GetCertRetrievalScript_ContainsCoreCertificateFunctions()
        {
            var script = GetDbaComputerCertificateCommand.GetCertRetrievalScript();
            Assert.IsTrue(script.Contains("function Get-CoreCertStore"));
            Assert.IsTrue(script.Contains("function Get-CoreCertificate"));
        }

        [TestMethod]
        public void GetCertRetrievalScript_IsParsableAsScriptBlock()
        {
            var script = GetDbaComputerCertificateCommand.GetCertRetrievalScript();
            // This will throw if the script is not valid PowerShell
            var sb = ScriptBlock.Create(script);
            Assert.IsNotNull(sb);
        }
        #endregion
    }
}
