#nullable enable

using System;
using System.Globalization;
using System.Management.Automation;
using System.Numerics;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// PSObject dynamic property access with PS non-strict semantics: a missing property reads
/// as null, DataRow columns / class fields / ETS note properties are all reachable the same
/// way the PS dot operator reached them.
/// </summary>
internal static class PsProperty
{
    /// <summary>Reads a property (adapted, class, or note) off any object; null when absent.</summary>
    internal static object? Get(object? item, string name)
    {
        if (item is null)
            return null;
        PSObject wrapped = PSObject.AsPSObject(item);
        PSPropertyInfo? property = wrapped.Properties[name];
        if (property is null)
            return null;
        object? value = property.Value;
        if (value is PSObject psValue)
            value = psValue.BaseObject;
        if (value is DBNull)
            return null;
        return value;
    }

    /// <summary>Whether the object exposes the named property ("X" -in $obj.PSObject.Properties.Name).</summary>
    internal static bool Has(object? item, string name)
    {
        if (item is null)
            return false;
        return PSObject.AsPSObject(item).Properties[name] is not null;
    }

    /// <summary>Sets an existing property's value (class field, note property, or DataRow column).</summary>
    internal static void Set(object item, string name, object? value)
    {
        PSObject wrapped = PSObject.AsPSObject(item);
        PSPropertyInfo? property = wrapped.Properties[name];
        if (property is not null)
            property.Value = value;
    }

    /// <summary>Add-Member -MemberType NoteProperty parity (only when not already present).</summary>
    internal static void AddNote(object item, string name, object? value)
    {
        PSObject wrapped = PSObject.AsPSObject(item);
        if (wrapped.Properties[name] is null)
            wrapped.Properties.Add(new PSNoteProperty(name, value));
    }
}

/// <summary>LSN readings, replicating the PS [BigInt]$value.ToString() cast chain.</summary>
internal static class PsLsn
{
    /// <summary>Converts an LSN-carrying value (decimal, BigInteger, string, DBNull) to BigInteger.</summary>
    internal static BigInteger ToBigInt(object? value)
    {
        if (value is PSObject psValue)
            value = psValue.BaseObject;
        if (value is null || value is DBNull)
            return BigInteger.Zero;
        if (value is BigInteger big)
            return big;
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        if (text.Length == 0)
            return BigInteger.Zero;
        // Decimal-typed LSNs can stringify with a trailing ".0"-less integral form already;
        // BigInteger.Parse on the integral text matches [BigInt]"..." exactly.
        return BigInteger.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}

/// <summary>PS string-rendering helpers for message parity.</summary>
internal static class PsBool
{
    /// <summary>PS renders booleans as True/False inside expandable strings.</summary>
    internal static string Text(bool value)
    {
        return value ? "True" : "False";
    }
}

/// <summary>PS comparison-operator parity (case-insensitive -eq / -in).</summary>
internal static class PsString
{
    /// <summary>The PS -eq operator for strings: case-insensitive, invariant culture.</summary>
    internal static bool Eq(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The PS -in operator over a literal string list: case-insensitive.</summary>
    internal static bool In(string? value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

/// <summary>
/// PS operator semantics over arbitrary values, backed by the engine's own
/// LanguagePrimitives so type-coercion rules (left operand wins, case-insensitive strings,
/// null conversion) match the retired PS source exactly.
/// </summary>
internal static class PsOps
{
    /// <summary>The PS -eq operator (case-insensitive, lhs-type-driven conversion).</summary>
    internal static bool Eq(object? left, object? right)
    {
        return LanguagePrimitives.Equals(Unwrap(left), Unwrap(right), true);
    }

    /// <summary>The PS comparison engine (-lt/-le/-gt/-ge): negative, zero, positive.</summary>
    internal static int Compare(object? left, object? right)
    {
        return LanguagePrimitives.Compare(Unwrap(left), Unwrap(right), true);
    }

    /// <summary>
    /// The PS -in operator over any collection. $value -in $collection is evaluated as
    /// "any element -eq $value" with the ELEMENT as the left operand — so a single-element
    /// array value coerces to the element's type and can match (verified against the
    /// Select-DbaBackupInformation Continue Points behavior).
    /// </summary>
    internal static bool In(object? value, object? collection)
    {
        if (Unwrap(collection) is not System.Collections.IEnumerable items || collection is string)
        {
            return Eq(collection, value);
        }
        foreach (object? item in items)
        {
            if (Eq(item, value))
                return true;
        }
        return false;
    }

    /// <summary>PS truthiness (if ($x) { ... }).</summary>
    internal static bool IsTrue(object? value)
    {
        return LanguagePrimitives.IsTrue(value);
    }

    private static object? Unwrap(object? value)
    {
        if (value is PSObject psValue)
            return psValue.BaseObject;
        return value;
    }
}
