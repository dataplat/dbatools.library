using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// TB-013 coverage for the absorbed Convert-DbaTimelineStatusColor helper inside
    /// ConvertToDbaTimelineCommand. Expected values ground-truthed against the PS helper
    /// on both editions (probe 2026-07-16): the five status colors are case-insensitive
    /// (PS switch semantics), everything else - including the British "Cancelled" and a
    /// padded " Failed " - falls to the magenta default, and the binding-rejection shapes
    /// (null, empty string, arrays even single-element) render the Style field EMPTY, the
    /// documented lab-proven behavior of the mandatory [string]$Status calculated-property
    /// call site. Scalar paths touch no SessionState, so a bare instance suffices.
    /// </summary>
    [TestClass]
    public class TimelineStatusColorTest
    {
        private static string Convert(object status)
        {
            ConvertToDbaTimelineCommand host = new ConvertToDbaTimelineCommand();
            return host.ConvertTimelineStatusColor(status);
        }

        [TestMethod]
        public void StatusColor_FiveStatusesAreCaseInsensitive()
        {
            Assert.AreEqual("#FF3D3D", Convert("Failed"));
            Assert.AreEqual("#FF3D3D", Convert("failed"));
            Assert.AreEqual("#FF3D3D", Convert("FAILED"));
            Assert.AreEqual("#36B300", Convert("Succeeded"));
            Assert.AreEqual("#FFFF00", Convert("Retry"));
            Assert.AreEqual("#C2C2C2", Convert("Canceled"));
            Assert.AreEqual("#00CCFF", Convert("in progress"));
        }

        [TestMethod]
        public void StatusColor_EverythingElseIsTheMagentaDefault()
        {
            // The British spelling is NOT the American switch case - real Agent data
            // never produces it, but the pin keeps a well-meaning "fix" honest.
            Assert.AreEqual("#FF00CC", Convert("Cancelled"));
            Assert.AreEqual("#FF00CC", Convert("whatever"));
            Assert.AreEqual("#FF00CC", Convert(" Failed "), "no trimming - PS switch equality is exact apart from case");
            Assert.AreEqual("#FF00CC", Convert(true), "a non-string scalar converts to its string form (True) and misses every case");
        }

        [TestMethod]
        public void StatusColor_BindingRejectionShapesRenderEmpty()
        {
            // The PS helper's Mandatory [string]$Status REJECTS these bindings
            // (ParameterBindingValidationException, probed both editions); in the
            // calculated property that error leaves the Style field EMPTY, never magenta.
            Assert.AreEqual("", Convert(null));
            Assert.AreEqual("", Convert(""));
            Assert.AreEqual("", Convert(new object[] { "Failed" }), "arrays never bind to a scalar [string] parameter, even single-element");
            Assert.AreEqual("", Convert(new string[0]));
        }

        [TestMethod]
        public void StatusColor_PsObjectWrappingIsTransparent()
        {
            Assert.AreEqual("#36B300", Convert(PSObject.AsPSObject("Succeeded")));
            Assert.AreEqual("", Convert(PSObject.AsPSObject(new object[] { "Failed" })), "the array check unwraps PSObject first");
        }
    }
}
