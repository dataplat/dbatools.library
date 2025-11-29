using System;
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
