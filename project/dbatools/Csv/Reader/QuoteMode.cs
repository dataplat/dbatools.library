namespace Dataplat.Dbatools.Csv.Reader
{
    /// <summary>
    /// Specifies how to handle quoting in CSV fields.
    /// </summary>
    public enum QuoteMode
    {
        /// <summary>
        /// Strict RFC 4180 compliant parsing.
        /// Quotes must properly enclose fields and be escaped by doubling.
        /// </summary>
        Strict,

        /// <summary>
        /// Lenient parsing that handles common real-world malformed data.
        /// A quote only starts a quoted field if it's at the field start AND has a matching closing quote.
        /// Unmatched quotes are treated as literal characters.
        /// Also handles non-RFC escaped quotes like \"
        /// </summary>
        Lenient
    }
}
