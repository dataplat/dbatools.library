using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Commands;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAgentOperatorCommandTests
    {
        #region CalculatePagerDayInterval

        [TestMethod]
        public void CalculatePagerDayInterval_Sunday_Returns1()
        {
            Assert.AreEqual(1, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Sunday"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Monday_Returns2()
        {
            Assert.AreEqual(2, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Monday"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Tuesday_Returns4()
        {
            Assert.AreEqual(4, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Tuesday"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Wednesday_Returns8()
        {
            Assert.AreEqual(8, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Wednesday"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Thursday_Returns16()
        {
            Assert.AreEqual(16, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Thursday"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Friday_Returns32()
        {
            Assert.AreEqual(32, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Friday"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Saturday_Returns64()
        {
            Assert.AreEqual(64, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Saturday"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Weekdays_Returns62()
        {
            Assert.AreEqual(62, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Weekdays"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Weekend_Returns65()
        {
            Assert.AreEqual(65, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("Weekend"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_EveryDay_Returns127()
        {
            Assert.AreEqual(127, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("EveryDay"));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Null_Returns0()
        {
            Assert.AreEqual(0, NewDbaAgentOperatorCommand.CalculatePagerDayInterval(null));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_Empty_Returns0()
        {
            Assert.AreEqual(0, NewDbaAgentOperatorCommand.CalculatePagerDayInterval(""));
        }

        [TestMethod]
        public void CalculatePagerDayInterval_InvalidValue_Returns0()
        {
            Assert.AreEqual(0, NewDbaAgentOperatorCommand.CalculatePagerDayInterval("InvalidDay"));
        }

        #endregion CalculatePagerDayInterval

        #region FormatTime

        [TestMethod]
        public void FormatTime_ValidSixDigit_FormatsCorrectly()
        {
            Assert.AreEqual("07:00:00", NewDbaAgentOperatorCommand.FormatTime("070000"));
        }

        [TestMethod]
        public void FormatTime_Midnight_FormatsCorrectly()
        {
            Assert.AreEqual("00:00:00", NewDbaAgentOperatorCommand.FormatTime("000000"));
        }

        [TestMethod]
        public void FormatTime_EndOfDay_FormatsCorrectly()
        {
            Assert.AreEqual("23:59:59", NewDbaAgentOperatorCommand.FormatTime("235959"));
        }

        [TestMethod]
        public void FormatTime_Null_ReturnsNull()
        {
            Assert.IsNull(NewDbaAgentOperatorCommand.FormatTime(null));
        }

        [TestMethod]
        public void FormatTime_Empty_ReturnsNull()
        {
            Assert.IsNull(NewDbaAgentOperatorCommand.FormatTime(""));
        }

        [TestMethod]
        public void FormatTime_NonSixDigit_ReturnsUnchanged()
        {
            // If the time is already formatted or has a different length, return as-is
            Assert.AreEqual("07:00:00", NewDbaAgentOperatorCommand.FormatTime("07:00:00"));
        }

        [TestMethod]
        public void FormatTime_EveningTime_FormatsCorrectly()
        {
            Assert.AreEqual("19:00:00", NewDbaAgentOperatorCommand.FormatTime("190000"));
        }

        #endregion FormatTime

        #region IsValidTimeFormat

        [TestMethod]
        public void IsValidTimeFormat_ValidTime_ReturnsTrue()
        {
            Assert.IsTrue(NewDbaAgentOperatorCommand.IsValidTimeFormat("070000"));
        }

        [TestMethod]
        public void IsValidTimeFormat_Midnight_ReturnsTrue()
        {
            Assert.IsTrue(NewDbaAgentOperatorCommand.IsValidTimeFormat("000000"));
        }

        [TestMethod]
        public void IsValidTimeFormat_EndOfDay_ReturnsTrue()
        {
            Assert.IsTrue(NewDbaAgentOperatorCommand.IsValidTimeFormat("235959"));
        }

        [TestMethod]
        public void IsValidTimeFormat_Null_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentOperatorCommand.IsValidTimeFormat(null));
        }

        [TestMethod]
        public void IsValidTimeFormat_Empty_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentOperatorCommand.IsValidTimeFormat(""));
        }

        #endregion IsValidTimeFormat

        #region IsContainedAgError

        [TestMethod]
        public void IsContainedAgError_ContainsNewParent_ReturnsTrue()
        {
            var ex = new Exception("Value does not fall within newParent expected range.");
            Assert.IsTrue(NewDbaAgentOperatorCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_CaseInsensitive_ReturnsTrue()
        {
            var ex = new Exception("Something NEWPARENT something");
            Assert.IsTrue(NewDbaAgentOperatorCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_NoNewParent_ReturnsFalse()
        {
            var ex = new Exception("Some other error");
            Assert.IsFalse(NewDbaAgentOperatorCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_NullException_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentOperatorCommand.IsContainedAgError(null));
        }

        [TestMethod]
        public void IsContainedAgError_InnerException_ReturnsTrue()
        {
            var inner = new Exception("Value does not fall within newParent expected range.");
            var outer = new Exception("Outer error", inner);
            Assert.IsTrue(NewDbaAgentOperatorCommand.IsContainedAgError(outer));
        }

        [TestMethod]
        public void IsContainedAgError_DeepInnerException_ReturnsTrue()
        {
            var deepest = new Exception("newParent issue");
            var middle = new Exception("Middle error", deepest);
            var outer = new Exception("Outer error", middle);
            Assert.IsTrue(NewDbaAgentOperatorCommand.IsContainedAgError(outer));
        }

        #endregion IsContainedAgError

        #region NormalizePagerDay

        [TestMethod]
        public void NormalizePagerDay_LowercaseEveryday_ReturnsEveryDay()
        {
            Assert.AreEqual("EveryDay", NewDbaAgentOperatorCommand.NormalizePagerDay("everyday"));
        }

        [TestMethod]
        public void NormalizePagerDay_MixedCaseEveryday_ReturnsEveryDay()
        {
            Assert.AreEqual("EveryDay", NewDbaAgentOperatorCommand.NormalizePagerDay("Everyday"));
        }

        [TestMethod]
        public void NormalizePagerDay_CanonicalEveryDay_ReturnsSame()
        {
            Assert.AreEqual("EveryDay", NewDbaAgentOperatorCommand.NormalizePagerDay("EveryDay"));
        }

        [TestMethod]
        public void NormalizePagerDay_LowercaseWeekend_ReturnsWeekend()
        {
            Assert.AreEqual("Weekend", NewDbaAgentOperatorCommand.NormalizePagerDay("weekend"));
        }

        [TestMethod]
        public void NormalizePagerDay_Null_ReturnsNull()
        {
            Assert.IsNull(NewDbaAgentOperatorCommand.NormalizePagerDay(null));
        }

        [TestMethod]
        public void NormalizePagerDay_Empty_ReturnsEmpty()
        {
            Assert.AreEqual("", NewDbaAgentOperatorCommand.NormalizePagerDay(""));
        }

        #endregion NormalizePagerDay

        #region NormalizeNotifyMethod

        [TestMethod]
        public void NormalizeNotifyMethod_NotifyPager_ReturnsPager()
        {
            Assert.AreEqual("Pager", NewDbaAgentOperatorCommand.NormalizeNotifyMethod("NotifyPager"));
        }

        [TestMethod]
        public void NormalizeNotifyMethod_NotifyPagerCaseInsensitive_ReturnsPager()
        {
            Assert.AreEqual("Pager", NewDbaAgentOperatorCommand.NormalizeNotifyMethod("notifypager"));
        }

        [TestMethod]
        public void NormalizeNotifyMethod_NotifyEmail_ReturnsSame()
        {
            Assert.AreEqual("NotifyEmail", NewDbaAgentOperatorCommand.NormalizeNotifyMethod("NotifyEmail"));
        }

        [TestMethod]
        public void NormalizeNotifyMethod_Null_ReturnsNull()
        {
            Assert.IsNull(NewDbaAgentOperatorCommand.NormalizeNotifyMethod(null));
        }

        #endregion NormalizeNotifyMethod

        #region IsValidTimeFormat_LengthCheck

        [TestMethod]
        public void IsValidTimeFormat_ShortInput_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentOperatorCommand.IsValidTimeFormat("12"));
        }

        [TestMethod]
        public void IsValidTimeFormat_FiveDigits_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentOperatorCommand.IsValidTimeFormat("12345"));
        }

        [TestMethod]
        public void IsValidTimeFormat_SevenDigits_ReturnsFalse()
        {
            Assert.IsFalse(NewDbaAgentOperatorCommand.IsValidTimeFormat("1234567"));
        }

        #endregion IsValidTimeFormat_LengthCheck
    }
}
