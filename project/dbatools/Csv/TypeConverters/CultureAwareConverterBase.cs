using System;
using System.Globalization;

namespace Dataplat.Dbatools.Csv.TypeConverters
{
    /// <summary>
    /// Base class for type converters that support culture-aware numeric parsing.
    /// Provides common functionality for NumberStyles and FormatProvider handling.
    /// </summary>
    /// <typeparam name="T">The target numeric type for conversion.</typeparam>
    public abstract class CultureAwareConverterBase<T> : TypeConverterBase<T>, ICultureAwareConverter
        where T : struct
    {
        /// <summary>
        /// Gets or sets the number styles to use for parsing.
        /// </summary>
        public NumberStyles NumberStyles { get; set; }

        /// <summary>
        /// Gets or sets the format provider to use for parsing.
        /// Defaults to InvariantCulture.
        /// </summary>
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        /// <summary>
        /// Attempts to convert the string value to the target type.
        /// </summary>
        public override bool TryConvert(string value, out T result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = default;
                return false;
            }
            return TryParseCore(value.Trim(), NumberStyles, FormatProvider, out result);
        }

        /// <summary>
        /// Attempts to convert the string value using the specified culture.
        /// </summary>
        public bool TryConvert(string value, CultureInfo culture, out object result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = default(T);
                return false;
            }
            if (TryParseCore(value.Trim(), NumberStyles, culture ?? FormatProvider, out T parsed))
            {
                result = parsed;
                return true;
            }
            result = default(T);
            return false;
        }

        /// <summary>
        /// Core parsing implementation. Override this in derived classes to provide
        /// type-specific parsing using the appropriate TryParse method.
        /// </summary>
        /// <param name="value">The trimmed string value to parse.</param>
        /// <param name="styles">The number styles to use.</param>
        /// <param name="provider">The format provider to use.</param>
        /// <param name="result">The parsed result if successful.</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        protected abstract bool TryParseCore(string value, NumberStyles styles, IFormatProvider provider, out T result);
    }
}
