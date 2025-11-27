using System;
using System.Globalization;

namespace Dataplat.Dbatools.Csv.TypeConverters
{
    /// <summary>
    /// Converts string values to Int16 (short) values.
    /// </summary>
    public sealed class Int16Converter : TypeConverterBase<short>
    {
        public static Int16Converter Default { get; } = new Int16Converter();
        public NumberStyles NumberStyles { get; set; } = NumberStyles.Integer;
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        public override bool TryConvert(string value, out short result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }
            return short.TryParse(value.Trim(), NumberStyles, FormatProvider, out result);
        }
    }

    /// <summary>
    /// Converts string values to Int32 (int) values.
    /// </summary>
    public sealed class Int32Converter : TypeConverterBase<int>
    {
        public static Int32Converter Default { get; } = new Int32Converter();
        public NumberStyles NumberStyles { get; set; } = NumberStyles.Integer;
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        public override bool TryConvert(string value, out int result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }
            return int.TryParse(value.Trim(), NumberStyles, FormatProvider, out result);
        }
    }

    /// <summary>
    /// Converts string values to Int64 (long) values.
    /// </summary>
    public sealed class Int64Converter : TypeConverterBase<long>
    {
        public static Int64Converter Default { get; } = new Int64Converter();
        public NumberStyles NumberStyles { get; set; } = NumberStyles.Integer;
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        public override bool TryConvert(string value, out long result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }
            return long.TryParse(value.Trim(), NumberStyles, FormatProvider, out result);
        }
    }

    /// <summary>
    /// Converts string values to Single (float) values.
    /// </summary>
    public sealed class SingleConverter : TypeConverterBase<float>
    {
        public static SingleConverter Default { get; } = new SingleConverter();
        public NumberStyles NumberStyles { get; set; } = NumberStyles.Float | NumberStyles.AllowThousands;
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        public override bool TryConvert(string value, out float result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }
            return float.TryParse(value.Trim(), NumberStyles, FormatProvider, out result);
        }
    }

    /// <summary>
    /// Converts string values to Double values.
    /// </summary>
    public sealed class DoubleConverter : TypeConverterBase<double>
    {
        public static DoubleConverter Default { get; } = new DoubleConverter();
        public NumberStyles NumberStyles { get; set; } = NumberStyles.Float | NumberStyles.AllowThousands;
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        public override bool TryConvert(string value, out double result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }
            return double.TryParse(value.Trim(), NumberStyles, FormatProvider, out result);
        }
    }

    /// <summary>
    /// Converts string values to Decimal values.
    /// </summary>
    public sealed class DecimalConverter : TypeConverterBase<decimal>
    {
        public static DecimalConverter Default { get; } = new DecimalConverter();
        public NumberStyles NumberStyles { get; set; } = NumberStyles.Number;
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        public override bool TryConvert(string value, out decimal result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }
            return decimal.TryParse(value.Trim(), NumberStyles, FormatProvider, out result);
        }
    }

    /// <summary>
    /// Converts string values to Byte values.
    /// </summary>
    public sealed class ByteConverter : TypeConverterBase<byte>
    {
        public static ByteConverter Default { get; } = new ByteConverter();
        public NumberStyles NumberStyles { get; set; } = NumberStyles.Integer;
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        public override bool TryConvert(string value, out byte result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }
            return byte.TryParse(value.Trim(), NumberStyles, FormatProvider, out result);
        }
    }
}
