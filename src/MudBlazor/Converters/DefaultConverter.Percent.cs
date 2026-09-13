// Copyright (c) MudBlazor 2021
// MudBlazor licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Numerics;

namespace MudBlazor;

internal partial class DefaultConverter
{
    /// <summary>
    /// Determines whether <paramref name="format"/> is the standard percent format specifier (<c>P</c> or <c>p</c> with an optional precision).
    /// </summary>
    /// <remarks>
    /// Custom formats that contain <c>%</c> are not recognized.
    /// </remarks>
    internal static bool IsPercentFormat(string? format) =>
        format is { Length: > 0 } && format[0] is 'P' or 'p' && !format.AsSpan(1).ContainsAnyExceptInRange('0', '9');

    /// <summary>
    /// Parses text produced by the percent format specifier back into the underlying fraction.
    /// </summary>
    /// <remarks>
    /// .NET has no <see cref="NumberStyles"/> flag for percent, so the symbol and the x100 scaling are handled here.
    /// The percent symbol is optional, so a bare "15" is read as 15 percent, matching what the field displays.
    /// </remarks>
    internal static bool TryParsePercent<TNumber>(string input, CultureInfo culture, out TNumber result)
        where TNumber : INumber<TNumber>
    {
        result = TNumber.Zero;
        var numberFormat = culture.NumberFormat;
        var span = input.AsSpan().Trim();
        var styles = NumberStyles.Number;

        // Several negative patterns place the sign outside the symbol ("-%12" or "12%-"), so it is peeled off before the symbol.
        var negative = false;
        if (span.StartsWith(numberFormat.NegativeSign))
        {
            negative = true;
            span = span[numberFormat.NegativeSign.Length..].TrimStart();
        }
        else if (span.EndsWith(numberFormat.NegativeSign))
        {
            negative = true;
            span = span[..^numberFormat.NegativeSign.Length].TrimEnd();
        }

        if (negative)
        {
            styles &= ~(NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign);
        }

        if (span.StartsWith(numberFormat.PercentSymbol))
        {
            span = span[numberFormat.PercentSymbol.Length..];
        }
        else if (span.EndsWith(numberFormat.PercentSymbol))
        {
            span = span[..^numberFormat.PercentSymbol.Length];
        }

        // Number parsing only reads the number separators, so a customized culture with different percent separators needs them swapped in.
        var parseFormat = numberFormat;
        if (numberFormat.PercentDecimalSeparator != numberFormat.NumberDecimalSeparator || numberFormat.PercentGroupSeparator != numberFormat.NumberGroupSeparator)
        {
            parseFormat = (NumberFormatInfo)numberFormat.Clone();
            parseFormat.NumberDecimalSeparator = numberFormat.PercentDecimalSeparator;
            parseFormat.NumberGroupSeparator = numberFormat.PercentGroupSeparator;
        }

        // Parsing through decimal keeps the division by 100 exact before converting to the target type.
        if (!decimal.TryParse(span, styles, parseFormat, out var percent))
        {
            return false;
        }

        var value = (negative ? -percent : percent) / 100m;

        // "150 %" must not silently truncate to 1 for an integral type.
        if (IsIntegralType() && !decimal.IsInteger(value))
        {
            return false;
        }

        try
        {
            result = TNumber.CreateChecked(value);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }

        // Determines whether TNumber is an integral type that cannot represent a fraction.
        // INumber<TSelf> describes values, not the type itself: it has no static member such as "IsIntegral", and INumberBase<TSelf>.IsInteger only reports whether one particular value is whole.
        // Instead, the type is asked to store one half using truncating conversion.
        // An integral type discards the fraction and yields zero, while float, double and decimal keep it.
        static bool IsIntegralType() => TNumber.IsZero(TNumber.CreateTruncating(0.5m));
    }
}
