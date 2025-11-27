using System;
using System.Globalization;

namespace Dataplat.Dbatools.Csv.TypeConverters
{
    /// <summary>
    /// Converts string values to Int16 (short) values.
    /// Supports culture-aware parsing.
    /// </summary>
    public sealed class Int16Converter : TypeConverterBase<short>, ICultureAwareConverter
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

        public bool TryConvert(string value, CultureInfo culture, out object result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = (short)0;
                return false;
            }
            if (short.TryParse(value.Trim(), NumberStyles, culture ?? FormatProvider, out short parsed))
            {
                result = parsed;
                return true;
            }
            result = (short)0;
            return false;
        }
    }

    /// <summary>
    /// Converts string values to Int32 (int) values.
    /// Supports culture-aware parsing.
    /// </summary>
    public sealed class Int32Converter : TypeConverterBase<int>, ICultureAwareConverter
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

        public bool TryConvert(string value, CultureInfo culture, out object result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0;
                return false;
            }
            if (int.TryParse(value.Trim(), NumberStyles, culture ?? FormatProvider, out int parsed))
            {
                result = parsed;
                return true;
            }
            result = 0;
            return false;
        }
    }

    /// <summary>
    /// Converts string values to Int64 (long) values.
    /// Supports culture-aware parsing.
    /// </summary>
    public sealed class Int64Converter : TypeConverterBase<long>, ICultureAwareConverter
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

        public bool TryConvert(string value, CultureInfo culture, out object result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0L;
                return false;
            }
            if (long.TryParse(value.Trim(), NumberStyles, culture ?? FormatProvider, out long parsed))
            {
                result = parsed;
                return true;
            }
            result = 0L;
            return false;
        }
    }

    /// <summary>
    /// Converts string values to Single (float) values.
    /// Supports culture-aware parsing for decimal separators.
    /// </summary>
    public sealed class SingleConverter : TypeConverterBase<float>, ICultureAwareConverter
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

        public bool TryConvert(string value, CultureInfo culture, out object result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0f;
                return false;
            }
            if (float.TryParse(value.Trim(), NumberStyles, culture ?? FormatProvider, out float parsed))
            {
                result = parsed;
                return true;
            }
            result = 0f;
            return false;
        }
    }

    /// <summary>
    /// Converts string values to Double values.
    /// Supports culture-aware parsing for decimal separators.
    /// </summary>
    public sealed class DoubleConverter : TypeConverterBase<double>, ICultureAwareConverter
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

        public bool TryConvert(string value, CultureInfo culture, out object result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0d;
                return false;
            }
            if (double.TryParse(value.Trim(), NumberStyles, culture ?? FormatProvider, out double parsed))
            {
                result = parsed;
                return true;
            }
            result = 0d;
            return false;
        }
    }

    /// <summary>
    /// Converts string values to Decimal values.
    /// Supports culture-aware parsing for decimal separators.
    /// Addresses LumenWorks issue #66 for Czech locale decimal parsing.
    /// </summary>
    public sealed class DecimalConverter : TypeConverterBase<decimal>, ICultureAwareConverter
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

        public bool TryConvert(string value, CultureInfo culture, out object result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = 0m;
                return false;
            }
            if (decimal.TryParse(value.Trim(), NumberStyles, culture ?? FormatProvider, out decimal parsed))
            {
                result = parsed;
                return true;
            }
            result = 0m;
            return false;
        }
    }

    /// <summary>
    /// Converts string values to Byte values.
    /// Supports culture-aware parsing.
    /// </summary>
    public sealed class ByteConverter : TypeConverterBase<byte>, ICultureAwareConverter
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

        public bool TryConvert(string value, CultureInfo culture, out object result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = (byte)0;
                return false;
            }
            if (byte.TryParse(value.Trim(), NumberStyles, culture ?? FormatProvider, out byte parsed))
            {
                result = parsed;
                return true;
            }
            result = (byte)0;
            return false;
        }
    }
}
