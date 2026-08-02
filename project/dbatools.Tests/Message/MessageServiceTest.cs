using System;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Message.Test
{
    [TestClass]
    public class MessageServiceTest
    {
        [TestMethod]
        public void GetErrorMessage_ReturnsDeepestNonEmptyMessage()
        {
            Exception inner = new InvalidOperationException("the real cause");
            Exception middle = new Exception("wrapper", inner);
            Exception outer = new Exception("outermost", middle);
            ErrorRecord record = new ErrorRecord(outer, "test", ErrorCategory.NotSpecified, null);

            Assert.AreEqual("the real cause", MessageService.GetErrorMessage(record));
        }

        [TestMethod]
        public void GetErrorMessage_FallsBackToOuterMessage()
        {
            Exception outer = new Exception("only message");
            ErrorRecord record = new ErrorRecord(outer, "test", ErrorCategory.NotSpecified, null);

            Assert.AreEqual("only message", MessageService.GetErrorMessage(record));
        }

        [TestMethod]
        public void GetErrorMessage_NullRecordReturnsNull()
        {
            Assert.IsNull(MessageService.GetErrorMessage(null));
        }
    }
}
