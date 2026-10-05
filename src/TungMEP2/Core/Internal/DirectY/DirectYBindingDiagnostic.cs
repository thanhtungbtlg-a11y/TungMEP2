using System;
using System.Globalization;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

// Measurement-only diagnostic payload; this does not approve or repair any binding.
internal static class DirectYBindingDiagnostic
{
    internal static void Observe(Action measurement, Func<Exception, bool> mustAbort, Action<Exception> unavailable)
    {
        try { measurement(); }
        catch (Exception error) when (!mustAbort(error)) { unavailable(error); }
    }

    internal static string Format(long firstOwner, int firstConnector, long secondOwner, int secondConnector,
        bool forward, bool reverse, double? gapFeet, double? snapshotGapFeet, double? axisDot, double toleranceFeet)
        => string.Format(CultureInfo.InvariantCulture,
            "first={0}:{1}; second={2}:{3}; forward={4}; reverse={5}; gapMm={6}; snapshotGapMm={7}; toleranceMm={8}; axisDot={9}",
            firstOwner, firstConnector, secondOwner, secondConnector, forward, reverse,
            Number(gapFeet, 304.8, "F6"), Number(snapshotGapFeet, 304.8, "F6"),
            Number(toleranceFeet, 304.8, "F6"), Number(axisDot, 1, "F9"));

    private static string Number(double? value, double scale, string format)
    {
        double scaled = value.GetValueOrDefault() * scale;
        return value.HasValue && !double.IsNaN(scaled) && !double.IsInfinity(scaled)
            ? scaled.ToString(format, CultureInfo.InvariantCulture) : "unavailable";
    }
}
