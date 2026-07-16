using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Dataplat.Dbatools.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dataplat.Dbatools.Commands.Test
{
    /// <summary>
    /// Engine host for the collection-join pins: ConvertTimelineStatusColor routes non-array
    /// collections through PsText -> GetOfsSeparator, which reads the session $OFS - a bare
    /// instance swallows that SessionState access and silently defaults to a space, so only
    /// a cmdlet EXECUTING in a real runspace can prove the join consults $OFS (codex TB-013
    /// r3). Derives from the product cmdlet (unsealed for exactly this) so the REAL
    /// GetOfsSeparator runs against a real SessionState; the pipeline overrides bypass the
    /// timeline logic. The inherited mandatory InputObject must still be bound - callers
    /// pass a dummy.
    /// </summary>
    [Cmdlet("Test", "TimelineStatusColorHost")]
    public sealed class TestTimelineStatusColorHostCommand : ConvertToDbaTimelineCommand
    {
        [Parameter]
        public object StatusValue { get; set; }

        protected override void BeginProcessing()
        {
        }

        protected override void ProcessRecord()
        {
            WriteObject(ConvertTimelineStatusColor(StatusValue));
        }

        protected override void EndProcessing()
        {
        }
    }

    /// <summary>
    /// TB-013 coverage for the absorbed Convert-DbaTimelineStatusColor helper inside
    /// ConvertToDbaTimelineCommand. Expected values ground-truthed against the PS helper
    /// on both editions (probe 2026-07-16): the five status colors are case-insensitive
    /// (PS switch semantics), everything else - including the British "Cancelled" and a
    /// padded " Failed " - falls to the magenta default, and the binding-rejection shapes
    /// (null, empty string, arrays even single-element) render the Style field EMPTY, the
    /// documented lab-proven behavior of the mandatory [string]$Status calculated-property
    /// call site. SCALAR paths (and the array early-return) touch no SessionState, so a
    /// bare instance suffices for those pins only; the collection $OFS-join pins run
    /// through the engine-hosted subclass above (codex TB-013 r3).
    /// </summary>
    [TestClass]
    public class TimelineStatusColorTest
    {
        private static string Convert(object status)
        {
            ConvertToDbaTimelineCommand host = new ConvertToDbaTimelineCommand();
            return host.ConvertTimelineStatusColor(status);
        }

        private static string InvokeHosted(object status, string ofs)
        {
            InitialSessionState iss = InitialSessionState.CreateDefault2();
            iss.Commands.Add(new SessionStateCmdletEntry("Test-TimelineStatusColorHost", typeof(TestTimelineStatusColorHostCommand), null));
            System.Management.Automation.Runspaces.Runspace runspace = RunspaceFactory.CreateRunspace(iss);
            runspace.Open();
            try
            {
                if (ofs != null)
                {
                    runspace.SessionStateProxy.SetVariable("OFS", ofs);
                }
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.Runspace = runspace;
                    shell.AddCommand("Test-TimelineStatusColorHost")
                        .AddParameter("StatusValue", status)
                        .AddParameter("InputObject", new object[] { "dummy" });
                    Collection<PSObject> output = shell.Invoke();
                    Assert.AreEqual(1, output.Count, "the host cmdlet emits exactly one color string");
                    return (string)output[0].BaseObject;
                }
            }
            finally
            {
                runspace.Dispose();
            }
        }

        [TestMethod]
        public void StatusColor_FiveStatusesAreCaseInsensitive()
        {
            // Codex r3: every one of the five needs an alternate-casing pin, or a
            // case-SENSITIVE comparison on the untested ones would falsely pass.
            Assert.AreEqual("#FF3D3D", Convert("Failed"));
            Assert.AreEqual("#FF3D3D", Convert("failed"));
            Assert.AreEqual("#FF3D3D", Convert("FAILED"));
            Assert.AreEqual("#36B300", Convert("Succeeded"));
            Assert.AreEqual("#36B300", Convert("sUcCeEdEd"));
            Assert.AreEqual("#FFFF00", Convert("Retry"));
            Assert.AreEqual("#FFFF00", Convert("RETRY"));
            Assert.AreEqual("#C2C2C2", Convert("Canceled"));
            Assert.AreEqual("#C2C2C2", Convert("canceled"));
            Assert.AreEqual("#00CCFF", Convert("In Progress"));
            Assert.AreEqual("#00CCFF", Convert("in progress"));
            Assert.AreEqual("#00CCFF", Convert("IN PROGRESS"));
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
        public void StatusColor_NonArrayCollectionsBindViaTheOfsJoin()
        {
            // Codex r1: arrays are REJECTED by the scalar [string] binder, but non-array
            // collections DO bind, converting with the $OFS join (lab-proven by the
            // command's W-row; re-probed both editions 2026-07-16). Codex r3: these pins
            // must run ENGINE-HOSTED - a bare instance swallows the SessionState access
            // and defaults to a space, so it cannot distinguish a real $OFS read from a
            // hard-coded separator.
            ArrayList single = new ArrayList();
            single.Add("Succeeded");
            Assert.AreEqual("#36B300", InvokeHosted(single, null), "single-element non-array collection binds and resolves");
            // The JOIN discriminator (codex r2): {"in","progress"} resolves cyan ONLY if
            // the elements space-join to "in progress" - any wrong conversion misses the
            // case and gives magenta instead. PS ground truth both editions: #00CCFF.
            System.Collections.Generic.List<string> joinParts = new System.Collections.Generic.List<string>();
            joinParts.Add("in");
            joinParts.Add("progress");
            Assert.AreEqual("#00CCFF", InvokeHosted(joinParts, null), "multi-element collection OFS-joins with the default space");
        }

        [TestMethod]
        public void StatusColor_TheJoinConsultsTheSessionOfs()
        {
            // Codex r3: prove the separator comes from the session $OFS, not a hard-coded
            // space. Ground truth probed on both editions 2026-07-16 ($OFS is honored by
            // the binder's collection-to-[string] conversion, including inside the
            // calculated-property call site):
            //   $OFS = "rog": {"in p","ress"} joins to "in progress" -> #00CCFF. Only an
            //   implementation READING $OFS can produce cyan here; a hard-coded space
            //   yields "in p ress" -> magenta.
            System.Collections.Generic.List<string> glued = new System.Collections.Generic.List<string>();
            glued.Add("in p");
            glued.Add("ress");
            Assert.AreEqual("#00CCFF", InvokeHosted(glued, "rog"), "a custom $OFS is read from the session and used as the join separator");
            //   $OFS = ",": {"in","progress"} joins to "in,progress" and misses -> #FF00CC.
            System.Collections.Generic.List<string> commaParts = new System.Collections.Generic.List<string>();
            commaParts.Add("in");
            commaParts.Add("progress");
            Assert.AreEqual("#FF00CC", InvokeHosted(commaParts, ","), "a custom $OFS breaks the space-joined match");
            // The bare-instance FALLBACK contract (not caller-reachable parity - the real
            // cmdlet always executes with a SessionState): outside the engine the swallowed
            // SessionState access defaults the separator to a single space.
            System.Collections.Generic.List<string> fallbackParts = new System.Collections.Generic.List<string>();
            fallbackParts.Add("in");
            fallbackParts.Add("progress");
            Assert.AreEqual("#00CCFF", Convert(fallbackParts), "engine-less fallback separator is the documented single space");
        }

        [TestMethod]
        public void StatusColor_PsObjectWrappingIsTransparent()
        {
            Assert.AreEqual("#36B300", Convert(PSObject.AsPSObject("Succeeded")));
            Assert.AreEqual("", Convert(PSObject.AsPSObject(new object[] { "Failed" })), "the array check unwraps PSObject first");
        }
    }
}
