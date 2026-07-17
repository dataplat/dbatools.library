using System;
using System.Globalization;
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

        [TestMethod]
        public void JsDate_StringInputConvertsLikeTheDatetimeBinder()
        {
            // The PS [datetime] parameter cast is a LanguagePrimitives conversion; probed:
            // an ISO-ish string binds and renders identically on both editions.
            Assert.AreEqual("new Date(2026, 6, 16, 08, 30, 00)", Convert("2026-07-16 08:30:00"));
        }

        [TestMethod]
        public void JsDate_UnconvertibleAndNullRenderEmpty()
        {
            // PS: the Mandatory [datetime] cast fails statement-terminating inside the
            // calculated property and the whole field is EMPTY (probed both editions).
            Assert.AreEqual("", Convert(null));
            Assert.AreEqual("", Convert("not a date"));
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
