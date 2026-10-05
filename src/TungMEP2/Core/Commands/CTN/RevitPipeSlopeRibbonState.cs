using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Revit.UI.Events;

namespace VP_RevitAPI.Commands.CTN;

internal sealed class PipeSlopeRibbonSelection
{
    public PipeSlopeRibbonSelection(double ratio, PipeSlopeMode mode)
    {
        Ratio = Math.Abs(ratio);
        Mode = mode;
    }

    public double Ratio { get; }

    public PipeSlopeMode Mode { get; }
}

internal static class RevitPipeSlopeRibbonState
{
    private static readonly object SyncRoot = new object();
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromMilliseconds(350.0);
    private static DateTime _lastCaptureAttemptUtc = DateTime.MinValue;
    private static PipeSlopeRibbonSelection? _lastSelection;

    public static void OnIdling(object sender, IdlingEventArgs args)
    {
        CaptureCurrent();
    }

    public static bool TryGetCurrentOrLast(out PipeSlopeRibbonSelection selection)
    {
        CaptureCurrent(force: true);
        lock (SyncRoot)
        {
            if (_lastSelection != null)
            {
                selection = new PipeSlopeRibbonSelection(
                    _lastSelection.Ratio,
                    _lastSelection.Mode);
                return true;
            }
        }
        selection = null!;
        return false;
    }

    private static void CaptureCurrent(bool force = false)
    {
        DateTime now = DateTime.UtcNow;
        if (!force && now - _lastCaptureAttemptUtc < CaptureInterval)
        {
            return;
        }
        _lastCaptureAttemptUtc = now;
        try
        {
            if (TryReadLive(out PipeSlopeRibbonSelection live))
            {
                lock (SyncRoot)
                {
                    _lastSelection = live;
                }
            }
        }
        catch
        {
            // Revit creates and removes contextual tabs while commands change.
            // A transient ribbon failure must never prevent the MEP command.
        }
    }

    private static bool TryReadLive(out PipeSlopeRibbonSelection selection)
    {
        selection = null!;
        object? ribbon = GetMainRibbon();
        if (ribbon == null)
        {
            return false;
        }

        foreach (object panelSource in FindPipeSlopePanelSources(ribbon))
        {
            List<object> items = EnumerateRibbonItems(panelSource).ToList();
            PipeSlopeMode mode = ResolveMode(items);
            if (mode == PipeSlopeMode.Unknown)
            {
                continue;
            }

            double ratio = 0.0;
            if (mode != PipeSlopeMode.Off && !TryReadRatio(items, out ratio))
            {
                lock (SyncRoot)
                {
                    if (_lastSelection == null)
                    {
                        continue;
                    }
                    ratio = _lastSelection.Ratio;
                }
            }
            selection = new PipeSlopeRibbonSelection(ratio, mode);
            return true;
        }
        return false;
    }

    private static object? GetMainRibbon()
    {
        Assembly? adWindows = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    "AdWindows",
                    StringComparison.OrdinalIgnoreCase));
        if (adWindows == null)
        {
            return null;
        }
        Type? componentManager = adWindows.GetType(
            "Autodesk.Windows.ComponentManager",
            throwOnError: false);
        return componentManager?
            .GetProperty("Ribbon", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null, null);
    }

    private static IEnumerable<object> FindPipeSlopePanelSources(object ribbon)
    {
        foreach (object tab in EnumerateProperty(ribbon, "Tabs"))
        {
            foreach (object panel in EnumerateProperty(tab, "Panels"))
            {
                object? source = GetProperty(panel, "Source");
                if (source == null)
                {
                    continue;
                }
                string identity = GetIdentity(source) + " " + GetIdentity(panel);
                string normalized = Normalize(identity);
                if (normalized.Contains("pipeslopepanel") ||
                    normalized.Contains("pipeslopes") ||
                    normalized.Contains("rbsroutepathslope") ||
                    (normalized.Contains("pipe") && normalized.Contains("slope")))
                {
                    yield return source;
                }
            }
        }
    }

    private static IEnumerable<object> EnumerateRibbonItems(object panelSource)
    {
        var pending = new Stack<object>();
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        foreach (object item in EnumerateProperty(panelSource, "Items"))
        {
            pending.Push(item);
        }
        while (pending.Count > 0)
        {
            object item = pending.Pop();
            if (!visited.Add(item))
            {
                continue;
            }
            yield return item;
            foreach (object child in EnumerateProperty(item, "Items"))
            {
                pending.Push(child);
            }
            object? source = GetProperty(item, "Source");
            if (source != null)
            {
                foreach (object child in EnumerateProperty(source, "Items"))
                {
                    pending.Push(child);
                }
            }
            object? current = GetProperty(item, "Current");
            if (current != null && !ReferenceEquals(current, item))
            {
                pending.Push(current);
            }
        }
    }

    private static PipeSlopeMode ResolveMode(IEnumerable<object> items)
    {
        foreach (object item in items)
        {
            if (!IsSelected(item))
            {
                continue;
            }
            string identity = Normalize(GetIdentity(item));
            if (identity.Contains("slopedown"))
            {
                return PipeSlopeMode.Down;
            }
            if (identity.Contains("slopeup"))
            {
                return PipeSlopeMode.Up;
            }
            if (identity.Contains("slopeoff") ||
                identity.Contains("noslope"))
            {
                return PipeSlopeMode.Off;
            }
        }
        return PipeSlopeMode.Unknown;
    }

    private static bool TryReadRatio(IEnumerable<object> items, out double ratio)
    {
        foreach (object item in items)
        {
            foreach (string propertyName in new[]
            {
                "EditableText",
                "TextValue",
                "Value",
                "Text"
            })
            {
                object? raw = GetProperty(item, propertyName);
                if (raw != null &&
                    ElbowRunSlopeMath.TryParseRatio(raw.ToString() ?? string.Empty, out ratio))
                {
                    return true;
                }
            }
            object? current = GetProperty(item, "Current");
            if (current == null)
            {
                continue;
            }
            foreach (string propertyName in new[] { "Text", "Name", "Value" })
            {
                object? raw = GetProperty(current, propertyName);
                if (raw != null &&
                    ElbowRunSlopeMath.TryParseRatio(raw.ToString() ?? string.Empty, out ratio))
                {
                    return true;
                }
            }
        }
        ratio = 0.0;
        return false;
    }

    private static bool IsSelected(object item)
    {
        foreach (string propertyName in new[]
        {
            "IsChecked",
            "CheckState",
            "IsCheckedOverride",
            "IsActive"
        })
        {
            object? raw = GetProperty(item, propertyName);
            if (raw is bool value && value)
            {
                return true;
            }
        }
        return false;
    }

    private static string GetIdentity(object item)
    {
        var values = new List<string>();
        foreach (string propertyName in new[]
        {
            "Id",
            "AutomationName",
            "Text",
            "Name",
            "Description",
            "GroupName",
            "CommandParameter"
        })
        {
            object? raw = GetProperty(item, propertyName);
            if (raw != null)
            {
                values.Add(raw.ToString() ?? string.Empty);
            }
        }
        return string.Join(" ", values);
    }

    private static string Normalize(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static IEnumerable<object> EnumerateProperty(object instance, string name)
    {
        object? value = GetProperty(instance, name);
        if (value is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private static object? GetProperty(object instance, string name)
    {
        try
        {
            return instance.GetType()
                .GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(instance, null);
        }
        catch
        {
            return null;
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object? first, object? second)
        {
            return ReferenceEquals(first, second);
        }

        public int GetHashCode(object value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
