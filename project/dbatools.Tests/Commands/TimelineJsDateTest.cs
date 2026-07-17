using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// TB-017 coverage for the absorbed ConvertTo-JsDate helper inside
    /// ConvertToDbaTimelineCommand. Expected values ground-truthed against the PS helper
    /// on both editions (probe 2026-07-17): "new Date(yyyy, MM-1, dd, HH, mm, ss)" with a
    /// 0-based UNPADDED month and zero-padded other parts, rendered with the CURRENT
    /// culture's calendar (th-TH Buddhist years, ar-SA UmAlQura dates); and exactly TWO
    /// output classes - the complete string, or EMPTY. Any Get-Date formatting failure is
    /// a statement-terminating throw killing the whole template (probed: -ErrorAction
    /// does not govern it), so the partial-render shapes the port previously emitted
    /// ("-1" month, per-component empties) never occur in PS - those pins guard the
    /// TB-017 correction.
    /// </summary>
    [TestClass]
    public class TimelineJsDateTest
    {
        private static string Convert(object inputDate)
        {
            return ConvertToDbaTimelineCommand.ConvertToJsDate(inputDate);
        }

        [TestMethod]
        public void JsDate_DocExampleAndPaddingShapes()
        {
            // Opus TB-017: the method is defined to follow CurrentCulture, so the shape
            // pins fix the culture instead of depending on the ambient host culture.
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                // The helper doc's own example: month 8 renders as unpadded 0-based 7, the
                // time parts keep their leading zeros.
                Assert.AreEqual("new Date(2018, 7, 14, 07, 40, 42)", Convert(new DateTime(2018, 8, 14, 7, 40, 42)));
                // January is month 0; midnight keeps double zeros; day pads.
                Assert.AreEqual("new Date(2026, 0, 05, 00, 00, 09)", Convert(new DateTime(2026, 1, 5, 0, 0, 9)));
                // December is 11 - a 1-based "fix" emits 12 here and fails.
                Assert.AreEqual("new Date(2026, 11, 31, 23, 59, 59)", Convert(new DateTime(2026, 12, 31, 23, 59, 59)));
                // October: the subtraction UNPADS the month ("10" -> 9), dd/HH/mm/ss stay padded.
                Assert.AreEqual("new Date(2026, 9, 02, 13, 05, 00)", Convert(new DateTime(2026, 10, 2, 13, 5, 0)));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [TestMethod]
        public void JsDate_StringInputConvertsInvariantLikeTheDatetimeBinder()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                // The PS [datetime] parameter cast is a LanguagePrimitives conversion;
                // probed: an ISO-ish string binds and renders identically on both editions.
                Assert.AreEqual("new Date(2026, 6, 16, 08, 30, 00)", Convert("2026-07-16 08:30:00"));
                // The INVARIANT discriminator (opus TB-017, probe-settled): under en-GB a
                // dd/MM string still FAILS the PS binder (renders empty) because the
                // binder's conversion is invariant, not culture-current - a port passing
                // CurrentCulture to ConvertTo would parse it and fail this pin.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-GB");
                Assert.AreEqual("", Convert("16/07/2026 08:30:00"), "the [datetime] binder parses invariant - a culture-current dd/MM string never binds");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [TestMethod]
        public void JsDate_UnconvertibleNullAndArraysRenderEmpty()
        {
            // PS: the Mandatory [datetime] cast fails statement-terminating inside the
            // calculated property and the whole field is EMPTY (probed both editions).
            Assert.AreEqual("", Convert(null));
            Assert.AreEqual("", Convert("not a date"));
            // Opus TB-017 (probe-settled): the PS binder rejects ARRAYS for the scalar
            // [datetime] exactly as it does for the [string] sibling - but unlike the
            // sibling, no explicit guard is needed here because LanguagePrimitives ALSO
            // refuses array-to-DateTime (single-element and empty both threw, both
            // editions), so the catch renders the same empty. These pins keep that true.
            Assert.AreEqual("", Convert(new object[] { new DateTime(2018, 8, 14, 7, 40, 42) }), "arrays never bind to a scalar [datetime] parameter, even single-element");
            Assert.AreEqual("", Convert(new DateTime[0]));
            Assert.AreEqual("", Convert(PSObject.AsPSObject(new object[] { new DateTime(2018, 8, 14, 7, 40, 42) })), "PSObject wrapping does not change the rejection");
        }

        [TestMethod]
        public void JsDate_ArrayStartDateRendersEmptyThroughTheRealPipeline()
        {
            // Opus TB-017: one end-to-end pin through the real cmdlet, mirroring the
            // sibling helper's hosted array pins - an array-valued StartDate must render
            // an EMPTY StartDate column in the emitted body row (PS calc-prop ground
            // truth both editions), never a fabricated new Date(...).
            InitialSessionState iss = InitialSessionState.CreateDefault2();
            iss.Commands.Add(new SessionStateCmdletEntry("ConvertTo-DbaTimeline", typeof(ConvertToDbaTimelineCommand), null));
            System.Management.Automation.Runspaces.Runspace runspace = RunspaceFactory.CreateRunspace(iss);
            runspace.Open();
            try
            {
                PSObject record = new PSObject();
                record.Properties.Add(new PSNoteProperty("TypeName", "AgentJobHistory"));
                record.Properties.Add(new PSNoteProperty("SqlInstance", "srv1"));
                record.Properties.Add(new PSNoteProperty("InstanceName", "MSSQLSERVER"));
                record.Properties.Add(new PSNoteProperty("Job", "job1"));
                record.Properties.Add(new PSNoteProperty("Status", "Failed"));
                record.Properties.Add(new PSNoteProperty("StartDate", new object[] { new DateTime(2026, 1, 1, 8, 0, 0) }));
                record.Properties.Add(new PSNoteProperty("EndDate", new DateTime(2026, 1, 1, 9, 0, 0)));
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.Runspace = runspace;
                    shell.AddCommand("ConvertTo-DbaTimeline").AddParameter("InputObject", new object[] { record });
                    Collection<PSObject> output = shell.Invoke();
                    Assert.AreEqual(3, output.Count, "header, body array, footer");
                    object[] body = (object[])output[1].BaseObject;
                    string bodyRow = (string)body[0];
                    // Row template: ['v','h','style',{start}, {end}], - an empty start
                    // leaves nothing between the style quote's comma and the end's comma.
                    StringAssert.Contains(bodyRow, "',, new Date(", "the array-valued StartDate renders EMPTY while EndDate still renders");
                }
            }
            finally
            {
                runspace.Dispose();
            }
        }

        [TestMethod]
        public void JsDate_FollowsTheCurrentCultureCalendar()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // th-TH renders Buddhist-era years (2018 + 543 = 2561), months unchanged -
                // PS ground truth both editions. An invariant-culture "fix" emits 2018.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("th-TH");
                Assert.AreEqual("new Date(2561, 7, 14, 07, 40, 42)", Convert(new DateTime(2018, 8, 14, 7, 40, 42)));
                // ar-SA renders the full UmAlQura date: 2018-08-14 is 1439-12-03, so the
                // 0-based month is 11 - year, month AND day all diverge from Gregorian.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                Assert.AreEqual("new Date(1439, 11, 03, 07, 40, 42)", Convert(new DateTime(2018, 8, 14, 7, 40, 42)));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [TestMethod]
        public void JsDate_OutOfCalendarDatesRenderEmptyNotPartial()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // The TB-017 correction pin: UmAlQura supports 1900-04-30..2077-11-16, and
                // SQL sentinel dates reach back to 1753 - REACHABLE. PS ground truth both
                // editions: Get-Date's ArgumentOutOfRangeException is statement-terminating
                // (probed: -ErrorAction does not govern it), the whole template dies and
                // the field renders EMPTY. The port previously fabricated the partial
                // render "new Date(, -1, , , , )" here - this pin fails that shape.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                Assert.AreEqual("", Convert(new DateTime(1800, 1, 1, 0, 0, 0)));
                Assert.AreEqual("", Convert(new DateTime(1753, 1, 1, 0, 0, 0)), "the SQL datetime minimum sentinel");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }
}
