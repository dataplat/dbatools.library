using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Internal
{
    [TestClass]
    public class FlowControlTests
    {
        #region TestBound (simple overload)
        [TestMethod]
        public void TestBound_NullDictionary_ReturnsFalse()
        {
            Assert.IsFalse(FlowControl.TestBound(null, "Param1"));
        }

        [TestMethod]
        public void TestBound_NullParamNames_ReturnsFalse()
        {
            var dict = new Hashtable();
            dict["Foo"] = "bar";
            Assert.IsFalse(FlowControl.TestBound(dict, null));
        }

        [TestMethod]
        public void TestBound_EmptyParamNames_ReturnsFalse()
        {
            var dict = new Hashtable();
            dict["Foo"] = "bar";
            Assert.IsFalse(FlowControl.TestBound(dict, new string[0]));
        }

        [TestMethod]
        public void TestBound_ParameterPresent_ReturnsTrue()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            dict["Path"] = "C:\\temp";
            Assert.IsTrue(FlowControl.TestBound(dict, "Name"));
        }

        [TestMethod]
        public void TestBound_ParameterMissing_ReturnsFalse()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            Assert.IsFalse(FlowControl.TestBound(dict, "Missing"));
        }

        [TestMethod]
        public void TestBound_OneOfMultiplePresent_ReturnsTrue()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            Assert.IsTrue(FlowControl.TestBound(dict, "Missing", "Name"));
        }
        #endregion

        #region TestBoundAll
        [TestMethod]
        public void TestBoundAll_AllPresent_ReturnsTrue()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            dict["Path"] = "C:\\temp";
            Assert.IsTrue(FlowControl.TestBoundAll(dict, "Name", "Path"));
        }

        [TestMethod]
        public void TestBoundAll_OneMissing_ReturnsFalse()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            Assert.IsFalse(FlowControl.TestBoundAll(dict, "Name", "Path"));
        }

        [TestMethod]
        public void TestBoundAll_NullDictionary_ReturnsFalse()
        {
            Assert.IsFalse(FlowControl.TestBoundAll(null, "Name"));
        }
        #endregion

        #region TestBound (full overload)
        [TestMethod]
        public void TestBoundFull_AnyPresent_ReturnsTrue()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            // min=1, max=2, not=false, and=false
            Assert.IsTrue(FlowControl.TestBound(dict, new string[] { "Name", "Path" }, false, false, 1, 2));
        }

        [TestMethod]
        public void TestBoundFull_NonePresent_ReturnsFalse()
        {
            var dict = new Hashtable();
            Assert.IsFalse(FlowControl.TestBound(dict, new string[] { "Name", "Path" }, false, false, 1, 2));
        }

        [TestMethod]
        public void TestBoundFull_NotFlag_InvertsResult()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            // Name is present -> true, but not=true inverts it
            Assert.IsFalse(FlowControl.TestBound(dict, new string[] { "Name" }, true, false, 1, 1));
        }

        [TestMethod]
        public void TestBoundFull_NotFlag_NonePresent_ReturnsTrue()
        {
            var dict = new Hashtable();
            // None present -> false, but not=true inverts to true
            Assert.IsTrue(FlowControl.TestBound(dict, new string[] { "Name" }, true, false, 1, 1));
        }

        [TestMethod]
        public void TestBoundFull_AndFlag_AllRequired()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            dict["Path"] = "C:\\temp";
            // and=true forces min=parameterNames.Length
            Assert.IsTrue(FlowControl.TestBound(dict, new string[] { "Name", "Path" }, false, true, 1, 2));
        }

        [TestMethod]
        public void TestBoundFull_AndFlag_OneMissing_ReturnsFalse()
        {
            var dict = new Hashtable();
            dict["Name"] = "value";
            // and=true forces min=2, but only 1 present
            Assert.IsFalse(FlowControl.TestBound(dict, new string[] { "Name", "Path" }, false, true, 1, 2));
        }

        [TestMethod]
        public void TestBoundFull_MinMax_ExactCount()
        {
            var dict = new Hashtable();
            dict["A"] = 1;
            dict["B"] = 2;
            // 2 present, min=2, max=2 -> true
            Assert.IsTrue(FlowControl.TestBound(dict, new string[] { "A", "B", "C" }, false, false, 2, 2));
        }

        [TestMethod]
        public void TestBoundFull_MinMax_TooMany()
        {
            var dict = new Hashtable();
            dict["A"] = 1;
            dict["B"] = 2;
            dict["C"] = 3;
            // 3 present, max=2 -> false
            Assert.IsFalse(FlowControl.TestBound(dict, new string[] { "A", "B", "C" }, false, false, 1, 2));
        }

        [TestMethod]
        public void TestBoundFull_MinMax_TooFew()
        {
            var dict = new Hashtable();
            dict["A"] = 1;
            // 1 present, min=2 -> false
            Assert.IsFalse(FlowControl.TestBound(dict, new string[] { "A", "B", "C" }, false, false, 2, 3));
        }
        #endregion

        #region GetErrorMessage
        [TestMethod]
        public void GetErrorMessage_NullRecord_ReturnsNull()
        {
            Assert.IsNull(FlowControl.GetErrorMessage(null));
        }

        [TestMethod]
        public void GetErrorMessage_SimpleException_ReturnsMessage()
        {
            var ex = new InvalidOperationException("test error");
            var record = new ErrorRecord(ex, "TestError", ErrorCategory.NotSpecified, null);
            Assert.AreEqual("test error", FlowControl.GetErrorMessage(record));
        }

        [TestMethod]
        public void GetErrorMessage_NestedException_ReturnsDeepest()
        {
            var inner = new ArgumentException("root cause");
            var outer = new InvalidOperationException("wrapper", inner);
            var record = new ErrorRecord(outer, "TestError", ErrorCategory.NotSpecified, null);
            Assert.AreEqual("root cause", FlowControl.GetErrorMessage(record));
        }
        #endregion

        #region GetDeepestExceptionMessage
        [TestMethod]
        public void GetDeepestExceptionMessage_NullException_ReturnsNull()
        {
            Assert.IsNull(FlowControl.GetDeepestExceptionMessage(null));
        }

        [TestMethod]
        public void GetDeepestExceptionMessage_SingleLevel_ReturnsMessage()
        {
            var ex = new Exception("single level");
            Assert.AreEqual("single level", FlowControl.GetDeepestExceptionMessage(ex));
        }

        [TestMethod]
        public void GetDeepestExceptionMessage_ThreeLevels_ReturnsDeepest()
        {
            var level3 = new Exception("deepest");
            var level2 = new Exception("middle", level3);
            var level1 = new Exception("outer", level2);
            Assert.AreEqual("deepest", FlowControl.GetDeepestExceptionMessage(level1));
        }
        #endregion

        #region TestWindows
        [TestMethod]
        public void TestWindows_ReturnsBoolean()
        {
            // Just verify it returns without error; actual value depends on platform
            bool result = FlowControl.TestWindows();
#if NETFRAMEWORK
            Assert.IsTrue(result, "net472 always reports Windows");
#endif
        }
        #endregion

        #region StopFunction
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void StopFunction_NullCmdlet_ThrowsArgumentNull()
        {
            FlowControl.StopFunction(null, "test");
        }
        #endregion
    }
}
