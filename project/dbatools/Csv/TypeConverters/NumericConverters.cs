using System;
using System.Globalization;

namespace Dataplat.Dbatools.Csv.TypeConverters
{
    /// <summary>
    /// Converts string values to Int16 (short) values.
    /// Supports culture-aware parsing.
    /// </summary>
    public sealed class Int16Converter : CultureAwareConverterBase<short>
    {
        public static Int16Converter Default { get; } = new Int16Converter();

        public Int16Converter()
        {
            NumberStyles = NumberStyles.Integer;
        }

        protected override bool TryParseCore(string value, NumberStyles styles, IFormatProvider provider, out short result)
            => short.TryParse(value, styles, provider, out result);

#if NET8_0_OR_GREATER
        protected override bool TryParseSpan(ReadOnlySpan<char> value, NumberStyles styles, IFormatProvider provider, out short result)
            => short.TryParse(value, styles, provider, out result);
#endif
    }

    /// <summary>
    /// Converts string values to Int32 (int) values.
    /// Supports culture-aware parsing.
    /// </summary>
    public sealed class Int32Converter : CultureAwareConverterBase<int>
    {
        public static Int32Converter Default { get; } = new Int32Converter();

        public Int32Converter()
        {
            NumberStyles = NumberStyles.Integer;
        }

        protected override bool TryParseCore(string value, NumberStyles styles, IFormatProvider provider, out int result)
            => int.TryParse(value, styles, provider, out result);

#if NET8_0_OR_GREATER
        protected override bool TryParseSpan(ReadOnlySpan<char> value, NumberStyles styles, IFormatProvider provider, out int result)
            => int.TryParse(value, styles, provider, out result);
#endif
    }

    /// <summary>
    /// Converts string values to Int64 (long) values.
    /// Supports culture-aware parsing.
    /// </summary>
    public sealed class Int64Converter : CultureAwareConverterBase<long>
    {
        public static Int64Converter Default { get; } = new Int64Converter();

        public Int64Converter()
        {
            NumberStyles = NumberStyles.Integer;
        }

        protected override bool TryParseCore(string value, NumberStyles styles, IFormatProvider provider, out long result)
            => long.TryParse(value, styles, provider, out result);

#if NET8_0_OR_GREATER
        protected override bool TryParseSpan(ReadOnlySpan<char> value, NumberStyles styles, IFormatProvider provider, out long result)
            => long.TryParse(value, styles, provider, out result);
#endif
    }

    /// <summary>
    /// Converts string values to Single (float) values.
    /// Supports culture-aware parsing for decimal separators.
    /// </summary>
    public sealed class SingleConverter : CultureAwareConverterBase<float>
    {
        public static SingleConverter Default { get; } = new SingleConverter();

        public SingleConverter()
        {
            NumberStyles = NumberStyles.Float | NumberStyles.AllowThousands;
        }

        protected override bool TryParseCore(string value, NumberStyles styles, IFormatProvider provider, out float result)
            => float.TryParse(value, styles, provider, out result);

#if NET8_0_OR_GREATER
        protected override bool TryParseSpan(ReadOnlySpan<char> value, NumberStyles styles, IFormatProvider provider, out float result)
            => float.TryParse(value, styles, provider, out result);
#endif
    }

    /// <summary>
    /// Converts string values to Double values.
    /// Supports culture-aware parsing for decimal separators.
    /// </summary>
    public sealed class DoubleConverter : CultureAwareConverterBase<double>
    {
        public static DoubleConverter Default { get; } = new DoubleConverter();

        public DoubleConverter()
        {
            NumberStyles = NumberStyles.Float | NumberStyles.AllowThousands;
        }

        protected override bool TryParseCore(string value, NumberStyles styles, IFormatProvider provider, out double result)
            => double.TryParse(value, styles, provider, out result);

#if NET8_0_OR_GREATER
        protected override bool TryParseSpan(ReadOnlySpan<char> value, NumberStyles styles, IFormatProvider provider, out double result)
            => double.TryParse(value, styles, provider, out result);
#endif
    }

    /// <summary>
    /// Converts string values to Decimal values.
    /// Supports culture-aware parsing for decimal separators.
    /// Addresses LumenWorks issue #66 for Czech locale decimal parsing.
    /// </summary>
    public sealed class DecimalConverter : CultureAwareConverterBase<decimal>
    {
        public static DecimalConverter Default { get; } = new DecimalConverter();

        public DecimalConverter()
        {
            NumberStyles = NumberStyles.Number;
        }

        protected override bool TryParseCore(string value, NumberStyles styles, IFormatProvider provider, out decimal result)
            => decimal.TryParse(value, styles, provider, out result);

#if NET8_0_OR_GREATER
        protected override bool TryParseSpan(ReadOnlySpan<char> value, NumberStyles styles, IFormatProvider provider, out decimal result)
            => decimal.TryParse(value, styles, provider, out result);
#endif
    }

    /// <summary>
    /// Converts string values to Byte values.
    /// Supports culture-aware parsing.
    /// </summary>
    public sealed class ByteConverter : CultureAwareConverterBase<byte>
    {
        public static ByteConverter Default { get; } = new ByteConverter();

        public ByteConverter()
        {
            NumberStyles = NumberStyles.Integer;
        }

        protected override bool TryParseCore(string value, NumberStyles styles, IFormatProvider provider, out byte result)
            => byte.TryParse(value, styles, provider, out result);

#if NET8_0_OR_GREATER
        protected override bool TryParseSpan(ReadOnlySpan<char> value, NumberStyles styles, IFormatProvider provider, out byte result)
            => byte.TryParse(value, styles, provider, out result);
#endif
    }
}
