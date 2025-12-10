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
            var converter = MoneyConverter.Default;

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
            var converter = MoneyConverter.Default;

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

            // NumberStyles.Currency includes AllowExponent, so scientific notation should work
            Assert.IsTrue(converter.TryConvert("1.5E3", out decimal result));
            Assert.AreEqual(1500m, result);

            Assert.IsTrue(converter.TryConvert("2.5E-2", out result));
            Assert.AreEqual(0.025m, result);
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
