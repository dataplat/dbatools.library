using System;
using System.Globalization;

namespace Dataplat.Dbatools.Csv.TypeConverters
{
    /// <summary>
    /// Converts string values to DateTime values with customizable format support.
    /// Addresses issue #9694: Import-DbaCsv date format specification.
    /// </summary>
    public sealed class DateTimeConverter : TypeConverterBase<DateTime>
    {
        /// <summary>
        /// Gets the default instance using invariant culture and standard formats.
        /// </summary>
        public static DateTimeConverter Default { get; } = new DateTimeConverter();

        /// <summary>
        /// Gets or sets custom date/time formats to try when parsing.
        /// These are tried before standard formats.
        /// </summary>
        public string[] CustomFormats { get; set; }

        /// <summary>
        /// Gets or sets the culture to use for parsing. Defaults to InvariantCulture.
        /// </summary>
        public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

        /// <summary>
        /// Gets or sets the date time styles to use for parsing.
        /// </summary>
        public DateTimeStyles Styles { get; set; } = DateTimeStyles.AllowWhiteSpaces;

        /// <summary>
        /// Gets or sets whether to assume UTC when no timezone is specified.
        /// </summary>
        public bool AssumeUtc { get; set; }

        // Common date formats that users might encounter
        private static readonly string[] StandardFormats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy",
            "dd-MM-yyyy HH:mm:ss",
            "dd-MM-yyyy",
            "M/d/yyyy HH:mm:ss",
            "M/d/yyyy",
            "d/M/yyyy HH:mm:ss",
            "d/M/yyyy",
            "yyyyMMdd",
            "yyyyMMddHHmmss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "o", // Round-trip format
            "r", // RFC1123
            "u", // Universal sortable
        };

        /// <summary>
        /// Attempts to convert the string value to a DateTime.
        /// </summary>
        public override bool TryConvert(string value, out DateTime result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result = default(DateTime);
                return false;
            }

            string trimmed = value.Trim();
            DateTimeStyles styles = Styles;
            if (AssumeUtc)
            {
                styles |= DateTimeStyles.AssumeUniversal;
            }

            // Try custom formats first
            if (CustomFormats != null && CustomFormats.Length > 0)
            {
                if (DateTime.TryParseExact(trimmed, CustomFormats, Culture, styles, out result))
                {
                    return true;
                }
            }

            // Try standard formats
            if (DateTime.TryParseExact(trimmed, StandardFormats, Culture, styles, out result))
            {
                return true;
            }

            // Fall back to general parsing
            return DateTime.TryParse(trimmed, Culture, styles, out result);
        }

        /// <summary>
        /// Creates a converter configured for a specific date format.
        /// </summary>
        /// <param name="formats">The date format strings to use.</param>
        /// <param name="culture">The culture to use (null for InvariantCulture).</param>
        /// <returns>A configured DateTimeConverter.</returns>
        public static DateTimeConverter WithFormats(string[] formats, CultureInfo culture = null)
        {
            return new DateTimeConverter
            {
                CustomFormats = formats,
                Culture = culture ?? CultureInfo.InvariantCulture
            };
        }

        /// <summary>
        /// Creates a converter for the day-month-year format (European style).
        /// Addresses Oracle date format issues mentioned in #9694.
        /// </summary>
        public static DateTimeConverter DayMonthYear { get; } = new DateTimeConverter
        {
            CustomFormats = new[]
            {
                "dd-MM-yyyy HH:mm:ss",
                "dd-MM-yyyy",
                "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yyyy",
                "d-M-yyyy HH:mm:ss",
                "d-M-yyyy"
            }
        };

        /// <summary>
        /// Creates a converter for the month-day-year format (US style).
        /// </summary>
        public static DateTimeConverter MonthDayYear { get; } = new DateTimeConverter
        {
            CustomFormats = new[]
            {
                "MM-dd-yyyy HH:mm:ss",
                "MM-dd-yyyy",
                "MM/dd/yyyy HH:mm:ss",
                "MM/dd/yyyy",
                "M-d-yyyy HH:mm:ss",
                "M-d-yyyy"
            }
        };
    }
}
