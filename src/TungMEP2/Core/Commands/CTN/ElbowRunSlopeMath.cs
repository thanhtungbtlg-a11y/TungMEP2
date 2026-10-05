using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VP_RevitAPI.Commands.CTN
{

internal enum PipeSlopeMode
{
    Unknown,
    Off,
    Up,
    Down
}

internal static class ElbowRunSlopeMath
{
    private const double Epsilon = 1e-12;

    public static bool TryParseRatio(string text, out double ratio)
    {
        ratio = 0.0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();
        Match fraction = Regex.Match(
            value,
            @"^\s*([+-]?[0-9]+(?:[\.,][0-9]+)?)\s*[:/]\s*([0-9]+(?:[\.,][0-9]+)?)\s*$");
        if (fraction.Success)
        {
            double numerator;
            double denominator;
            if (!TryParseFlexibleNumber(fraction.Groups[1].Value, out numerator) ||
                !TryParseFlexibleNumber(fraction.Groups[2].Value, out denominator) ||
                Math.Abs(denominator) <= Epsilon)
            {
                return false;
            }
            ratio = Math.Abs(numerator / denominator);
            return true;
        }

        bool degrees = value.IndexOf("degree", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("deg", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("°", StringComparison.Ordinal) >= 0;
        Match number = Regex.Match(value, @"[+-]?[0-9]+(?:[\.,][0-9]+)?");
        double parsed;
        if (!number.Success ||
            !TryParseFlexibleNumber(number.Value, out parsed))
        {
            return false;
        }

        ratio = degrees
            ? Math.Abs(Math.Tan(parsed * Math.PI / 180.0))
            : Math.Abs(parsed) / 100.0;
        return !double.IsNaN(ratio) && !double.IsInfinity(ratio);
    }

    public static double ResolveSignedRatio(
        double ribbonRatio,
        PipeSlopeMode mode,
        double existingSignedRatio)
    {
        double magnitude = Math.Abs(ribbonRatio);
        if (mode == PipeSlopeMode.Up)
        {
            return magnitude;
        }
        if (mode == PipeSlopeMode.Down)
        {
            return -magnitude;
        }
        if (mode == PipeSlopeMode.Off)
        {
            return 0.0;
        }
        return existingSignedRatio;
    }

    public static double CalculateRise(double horizontalLength, double signedRatio)
    {
        return horizontalLength * signedRatio;
    }

    private static bool TryParseFlexibleNumber(string text, out double value)
    {
        string normalized = text.Trim();
        int comma = normalized.LastIndexOf(',');
        int dot = normalized.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            if (comma > dot)
            {
                normalized = normalized.Replace(".", string.Empty)
                    .Replace(',', '.');
            }
            else
            {
                normalized = normalized.Replace(",", string.Empty);
            }
        }
        else if (comma >= 0)
        {
            normalized = normalized.Replace(',', '.');
        }
        return double.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }
}
}
