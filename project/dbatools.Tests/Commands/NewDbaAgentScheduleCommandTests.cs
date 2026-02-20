using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class NewDbaAgentScheduleCommandTests
    {
        #region ConvertFrequencyType

        [TestMethod]
        public void ConvertFrequencyType_Once_Returns1()
        {
            Assert.AreEqual(1, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("Once"));
        }

        [TestMethod]
        public void ConvertFrequencyType_OneTime_Returns1()
        {
            Assert.AreEqual(1, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("OneTime"));
        }

        [TestMethod]
        public void ConvertFrequencyType_Daily_Returns4()
        {
            Assert.AreEqual(4, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("Daily"));
        }

        [TestMethod]
        public void ConvertFrequencyType_Weekly_Returns8()
        {
            Assert.AreEqual(8, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("Weekly"));
        }

        [TestMethod]
        public void ConvertFrequencyType_Monthly_Returns16()
        {
            Assert.AreEqual(16, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("Monthly"));
        }

        [TestMethod]
        public void ConvertFrequencyType_MonthlyRelative_Returns32()
        {
            Assert.AreEqual(32, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("MonthlyRelative"));
        }

        [TestMethod]
        public void ConvertFrequencyType_AgentStart_Returns64()
        {
            Assert.AreEqual(64, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("AgentStart"));
        }

        [TestMethod]
        public void ConvertFrequencyType_AutoStart_Returns64()
        {
            Assert.AreEqual(64, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("AutoStart"));
        }

        [TestMethod]
        public void ConvertFrequencyType_IdleComputer_Returns128()
        {
            Assert.AreEqual(128, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("IdleComputer"));
        }

        [TestMethod]
        public void ConvertFrequencyType_OnIdle_Returns128()
        {
            Assert.AreEqual(128, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("OnIdle"));
        }

        [TestMethod]
        public void ConvertFrequencyType_Null_Returns0()
        {
            Assert.AreEqual(0, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType(null));
        }

        [TestMethod]
        public void ConvertFrequencyType_Empty_Returns0()
        {
            Assert.AreEqual(0, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType(""));
        }

        [TestMethod]
        public void ConvertFrequencyType_CaseInsensitive_Works()
        {
            Assert.AreEqual(4, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("daily"));
            Assert.AreEqual(4, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyType("DAILY"));
        }

        #endregion ConvertFrequencyType

        #region ConvertFrequencySubdayType

        [TestMethod]
        public void ConvertFrequencySubdayType_Once_Returns1()
        {
            Assert.AreEqual(1, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType("Once"));
        }

        [TestMethod]
        public void ConvertFrequencySubdayType_Time_Returns1()
        {
            Assert.AreEqual(1, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType("Time"));
        }

        [TestMethod]
        public void ConvertFrequencySubdayType_Seconds_Returns2()
        {
            Assert.AreEqual(2, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType("Seconds"));
        }

        [TestMethod]
        public void ConvertFrequencySubdayType_Second_Returns2()
        {
            Assert.AreEqual(2, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType("Second"));
        }

        [TestMethod]
        public void ConvertFrequencySubdayType_Minutes_Returns4()
        {
            Assert.AreEqual(4, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType("Minutes"));
        }

        [TestMethod]
        public void ConvertFrequencySubdayType_Minute_Returns4()
        {
            Assert.AreEqual(4, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType("Minute"));
        }

        [TestMethod]
        public void ConvertFrequencySubdayType_Hours_Returns8()
        {
            Assert.AreEqual(8, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType("Hours"));
        }

        [TestMethod]
        public void ConvertFrequencySubdayType_Hour_Returns8()
        {
            Assert.AreEqual(8, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType("Hour"));
        }

        [TestMethod]
        public void ConvertFrequencySubdayType_Null_Returns1()
        {
            Assert.AreEqual(1, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencySubdayType(null));
        }

        #endregion ConvertFrequencySubdayType

        #region ConvertFrequencyRelativeInterval

        [TestMethod]
        public void ConvertFrequencyRelativeInterval_First_Returns1()
        {
            Assert.AreEqual(1, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyRelativeInterval("First"));
        }

        [TestMethod]
        public void ConvertFrequencyRelativeInterval_Second_Returns2()
        {
            Assert.AreEqual(2, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyRelativeInterval("Second"));
        }

        [TestMethod]
        public void ConvertFrequencyRelativeInterval_Third_Returns4()
        {
            Assert.AreEqual(4, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyRelativeInterval("Third"));
        }

        [TestMethod]
        public void ConvertFrequencyRelativeInterval_Fourth_Returns8()
        {
            Assert.AreEqual(8, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyRelativeInterval("Fourth"));
        }

        [TestMethod]
        public void ConvertFrequencyRelativeInterval_Last_Returns16()
        {
            Assert.AreEqual(16, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyRelativeInterval("Last"));
        }

        [TestMethod]
        public void ConvertFrequencyRelativeInterval_Unused_Returns0()
        {
            Assert.AreEqual(0, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyRelativeInterval("Unused"));
        }

        [TestMethod]
        public void ConvertFrequencyRelativeInterval_Null_Returns0()
        {
            Assert.AreEqual(0, Dbatools.Commands.NewDbaAgentScheduleCommand.ConvertFrequencyRelativeInterval(null));
        }

        #endregion ConvertFrequencyRelativeInterval

        #region CalculateInterval

        [TestMethod]
        public void CalculateInterval_Daily_NumericValue_ReturnsValue()
        {
            Assert.AreEqual(5, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(4, new object[] { 5 }));
        }

        [TestMethod]
        public void CalculateInterval_Daily_NullInterval_Returns0()
        {
            Assert.AreEqual(0, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(4, null));
        }

        [TestMethod]
        public void CalculateInterval_Weekly_Sunday_Returns1()
        {
            Assert.AreEqual(1, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(8, new object[] { "Sunday" }));
        }

        [TestMethod]
        public void CalculateInterval_Weekly_Monday_Returns2()
        {
            Assert.AreEqual(2, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(8, new object[] { "Monday" }));
        }

        [TestMethod]
        public void CalculateInterval_Weekly_Saturday_Returns64()
        {
            Assert.AreEqual(64, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(8, new object[] { "Saturday" }));
        }

        [TestMethod]
        public void CalculateInterval_Weekly_MultipleDays_AddsBitmask()
        {
            // Saturday (64) + Sunday (1) = 65
            Assert.AreEqual(65, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(8, new object[] { "Saturday", "Sunday" }));
        }

        [TestMethod]
        public void CalculateInterval_Weekly_Weekdays_Returns62()
        {
            Assert.AreEqual(62, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(8, new object[] { "Weekdays" }));
        }

        [TestMethod]
        public void CalculateInterval_Weekly_Weekend_Returns65()
        {
            Assert.AreEqual(65, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(8, new object[] { "Weekend" }));
        }

        [TestMethod]
        public void CalculateInterval_Weekly_EveryDay_Returns127()
        {
            Assert.AreEqual(127, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(8, new object[] { "EveryDay" }));
        }

        [TestMethod]
        public void CalculateInterval_Monthly_Day15_Returns15()
        {
            Assert.AreEqual(15, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(16, new object[] { 15 }));
        }

        [TestMethod]
        public void CalculateInterval_Monthly_Day31_Returns31()
        {
            Assert.AreEqual(31, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(16, new object[] { 31 }));
        }

        [TestMethod]
        public void CalculateInterval_MonthlyRelative_Sunday_Returns1()
        {
            Assert.AreEqual(1, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(32, new object[] { "Sunday" }));
        }

        [TestMethod]
        public void CalculateInterval_MonthlyRelative_Monday_Returns2()
        {
            Assert.AreEqual(2, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(32, new object[] { "Monday" }));
        }

        [TestMethod]
        public void CalculateInterval_MonthlyRelative_Friday_Returns6()
        {
            Assert.AreEqual(6, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(32, new object[] { "Friday" }));
        }

        [TestMethod]
        public void CalculateInterval_MonthlyRelative_NumericValue_Returns6()
        {
            Assert.AreEqual(6, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(32, new object[] { 6 }));
        }

        [TestMethod]
        public void CalculateInterval_UnknownFrequencyType_Returns0()
        {
            Assert.AreEqual(0, Dbatools.Commands.NewDbaAgentScheduleCommand.CalculateInterval(1, new object[] { "Monday" }));
        }

        #endregion CalculateInterval

        #region TryParseDate

        [TestMethod]
        public void TryParseDate_ValidDate_ReturnsTrue()
        {
            DateTime result;
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseDate("20240315", out result));
            Assert.AreEqual(new DateTime(2024, 3, 15), result);
        }

        [TestMethod]
        public void TryParseDate_MaxDate_ReturnsTrue()
        {
            DateTime result;
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseDate("99991231", out result));
            Assert.AreEqual(new DateTime(9999, 12, 31), result);
        }

        [TestMethod]
        public void TryParseDate_InvalidDate_ReturnsFalse()
        {
            DateTime result;
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseDate("20241301", out result));
        }

        [TestMethod]
        public void TryParseDate_TooShort_ReturnsFalse()
        {
            DateTime result;
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseDate("2024", out result));
        }

        [TestMethod]
        public void TryParseDate_Null_ReturnsFalse()
        {
            DateTime result;
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseDate(null, out result));
        }

        [TestMethod]
        public void TryParseDate_Empty_ReturnsFalse()
        {
            DateTime result;
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseDate("", out result));
        }

        #endregion TryParseDate

        #region TryParseTime

        [TestMethod]
        public void TryParseTime_ValidTime_ReturnsTrue()
        {
            TimeSpan result;
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseTime("143000", out result));
            Assert.AreEqual(new TimeSpan(14, 30, 0), result);
        }

        [TestMethod]
        public void TryParseTime_Midnight_ReturnsTrue()
        {
            TimeSpan result;
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseTime("000000", out result));
            Assert.AreEqual(TimeSpan.Zero, result);
        }

        [TestMethod]
        public void TryParseTime_EndOfDay_ReturnsTrue()
        {
            TimeSpan result;
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseTime("235959", out result));
            Assert.AreEqual(new TimeSpan(23, 59, 59), result);
        }

        [TestMethod]
        public void TryParseTime_TooShort_ReturnsFalse()
        {
            TimeSpan result;
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseTime("14", out result));
        }

        [TestMethod]
        public void TryParseTime_Null_ReturnsFalse()
        {
            TimeSpan result;
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentScheduleCommand.TryParseTime(null, out result));
        }

        #endregion TryParseTime

        #region IsContainedAgError

        [TestMethod]
        public void IsContainedAgError_WithNewParent_ReturnsTrue()
        {
            var ex = new Exception("Value cannot be null.\r\nParameter name: newParent");
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentScheduleCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_WithoutNewParent_ReturnsFalse()
        {
            var ex = new Exception("Some other error");
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentScheduleCommand.IsContainedAgError(ex));
        }

        [TestMethod]
        public void IsContainedAgError_Null_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.NewDbaAgentScheduleCommand.IsContainedAgError(null));
        }

        [TestMethod]
        public void IsContainedAgError_InnerException_ReturnsTrue()
        {
            var inner = new Exception("newParent is null");
            var outer = new Exception("Outer error", inner);
            Assert.IsTrue(Dbatools.Commands.NewDbaAgentScheduleCommand.IsContainedAgError(outer));
        }

        #endregion IsContainedAgError
    }
}
