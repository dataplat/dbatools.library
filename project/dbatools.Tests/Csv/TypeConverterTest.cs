using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dataplat.Dbatools.Csv.TypeConverters;

namespace Dataplat.Dbatools.Csv.Tests
{
    [TestClass]
    public class TypeConverterTest
    {
        #region Boolean Converter Tests

        [DataRow("true", true)]
        [DataRow("True", true)]
        [DataRow("TRUE", true)]
        [DataRow("false", false)]
        [DataRow("False", false)]
        [DataRow("FALSE", false)]
        [DataRow("1", true)]
        [DataRow("0", false)]
        [DataRow("yes", true)]
        [DataRow("no", false)]
        [DataRow("y", true)]
        [DataRow("n", false)]
        [DataRow("on", true)]
        [DataRow("off", false)]
        [DataTestMethod]
        public void TestBooleanConverter(string input, bool expected)
        {
            var converter = BooleanConverter.Default;
            Assert.IsTrue(converter.TryConvert(input, out bool result));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void TestBooleanConverterInvalidInput()
        {
            var converter = BooleanConverter.Default;
            Assert.IsFalse(converter.TryConvert("invalid", out bool _));
            Assert.IsFalse(converter.TryConvert("", out bool _));
            Assert.IsFalse(converter.TryConvert(null, out bool _));
        }

        #endregion

        #region GUID Converter Tests

        [DataRow("550e8400-e29b-41d4-a716-446655440000")]
        [DataRow("550E8400-E29B-41D4-A716-446655440000")]
        [DataRow("{550e8400-e29b-41d4-a716-446655440000}")]
        [DataRow("550e8400e29b41d4a716446655440000")]
        [DataTestMethod]
        public void TestGuidConverter(string input)
        {
            var converter = GuidConverter.Default;
            Assert.IsTrue(converter.TryConvert(input, out Guid result));
            Assert.AreEqual(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), result);
        }

        [TestMethod]
        public void TestGuidConverterInvalidInput()
        {
            var converter = GuidConverter.Default;
            Assert.IsFalse(converter.TryConvert("invalid", out Guid _));
            Assert.IsFalse(converter.TryConvert("", out Guid _));
            Assert.IsFalse(converter.TryConvert(null, out Guid _));
        }

        #endregion

        #region DateTime Converter Tests

        [TestMethod]
        public void TestDateTimeConverterISO()
        {
            var converter = DateTimeConverter.Default;
            Assert.IsTrue(converter.TryConvert("2024-01-15", out DateTime result));
            Assert.AreEqual(2024, result.Year);
            Assert.AreEqual(1, result.Month);
            Assert.AreEqual(15, result.Day);
        }

        [TestMethod]
        public void TestDateTimeConverterWithTime()
        {
            var converter = DateTimeConverter.Default;
            Assert.IsTrue(converter.TryConvert("2024-01-15 10:30:45", out DateTime result));
            Assert.AreEqual(10, result.Hour);
            Assert.AreEqual(30, result.Minute);
            Assert.AreEqual(45, result.Second);
        }

        [TestMethod]
        public void TestDateTimeConverterDayMonthYear()
        {
            // Addresses issue #9694: Oracle date format (dd-MM-yyyy)
            var converter = DateTimeConverter.DayMonthYear;
            Assert.IsTrue(converter.TryConvert("31-05-2024 00:00:00", out DateTime result));
            Assert.AreEqual(2024, result.Year);
            Assert.AreEqual(5, result.Month);
            Assert.AreEqual(31, result.Day);
        }

        [TestMethod]
        public void TestDateTimeConverterCustomFormat()
        {
            var converter = DateTimeConverter.WithFormats(new[] { "dd/MMM/yyyy" });
            Assert.IsTrue(converter.TryConvert("15/Jan/2024", out DateTime result));
            Assert.AreEqual(2024, result.Year);
            Assert.AreEqual(1, result.Month);
            Assert.AreEqual(15, result.Day);
        }

        [TestMethod]
        public void TestDateTimeConverterSwissCulture()
        {
            // de-CH uses dd.MM.yyyy format - month and day must not be swapped
            var culture = System.Globalization.CultureInfo.GetCultureInfo("de-CH");
            var converter = DateTimeConverter.Default;

            // Day=1, Month=5 - ambiguous (both <= 12), must parse as May 1st not Jan 5th
            Assert.IsTrue(converter.TryConvert("01.05.2026 12:00:00", culture, out object result));
            var dt = (DateTime)result;
            Assert.AreEqual(2026, dt.Year);
            Assert.AreEqual(5, dt.Month, "Month should be 5 (May), not 1");
            Assert.AreEqual(1, dt.Day, "Day should be 1, not 5");

            // Day=2, Month=4 - ambiguous
            Assert.IsTrue(converter.TryConvert("02.04.2026 17:09:41", culture, out result));
            dt = (DateTime)result;
            Assert.AreEqual(4, dt.Month, "Month should be 4 (April), not 2");
            Assert.AreEqual(2, dt.Day, "Day should be 2, not 4");

            // Day=14, Month=4 - unambiguous (14 > 12, can only be day)
            Assert.IsTrue(converter.TryConvert("14.04.2026 17:09:41", culture, out result));
            dt = (DateTime)result;
            Assert.AreEqual(4, dt.Month);
            Assert.AreEqual(14, dt.Day);

            // Day=11, Month=6 - ambiguous
            Assert.IsTrue(converter.TryConvert("11.06.2026 17:10:08", culture, out result));
            dt = (DateTime)result;
            Assert.AreEqual(6, dt.Month, "Month should be 6 (June), not 11");
            Assert.AreEqual(11, dt.Day, "Day should be 11, not 6");
        }

        [TestMethod]
        public void TestDateTimeConverterGermanCulture()
        {
            // de-DE also uses dd.MM.yyyy format
            var culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            var converter = DateTimeConverter.Default;

            Assert.IsTrue(converter.TryConvert("05.03.2026", culture, out object result));
            var dt = (DateTime)result;
            Assert.AreEqual(3, dt.Month, "Month should be 3 (March), not 5");
            Assert.AreEqual(5, dt.Day, "Day should be 5, not 3");
        }

        [TestMethod]
        public void TestDateTimeConverterFrenchCulture()
        {
            // fr-FR uses dd/MM/yyyy format
            var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            var converter = DateTimeConverter.Default;

            Assert.IsTrue(converter.TryConvert("05/03/2026", culture, out object result));
            var dt = (DateTime)result;
            Assert.AreEqual(3, dt.Month, "Month should be 3 (March), not 5");
            Assert.AreEqual(5, dt.Day, "Day should be 5, not 3");
        }

        #endregion

        #region Numeric Converter Tests

        [TestMethod]
        public void TestInt32Converter()
        {
            var converter = Int32Converter.Default;

            Assert.IsTrue(converter.TryConvert("42", out int result));
            Assert.AreEqual(42, result);

            Assert.IsTrue(converter.TryConvert("-100", out result));
            Assert.AreEqual(-100, result);

            Assert.IsFalse(converter.TryConvert("not a number", out _));
        }

        [TestMethod]
        public void TestInt64Converter()
        {
            var converter = Int64Converter.Default;

            Assert.IsTrue(converter.TryConvert("9999999999", out long result));
            Assert.AreEqual(9999999999L, result);
        }

        [TestMethod]
        public void TestDoubleConverter()
        {
            var converter = DoubleConverter.Default;

            Assert.IsTrue(converter.TryConvert("3.14159", out double result));
            Assert.AreEqual(3.14159, result, 0.00001);

            Assert.IsTrue(converter.TryConvert("-1.5e10", out result));
            Assert.AreEqual(-1.5e10, result, 0.01);
        }

        [TestMethod]
        public void TestDecimalConverter()
        {
            var converter = DecimalConverter.Default;

            Assert.IsTrue(converter.TryConvert("123.45", out decimal result));
            Assert.AreEqual(123.45m, result);

            Assert.IsTrue(converter.TryConvert("1234567890.123456", out result));
            Assert.AreEqual(1234567890.123456m, result);
        }

        [TestMethod]
        public void TestDecimalConverterScientificNotation()
        {
            var converter = DecimalConverter.Default;

            // Test case from issue #35
            Assert.IsTrue(converter.TryConvert("1.2345678E5", out decimal result));
            Assert.AreEqual(123456.78m, result);

            // Additional scientific notation tests
            Assert.IsTrue(converter.TryConvert("1.5e10", out result));
            Assert.AreEqual(15000000000m, result);

            Assert.IsTrue(converter.TryConvert("2.5E-3", out result));
            Assert.AreEqual(0.0025m, result);

            Assert.IsTrue(converter.TryConvert("-3.14E2", out result));
            Assert.AreEqual(-314m, result);
        }

        [TestMethod]
        public void TestDecimalConverterThousandsSeparator()
        {
            var converter = DecimalConverter.Default;

            // Test thousands separator (culture-aware)
            Assert.IsTrue(converter.TryConvert("1,234.56", out decimal result));
            Assert.AreEqual(1234.56m, result);

            // Test multiple thousands separators
            Assert.IsTrue(converter.TryConvert("1,234,567.89", out result));
            Assert.AreEqual(1234567.89m, result);

            // Test negative with thousands separator
            Assert.IsTrue(converter.TryConvert("-1,234.56", out result));
            Assert.AreEqual(-1234.56m, result);
        }

        [TestMethod]
        public void TestDecimalConverterEdgeCases()
        {
            var converter = DecimalConverter.Default;

            // Test zero
            Assert.IsTrue(converter.TryConvert("0", out decimal result));
            Assert.AreEqual(0m, result);

            // Test zero in scientific notation
            Assert.IsTrue(converter.TryConvert("0.0E0", out result));
            Assert.AreEqual(0m, result);

            // Test very small number
            Assert.IsTrue(converter.TryConvert("1E-28", out result));
            Assert.AreEqual(0.0000000000000000000000000001m, result);

            // Test near maximum value (decimal.MaxValue is ~7.9E+28)
            Assert.IsTrue(converter.TryConvert("1E+28", out result));
            Assert.AreEqual(10000000000000000000000000000m, result);

            // Test overflow - should fail gracefully
            Assert.IsFalse(converter.TryConvert("1E+30", out _));

            // Test invalid scientific notation
            Assert.IsFalse(converter.TryConvert("1E", out _));
            Assert.IsFalse(converter.TryConvert("E5", out _));
        }

        [TestMethod]
        public void TestDecimalConverterDifferentCultures()
        {
            // Test with German culture (uses comma as decimal separator)
            var germanConverter = new DecimalConverter();
            germanConverter.FormatProvider = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

            Assert.IsTrue(germanConverter.TryConvert("1234,56", out decimal result));
            Assert.AreEqual(1234.56m, result);

            // Test with French culture (uses culture-specific thousands separator, comma as decimal)
            var frenchCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            var frenchConverter = new DecimalConverter();
            frenchConverter.FormatProvider = frenchCulture;

            string frenchValue = string.Format(frenchCulture, "1{0}234,56", frenchCulture.NumberFormat.NumberGroupSeparator);
            Assert.IsTrue(frenchConverter.TryConvert(frenchValue, out result));
            Assert.AreEqual(1234.56m, result);
        }

        [TestMethod]
        public void TestMoneyConverter()
        {
            var converter = MoneyConverter.Default;

            // Test basic decimal values
            Assert.IsTrue(converter.TryConvert("123.45", out decimal result));
            Assert.AreEqual(123.45m, result);

            // Test negative values
            Assert.IsTrue(converter.TryConvert("-99.99", out result));
            Assert.AreEqual(-99.99m, result);
        }

        [TestMethod]
        public void TestMoneyConverterWithCurrencySymbols()
        {
            var converter = new MoneyConverter();
            converter.FormatProvider = System.Globalization.CultureInfo.GetCultureInfo("en-US");

            // Test US dollar sign
            Assert.IsTrue(converter.TryConvert("$123.45", out decimal result));
            Assert.AreEqual(123.45m, result);

            // Test negative with dollar sign
            Assert.IsTrue(converter.TryConvert("-$99.99", out result));
            Assert.AreEqual(-99.99m, result);

            // Test parentheses for negative (accounting format)
            Assert.IsTrue(converter.TryConvert("($50.00)", out result));
            Assert.AreEqual(-50.00m, result);
        }

        [TestMethod]
        public void TestMoneyConverterWithThousandsSeparator()
        {
            var converter = new MoneyConverter();
            converter.FormatProvider = System.Globalization.CultureInfo.GetCultureInfo("en-US");

            // Test with thousands separator
            Assert.IsTrue(converter.TryConvert("$1,234.56", out decimal result));
            Assert.AreEqual(1234.56m, result);

            // Test large number with currency
            Assert.IsTrue(converter.TryConvert("$1,234,567.89", out result));
            Assert.AreEqual(1234567.89m, result);
        }

        [TestMethod]
        public void TestMoneyConverterScientificNotation()
        {
            var converter = MoneyConverter.Default;

            // NumberStyles.Currency does NOT include AllowExponent
            Assert.IsFalse(converter.TryConvert("1.5E3", out decimal _));
            Assert.IsFalse(converter.TryConvert("2.5E-2", out decimal _));
        }

        [TestMethod]
        public void TestMoneyConverterInvalidInput()
        {
            var converter = MoneyConverter.Default;

            Assert.IsFalse(converter.TryConvert("invalid", out _));
            Assert.IsFalse(converter.TryConvert("", out _));
            Assert.IsFalse(converter.TryConvert(null, out _));
        }

        #endregion

        #region Vector Converter Tests

        [TestMethod]
        public void TestVectorConverterJsonArrayFormat()
        {
            var converter = VectorConverter.Default;

            // Test JSON array format
            Assert.IsTrue(converter.TryConvert("[0.1, 0.2, 0.3]", out float[] result));
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(0.1f, result[0], 0.0001f);
            Assert.AreEqual(0.2f, result[1], 0.0001f);
            Assert.AreEqual(0.3f, result[2], 0.0001f);
        }

        [TestMethod]
        public void TestVectorConverterCommaSeparated()
        {
            var converter = VectorConverter.Default;

            // Test comma-separated format (no brackets)
            Assert.IsTrue(converter.TryConvert("0.5, 1.0, 1.5", out float[] result));
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(0.5f, result[0], 0.0001f);
            Assert.AreEqual(1.0f, result[1], 0.0001f);
            Assert.AreEqual(1.5f, result[2], 0.0001f);
        }

        [TestMethod]
        public void TestVectorConverterScientificNotation()
        {
            var converter = VectorConverter.Default;

            // Test scientific notation in vectors
            Assert.IsTrue(converter.TryConvert("[1.5e-3, 2.0E2, -3.5e1]", out float[] result));
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(0.0015f, result[0], 0.000001f);
            Assert.AreEqual(200.0f, result[1], 0.0001f);
            Assert.AreEqual(-35.0f, result[2], 0.0001f);
        }

        [TestMethod]
        public void TestVectorConverterNegativeValues()
        {
            var converter = VectorConverter.Default;

            // Test negative values
            Assert.IsTrue(converter.TryConvert("[-0.5, -1.0, -1.5]", out float[] result));
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(-0.5f, result[0], 0.0001f);
            Assert.AreEqual(-1.0f, result[1], 0.0001f);
            Assert.AreEqual(-1.5f, result[2], 0.0001f);
        }

        [TestMethod]
        public void TestVectorConverterLargeEmbedding()
        {
            var converter = VectorConverter.Default;

            // Test realistic embedding size (e.g., OpenAI ada-002 uses 1536 dimensions)
            // Create a sample with 100 dimensions for testing
            string vectorString = "[" + string.Join(", ", Enumerable.Range(0, 100).Select(i => (i * 0.01f).ToString("F3"))) + "]";

            Assert.IsTrue(converter.TryConvert(vectorString, out float[] result));
            Assert.AreEqual(100, result.Length);
            Assert.AreEqual(0.0f, result[0], 0.0001f);
            Assert.AreEqual(0.99f, result[99], 0.0001f);
        }

        [TestMethod]
        public void TestVectorConverterWhitespaceHandling()
        {
            var converter = VectorConverter.Default;

            // Test various whitespace scenarios
            Assert.IsTrue(converter.TryConvert("  [  0.1  ,  0.2  ,  0.3  ]  ", out float[] result));
            Assert.AreEqual(3, result.Length);

            Assert.IsTrue(converter.TryConvert("0.1,0.2,0.3", out result)); // No spaces
            Assert.AreEqual(3, result.Length);
        }

        [TestMethod]
        public void TestVectorConverterInvalidInput()
        {
            var converter = VectorConverter.Default;

            // Test invalid inputs
            Assert.IsFalse(converter.TryConvert("", out _));
            Assert.IsFalse(converter.TryConvert(null, out _));
            Assert.IsFalse(converter.TryConvert("[]", out _)); // Empty array
            Assert.IsFalse(converter.TryConvert("[not, a, number]", out _));
            Assert.IsFalse(converter.TryConvert("[0.1, invalid, 0.3]", out _));
            Assert.IsFalse(converter.TryConvert("[", out _)); // Malformed
        }

        [TestMethod]
        public void TestVectorConverterSingleValue()
        {
            var converter = VectorConverter.Default;

            // Test single-value vector
            Assert.IsTrue(converter.TryConvert("[42.5]", out float[] result));
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(42.5f, result[0], 0.0001f);
        }

        #endregion

        #region Type Converter Registry Tests

        [TestMethod]
        public void TestRegistryDefaultConverters()
        {
            var registry = TypeConverterRegistry.Default;

            Assert.IsNotNull(registry.GetConverter<bool>());
            Assert.IsNotNull(registry.GetConverter<Guid>());
            Assert.IsNotNull(registry.GetConverter<DateTime>());
            Assert.IsNotNull(registry.GetConverter<int>());
            Assert.IsNotNull(registry.GetConverter<long>());
            Assert.IsNotNull(registry.GetConverter<double>());
            Assert.IsNotNull(registry.GetConverter<decimal>());
        }

        [TestMethod]
        public void TestRegistryTryConvert()
        {
            var registry = TypeConverterRegistry.Default;

            Assert.IsTrue(registry.TryConvert("true", typeof(bool), out object result));
            Assert.AreEqual(true, result);

            Assert.IsTrue(registry.TryConvert("42", typeof(int), out result));
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void TestRegistryNullableTypes()
        {
            var registry = TypeConverterRegistry.Default;

            // Empty string should return null for nullable types
            Assert.IsTrue(registry.TryConvert("", typeof(int?), out object result));
            Assert.IsNull(result);

            // Valid value should convert
            Assert.IsTrue(registry.TryConvert("42", typeof(int?), out result));
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void TestRegistryCustomConverter()
        {
            var registry = TypeConverterRegistry.Default.Clone();
            var customBoolConverter = new BooleanConverter
            {
                CustomTrueValues = new System.Collections.Generic.HashSet<string> { "si", "oui" }
            };
            registry.Register(customBoolConverter);

            Assert.IsTrue(registry.TryConvert("si", typeof(bool), out object result));
            Assert.AreEqual(true, result);
        }

        #endregion
    }
}
