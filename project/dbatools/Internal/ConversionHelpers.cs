using System;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Dataplat.Dbatools.Internal
{
    /// <summary>
    /// Static conversion utility methods mirroring PS1 functions like
    /// Convert-ByteToHexString, Convert-HexStringToByte, ConvertTo-JsDate, Convert-DbaLSN, etc.
    /// </summary>
    public static class ConversionHelpers
    {
        /// <summary>
        /// Converts a byte array to a hex string prefixed with 0x.
        /// Mirrors Convert-ByteToHexString.ps1.
        /// </summary>
        public static string ByteToHex(byte[] input)
        {
            if (input == null || input.Length == 0)
                return "0x";

            StringBuilder sb = new StringBuilder("0x", 2 + input.Length * 2);
            for (int i = 0; i < input.Length; i++)
                sb.Append(input[i].ToString("X2"));
            return sb.ToString();
        }

        /// <summary>
        /// Converts a hex string (with optional 0x prefix) to a byte array.
        /// Mirrors Convert-HexStringToByte.ps1.
        /// </summary>
        public static byte[] HexToByte(string input)
        {
            if (String.IsNullOrEmpty(input))
                return new byte[0];

            string hex = input;
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || hex.StartsWith("0X"))
                hex = hex.Substring(2);

            if (hex.Length % 2 != 0)
                hex = "0" + hex;

            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            return result;
        }

        /// <summary>
        /// Converts a DateTime to a JavaScript Date constructor string.
        /// Mirrors ConvertTo-JsDate.ps1. Note: JS months are 0-based.
        /// </summary>
        public static string ToJsDate(DateTime inputDate)
        {
            return String.Format("new Date({0}, {1}, {2}, {3}, {4}, {5})",
                inputDate.Year,
                inputDate.Month - 1,
                inputDate.Day,
                inputDate.Hour,
                inputDate.Minute,
                inputDate.Second);
        }

        /// <summary>
        /// Converts an LSN between hex and numeric formats.
        /// Mirrors Convert-DbaLSN.ps1.
        /// Returns a tuple-like result with both Hexadecimal and Numeric representations.
        /// </summary>
        /// <param name="lsn">The LSN in hex (XXXXXXXX:XXXXXXXX:XXXX) or numeric format</param>
        /// <returns>Array of [hexValue, numericValue]</returns>
        public static string[] ConvertLSN(string lsn)
        {
            if (String.IsNullOrEmpty(lsn))
                return null;

            string hexPattern = @"^[a-fA-F0-9]{8}:[a-fA-F0-9]{8}:[a-fA-F0-9]{4}$";
            string numericPattern = @"^[0-9]{15}[0-9]+$";

            if (Regex.IsMatch(lsn, hexPattern))
            {
                // Hex to Numeric
                string[] parts = lsn.Split(':');
                long part1 = Convert.ToInt64(parts[0], 16);
                long part2 = Convert.ToInt64(parts[1], 16);
                long part3 = Convert.ToInt64(parts[2], 16);

                string numeric = String.Format("{0}{1}{2}",
                    part1,
                    part2.ToString().PadLeft(10, '0'),
                    part3.ToString().PadLeft(5, '0'));

                return new string[] { lsn, numeric };
            }
            else if (Regex.IsMatch(lsn, numericPattern))
            {
                // Numeric to Hex
                int len = lsn.Length;
                string section3Str = lsn.Substring(len - 5);
                string section2Str = lsn.Substring(len - 14, 9);
                string section1Str = lsn.Substring(0, len - 14);

                long s1 = Int64.Parse(section1Str);
                long s2 = Int64.Parse(section2Str);
                long s3 = Int64.Parse(section3Str);

                string hexValue = String.Format("{0}:{1}:{2}",
                    s1.ToString("x").PadLeft(8, '0'),
                    s2.ToString("x").PadLeft(8, '0'),
                    s3.ToString("x").PadLeft(4, '0'));

                return new string[] { hexValue, lsn };
            }

            return null;
        }

        /// <summary>
        /// Converts a SecureString to its plaintext representation.
        /// Mirrors ConvertFrom-SecurePass.
        /// </summary>
        public static string FromSecureString(SecureString secureString)
        {
            if (secureString == null)
                return null;

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        /// <summary>
        /// Generates a SQL Server password hash with the appropriate algorithm for the version.
        /// Mirrors Get-PasswordHash.ps1.
        /// </summary>
        /// <param name="password">The password to hash (plaintext string)</param>
        /// <param name="sqlMajorVersion">SQL Server major version number</param>
        /// <param name="salt">Optional 4-byte salt; random if null</param>
        /// <returns>Hex-formatted hash string (e.g., 0x0200...)</returns>
        public static string GetPasswordHash(string password, int sqlMajorVersion, byte[] salt = null)
        {
            if (String.IsNullOrEmpty(password))
                return null;

            // Generate salt if not provided
            if (salt == null || salt.Length != 4)
            {
                salt = new byte[4];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }
            }

            bool useSha512 = sqlMajorVersion >= 11; // SQL 2012+
            string hashVersion = useSha512 ? "0200" : "0100";

            byte[] passwordBytes = Encoding.Unicode.GetBytes(password);
            byte[] dataToHash = new byte[passwordBytes.Length + salt.Length];
            Array.Copy(passwordBytes, 0, dataToHash, 0, passwordBytes.Length);
            Array.Copy(salt, 0, dataToHash, passwordBytes.Length, salt.Length);

            byte[] hash;
            if (useSha512)
            {
                using (var sha = SHA512.Create())
                {
                    hash = sha.ComputeHash(dataToHash);
                }
            }
            else
            {
                using (var sha = SHA1.Create())
                {
                    hash = sha.ComputeHash(dataToHash);
                }
            }

            StringBuilder result = new StringBuilder("0x");
            result.Append(hashVersion);
            for (int i = 0; i < salt.Length; i++)
                result.Append(salt[i].ToString("X2"));
            for (int i = 0; i < hash.Length; i++)
                result.Append(hash[i].ToString("X2"));

            return result.ToString();
        }

        /// <summary>
        /// Generates a random password meeting complexity requirements.
        /// Mirrors Get-RandomPassword.ps1.
        /// </summary>
        /// <param name="length">Password length (default: 15)</param>
        /// <returns>The generated password as plaintext</returns>
        public static string GetRandomPassword(int length = 15)
        {
            if (length < 4)
                length = 4;

            string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            string lower = "abcdefghijklmnopqrstuvwxyz";
            string digits = "0123456789";
            string special = "!#$%&()*+,-./:;<=>?[]^_{|}~";

            string[] groups = new string[] { upper, lower, digits, special };
            char[] password = new char[length];

            using (var rng = RandomNumberGenerator.Create())
            {
                // Ensure at least one from each group
                for (int i = 0; i < 4 && i < length; i++)
                {
                    password[i] = GetRandomChar(rng, groups[i]);
                }

                // Fill remaining
                string allChars = upper + lower + digits + special;
                for (int i = 4; i < length; i++)
                {
                    password[i] = GetRandomChar(rng, allChars);
                }

                // Fisher-Yates shuffle
                for (int i = length - 1; i > 0; i--)
                {
                    int j = GetRandomInt(rng, i + 1);
                    char tmp = password[i];
                    password[i] = password[j];
                    password[j] = tmp;
                }
            }

            return new string(password);
        }

        /// <summary>
        /// Masks the password in a SQL connection string with asterisks.
        /// Mirrors Hide-ConnectionString.ps1.
        /// </summary>
        public static string HideConnectionString(string connectionString)
        {
            if (String.IsNullOrEmpty(connectionString))
                return connectionString;

            try
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
                if (!String.IsNullOrEmpty(builder.Password))
                    builder.Password = "********";
                return builder.ConnectionString;
            }
            catch
            {
                return "Failed to mask the connection string";
            }
        }

        #region Private Helpers
        private static char GetRandomChar(RandomNumberGenerator rng, string chars)
        {
            byte[] data = new byte[4];
            rng.GetBytes(data);
            int index = (int)(BitConverter.ToUInt32(data, 0) % (uint)chars.Length);
            return chars[index];
        }

        private static int GetRandomInt(RandomNumberGenerator rng, int max)
        {
            byte[] data = new byte[4];
            rng.GetBytes(data);
            return (int)(BitConverter.ToUInt32(data, 0) % (uint)max);
        }
        #endregion Private Helpers
    }
}
