using System.Text;

namespace Dataplat.Dbatools.Utility
{
    /// <summary>
    /// private/functions/Convert-ByteToHexString.ps1 parity: renders a byte array as an
    /// uppercase "0x"-prefixed hex string ([byte[]]@(1,100,23,54) -> "0x01641736") for SMO
    /// login sids and hashed passwords. Faithful quirk: a NULL input yields "0x00" - the
    /// helper pipes $InputObject through ForEach-Object, and a bare $null pipes exactly
    /// once, formatting to "" and left-padding to "00" - while an EMPTY array pipes zero
    /// times and yields "0x". Callers span the security (New-DbaLogin, 3 sites) and backup
    /// (Test-DbaBackupEncrypted) families, hence the shared runtime placement (helper
    /// retained at refcount 4).
    /// </summary>
    public static class ByteHex
    {
        /// <summary>The helper's body: "0x" plus each byte as uppercase two-digit hex.</summary>
        public static string Convert(byte[] inputObject)
        {
            StringBuilder outString = new StringBuilder("0x");
            if (inputObject == null)
            {
                // PS: $null | ForEach-Object runs the block once; "{0:X}" -f $null is ""
                // and PadLeft(2, "0") makes it "00".
                outString.Append("00");
                return outString.ToString();
            }
            foreach (byte value in inputObject)
                outString.Append(value.ToString("X2"));
            return outString.ToString();
        }
    }
}
