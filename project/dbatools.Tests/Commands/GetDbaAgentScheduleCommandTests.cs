using System;
using System.Collections.Generic;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Tests.Commands
{
    [TestClass]
    public class GetDbaAgentScheduleCommandTests
    {
        #region GetWeekDaysDescription
        [TestMethod]
        public void GetWeekDaysDescription_SingleDay_Monday()
        {
            // Monday = bit 2
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetWeekDaysDescription(2);
            Assert.AreEqual("Monday", result);
        }

        [TestMethod]
        public void GetWeekDaysDescription_SingleDay_Sunday()
        {
            // Sunday = bit 1
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetWeekDaysDescription(1);
            Assert.AreEqual("Sunday", result);
        }

        [TestMethod]
        public void GetWeekDaysDescription_Weekdays_MonThroughFri()
        {
            // Monday(2) + Tuesday(4) + Wednesday(8) + Thursday(16) + Friday(32) = 62
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetWeekDaysDescription(62);
            Assert.AreEqual("Monday, Tuesday, Wednesday, Thursday, Friday", result);
        }

        [TestMethod]
        public void GetWeekDaysDescription_AllDays()
        {
            // All days: 1+2+4+8+16+32+64 = 127
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetWeekDaysDescription(127);
            Assert.AreEqual("Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday", result);
        }

        [TestMethod]
        public void GetWeekDaysDescription_Weekend()
        {
            // Saturday(64) + Sunday(1) = 65
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetWeekDaysDescription(65);
            Assert.AreEqual("Saturday, Sunday", result);
        }

        [TestMethod]
        public void GetWeekDaysDescription_Zero_ReturnsEmpty()
        {
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetWeekDaysDescription(0);
            Assert.AreEqual("", result);
        }
        #endregion

        #region GetScheduleDescription
        [TestMethod]
        public void GetScheduleDescription_DailyOnce_ReturnsCorrectDescription()
        {
            PSObject schedule = CreateSchedulePSObject(
                frequencyTypes: 4,
                frequencyInterval: 1,
                frequencyRecurrenceFactor: 0,
                frequencySubDayTypes: 1,
                frequencySubDayInterval: 0,
                frequencyRelativeIntervals: 0,
                activeStartDate: new DateTime(2024, 1, 1),
                activeStartTimeOfDay: new TimeSpan(23, 0, 0),
                activeEndDate: new DateTime(9999, 12, 31),
                activeEndTimeOfDay: new TimeSpan(23, 59, 59));

            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetScheduleDescription(schedule);
            Assert.IsTrue(result.StartsWith("Occurs every day at "), String.Format("Expected 'Occurs every day at ...' but got: {0}", result));
            Assert.IsTrue(result.Contains("Schedule will be used starting on "), String.Format("Expected 'Schedule will be used starting on ...' but got: {0}", result));
        }

        [TestMethod]
        public void GetScheduleDescription_MonthlyOnDay10_ReturnsCorrectDescription()
        {
            PSObject schedule = CreateSchedulePSObject(
                frequencyTypes: 16,
                frequencyInterval: 10,
                frequencyRecurrenceFactor: 1,
                frequencySubDayTypes: 1,
                frequencySubDayInterval: 0,
                frequencyRelativeIntervals: 0,
                activeStartDate: new DateTime(2024, 1, 1),
                activeStartTimeOfDay: TimeSpan.Zero,
                activeEndDate: new DateTime(9999, 12, 31),
                activeEndTimeOfDay: new TimeSpan(23, 59, 59));

            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetScheduleDescription(schedule);
            Assert.IsTrue(result.Contains("Occurs every month on day 10 of that month"), String.Format("Expected monthly on day 10, got: {0}", result));
        }

        [TestMethod]
        public void GetScheduleDescription_AutoStart_ReturnsCorrectDescription()
        {
            PSObject schedule = CreateSchedulePSObject(
                frequencyTypes: 64,
                frequencyInterval: 0,
                frequencyRecurrenceFactor: 0,
                frequencySubDayTypes: 0,
                frequencySubDayInterval: 0,
                frequencyRelativeIntervals: 0,
                activeStartDate: new DateTime(2024, 1, 1),
                activeStartTimeOfDay: TimeSpan.Zero,
                activeEndDate: new DateTime(9999, 12, 31),
                activeEndTimeOfDay: new TimeSpan(23, 59, 59));

            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetScheduleDescription(schedule);
            Assert.AreEqual("Start automatically when SQL Server Agent starts ", result);
        }

        [TestMethod]
        public void GetScheduleDescription_OnIdle_ReturnsCorrectDescription()
        {
            PSObject schedule = CreateSchedulePSObject(
                frequencyTypes: 128,
                frequencyInterval: 0,
                frequencyRecurrenceFactor: 0,
                frequencySubDayTypes: 0,
                frequencySubDayInterval: 0,
                frequencyRelativeIntervals: 0,
                activeStartDate: new DateTime(2024, 1, 1),
                activeStartTimeOfDay: TimeSpan.Zero,
                activeEndDate: new DateTime(9999, 12, 31),
                activeEndTimeOfDay: new TimeSpan(23, 59, 59));

            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetScheduleDescription(schedule);
            Assert.AreEqual("Start whenever the CPUs become idle", result);
        }

        [TestMethod]
        public void GetScheduleDescription_WeeklyMonWedFri_ReturnsCorrectDescription()
        {
            // Monday(2) + Wednesday(8) + Friday(32) = 42
            PSObject schedule = CreateSchedulePSObject(
                frequencyTypes: 8,
                frequencyInterval: 42,
                frequencyRecurrenceFactor: 1,
                frequencySubDayTypes: 1,
                frequencySubDayInterval: 0,
                frequencyRelativeIntervals: 0,
                activeStartDate: new DateTime(2024, 1, 1),
                activeStartTimeOfDay: new TimeSpan(8, 0, 0),
                activeEndDate: new DateTime(9999, 12, 31),
                activeEndTimeOfDay: new TimeSpan(23, 59, 59));

            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetScheduleDescription(schedule);
            Assert.IsTrue(result.Contains("week on Monday, Wednesday, Friday"), String.Format("Expected weekly on Mon/Wed/Fri, got: {0}", result));
        }

        [TestMethod]
        public void GetScheduleDescription_DailyEvery30Minutes_ContainsMinuteInterval()
        {
            PSObject schedule = CreateSchedulePSObject(
                frequencyTypes: 4,
                frequencyInterval: 1,
                frequencyRecurrenceFactor: 0,
                frequencySubDayTypes: 4,
                frequencySubDayInterval: 30,
                frequencyRelativeIntervals: 0,
                activeStartDate: new DateTime(2024, 1, 1),
                activeStartTimeOfDay: new TimeSpan(8, 0, 0),
                activeEndDate: new DateTime(9999, 12, 31),
                activeEndTimeOfDay: new TimeSpan(17, 0, 0));

            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetScheduleDescription(schedule);
            Assert.IsTrue(result.Contains("every 30 minute(s)"), String.Format("Expected 'every 30 minute(s)', got: {0}", result));
            Assert.IsTrue(result.Contains("between "), String.Format("Expected 'between', got: {0}", result));
        }

        [TestMethod]
        public void GetScheduleDescription_MonthlyRelativeFirstMonday_ReturnsCorrectDescription()
        {
            PSObject schedule = CreateSchedulePSObject(
                frequencyTypes: 32,
                frequencyInterval: 2,  // Monday
                frequencyRecurrenceFactor: 1,
                frequencySubDayTypes: 1,
                frequencySubDayInterval: 0,
                frequencyRelativeIntervals: 1,  // First
                activeStartDate: new DateTime(2024, 1, 1),
                activeStartTimeOfDay: new TimeSpan(6, 0, 0),
                activeEndDate: new DateTime(9999, 12, 31),
                activeEndTimeOfDay: new TimeSpan(23, 59, 59));

            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetScheduleDescription(schedule);
            Assert.IsTrue(result.Contains("first Monday of every 1 month(s)"), String.Format("Expected first Monday monthly, got: {0}", result));
        }

        [TestMethod]
        public void GetScheduleDescription_WithEndDate_ContainsWillUsed()
        {
            PSObject schedule = CreateSchedulePSObject(
                frequencyTypes: 4,
                frequencyInterval: 1,
                frequencyRecurrenceFactor: 0,
                frequencySubDayTypes: 1,
                frequencySubDayInterval: 0,
                frequencyRelativeIntervals: 0,
                activeStartDate: new DateTime(2024, 1, 1),
                activeStartTimeOfDay: new TimeSpan(8, 0, 0),
                activeEndDate: new DateTime(2024, 12, 31),
                activeEndTimeOfDay: new TimeSpan(17, 0, 0));

            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetScheduleDescription(schedule);
            // Note: "will used" (missing "be") matches original PS1 behavior
            Assert.IsTrue(result.Contains("Schedule will used between"), String.Format("Expected 'Schedule will used between', got: {0}", result));
        }
        #endregion

        #region GetFrequencyTypeValue
        [TestMethod]
        public void GetFrequencyTypeValue_IntValue_ReturnsCorrectly()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("FrequencyTypes", 4));
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetFrequencyTypeValue(obj);
            Assert.AreEqual(4, result);
        }

        [TestMethod]
        public void GetFrequencyTypeValue_StringDaily_Returns4()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("FrequencyTypes", "Daily"));
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetFrequencyTypeValue(obj);
            Assert.AreEqual(4, result);
        }

        [TestMethod]
        public void GetFrequencyTypeValue_StringOneTime_Returns1()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("FrequencyTypes", "OneTime"));
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetFrequencyTypeValue(obj);
            Assert.AreEqual(1, result);
        }

        [TestMethod]
        public void GetFrequencyTypeValue_NullObject_Returns0()
        {
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetFrequencyTypeValue(null);
            Assert.AreEqual(0, result);
        }
        #endregion

        #region GetFrequencySubDayTypeValue
        [TestMethod]
        public void GetFrequencySubDayTypeValue_StringMinute_Returns4()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("FrequencySubDayTypes", "Minute"));
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetFrequencySubDayTypeValue(obj);
            Assert.AreEqual(4, result);
        }

        [TestMethod]
        public void GetFrequencySubDayTypeValue_StringHour_Returns8()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("FrequencySubDayTypes", "Hour"));
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetFrequencySubDayTypeValue(obj);
            Assert.AreEqual(8, result);
        }
        #endregion

        #region IsInStringArray
        [TestMethod]
        public void IsInStringArray_MatchExists_ReturnsTrue()
        {
            Assert.IsTrue(Dbatools.Commands.GetDbaAgentScheduleCommand.IsInStringArray("test", new string[] { "Test", "Other" }));
        }

        [TestMethod]
        public void IsInStringArray_NoMatch_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.GetDbaAgentScheduleCommand.IsInStringArray("missing", new string[] { "Test", "Other" }));
        }

        [TestMethod]
        public void IsInStringArray_NullArray_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.GetDbaAgentScheduleCommand.IsInStringArray("test", null));
        }
        #endregion

        #region IsInIntArray
        [TestMethod]
        public void IsInIntArray_MatchExists_ReturnsTrue()
        {
            Assert.IsTrue(Dbatools.Commands.GetDbaAgentScheduleCommand.IsInIntArray(5, new int[] { 1, 5, 10 }));
        }

        [TestMethod]
        public void IsInIntArray_NoMatch_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.GetDbaAgentScheduleCommand.IsInIntArray(99, new int[] { 1, 5, 10 }));
        }

        [TestMethod]
        public void IsInIntArray_NullArray_ReturnsFalse()
        {
            Assert.IsFalse(Dbatools.Commands.GetDbaAgentScheduleCommand.IsInIntArray(1, null));
        }
        #endregion

        #region AddOrSetProperty
        [TestMethod]
        public void AddOrSetProperty_NewProperty_AddsSuccessfully()
        {
            PSObject obj = new PSObject();
            Dbatools.Commands.GetDbaAgentScheduleCommand.AddOrSetProperty(obj, "TestProp", "TestValue");
            Assert.AreEqual("TestValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_ExistingProperty_UpdatesValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "OldValue"));
            Dbatools.Commands.GetDbaAgentScheduleCommand.AddOrSetProperty(obj, "TestProp", "NewValue");
            Assert.AreEqual("NewValue", obj.Properties["TestProp"].Value);
        }

        [TestMethod]
        public void AddOrSetProperty_NullObject_DoesNotThrow()
        {
            Dbatools.Commands.GetDbaAgentScheduleCommand.AddOrSetProperty(null, "TestProp", "TestValue");
            // No exception = pass
        }
        #endregion

        #region SetDefaultDisplayPropertySet
        [TestMethod]
        public void SetDefaultDisplayPropertySet_SetsCorrectly()
        {
            PSObject obj = new PSObject();
            string[] props = new string[] { "Name", "Value" };
            Dbatools.Commands.GetDbaAgentScheduleCommand.SetDefaultDisplayPropertySet(obj, props);

            PSMemberInfo member = obj.Members["PSStandardMembers"];
            Assert.IsNotNull(member);
        }

        [TestMethod]
        public void SetDefaultDisplayPropertySet_NullObject_DoesNotThrow()
        {
            Dbatools.Commands.GetDbaAgentScheduleCommand.SetDefaultDisplayPropertySet(null, new string[] { "Name" });
            // No exception = pass
        }
        #endregion

        #region GetEnumPropertyAsInt
        [TestMethod]
        public void GetEnumPropertyAsInt_IntegerValue_ReturnsSameInt()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", 42));

            var nameMap = new Dictionary<string, int>();
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetEnumPropertyAsInt(obj, "TestProp", nameMap);
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void GetEnumPropertyAsInt_StringNumber_ParsesCorrectly()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "16"));

            var nameMap = new Dictionary<string, int>();
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetEnumPropertyAsInt(obj, "TestProp", nameMap);
            Assert.AreEqual(16, result);
        }

        [TestMethod]
        public void GetEnumPropertyAsInt_EnumName_MapsCorrectly()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("TestProp", "Weekly"));

            var nameMap = new Dictionary<string, int> { { "Weekly", 8 } };
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetEnumPropertyAsInt(obj, "TestProp", nameMap);
            Assert.AreEqual(8, result);
        }

        [TestMethod]
        public void GetEnumPropertyAsInt_MissingProperty_Returns0()
        {
            PSObject obj = new PSObject();
            var nameMap = new Dictionary<string, int>();
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetEnumPropertyAsInt(obj, "Missing", nameMap);
            Assert.AreEqual(0, result);
        }
        #endregion

        #region GetPSObjectPropertyString
        [TestMethod]
        public void GetPSObjectPropertyString_ValidProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Name", "TestSchedule"));
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyString(obj, "Name");
            Assert.AreEqual("TestSchedule", result);
        }

        [TestMethod]
        public void GetPSObjectPropertyString_MissingProperty_ReturnsNull()
        {
            PSObject obj = new PSObject();
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyString(obj, "NonExistent");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetPSObjectPropertyString_NullObject_ReturnsNull()
        {
            string result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyString(null, "Name");
            Assert.IsNull(result);
        }
        #endregion

        #region GetPSObjectPropertyInt
        [TestMethod]
        public void GetPSObjectPropertyInt_IntProperty_ReturnsValue()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Id", 42));
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyInt(obj, "Id");
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void GetPSObjectPropertyInt_StringNumber_ParsesCorrectly()
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("Id", "123"));
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyInt(obj, "Id");
            Assert.AreEqual(123, result);
        }

        [TestMethod]
        public void GetPSObjectPropertyInt_MissingProperty_ReturnsZero()
        {
            PSObject obj = new PSObject();
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyInt(obj, "NonExistent");
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetPSObjectPropertyInt_NullObject_ReturnsZero()
        {
            int result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyInt(null, "Id");
            Assert.AreEqual(0, result);
        }
        #endregion

        #region GetPSObjectPropertyDateTime
        [TestMethod]
        public void GetPSObjectPropertyDateTime_ValidDateTime_ReturnsValue()
        {
            PSObject obj = new PSObject();
            DateTime expected = new DateTime(2024, 6, 15);
            obj.Properties.Add(new PSNoteProperty("DateProp", expected));

            DateTime result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyDateTime(obj, "DateProp");
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void GetPSObjectPropertyDateTime_NullObject_ReturnsMinValue()
        {
            DateTime result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyDateTime(null, "DateProp");
            Assert.AreEqual(DateTime.MinValue, result);
        }
        #endregion

        #region GetPSObjectPropertyTimeSpan
        [TestMethod]
        public void GetPSObjectPropertyTimeSpan_ValidTimeSpan_ReturnsValue()
        {
            PSObject obj = new PSObject();
            TimeSpan expected = new TimeSpan(14, 30, 0);
            obj.Properties.Add(new PSNoteProperty("TimeProp", expected));

            TimeSpan result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyTimeSpan(obj, "TimeProp");
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void GetPSObjectPropertyTimeSpan_NullObject_ReturnsZero()
        {
            TimeSpan result = Dbatools.Commands.GetDbaAgentScheduleCommand.GetPSObjectPropertyTimeSpan(null, "TimeProp");
            Assert.AreEqual(TimeSpan.Zero, result);
        }
        #endregion

        #region Test Helpers
        private static PSObject CreateSchedulePSObject(
            int frequencyTypes, int frequencyInterval, int frequencyRecurrenceFactor,
            int frequencySubDayTypes, int frequencySubDayInterval, int frequencyRelativeIntervals,
            DateTime activeStartDate, TimeSpan activeStartTimeOfDay,
            DateTime activeEndDate, TimeSpan activeEndTimeOfDay)
        {
            PSObject obj = new PSObject();
            obj.Properties.Add(new PSNoteProperty("FrequencyTypes", frequencyTypes));
            obj.Properties.Add(new PSNoteProperty("FrequencyInterval", frequencyInterval));
            obj.Properties.Add(new PSNoteProperty("FrequencyRecurrenceFactor", frequencyRecurrenceFactor));
            obj.Properties.Add(new PSNoteProperty("FrequencySubDayTypes", frequencySubDayTypes));
            obj.Properties.Add(new PSNoteProperty("FrequencySubDayInterval", frequencySubDayInterval));
            obj.Properties.Add(new PSNoteProperty("FrequencyRelativeIntervals", frequencyRelativeIntervals));
            obj.Properties.Add(new PSNoteProperty("ActiveStartDate", activeStartDate));
            obj.Properties.Add(new PSNoteProperty("ActiveStartTimeOfDay", activeStartTimeOfDay));
            obj.Properties.Add(new PSNoteProperty("ActiveEndDate", activeEndDate));
            obj.Properties.Add(new PSNoteProperty("ActiveEndTimeOfDay", activeEndTimeOfDay));
            return obj;
        }
        #endregion
    }
}
