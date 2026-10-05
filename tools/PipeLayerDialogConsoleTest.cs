using System;
using Autodesk.Revit.UI;
using VP_RevitAPI.Internal.Ctn;

internal static class PipeLayerDialogConsoleTest
{
    private static int assertions;
    private static void Main()
    {
        // Catches a premature DefaultButton setter before its BOP command link exists.
        Choose(TaskDialogResult.CommandLink1, true, PipeLayerMode.Bop);
        Choose(TaskDialogResult.CommandLink2, true, PipeLayerMode.Cop);
        Choose(TaskDialogResult.CommandLink3, true, PipeLayerMode.Top);
        Choose(TaskDialogResult.Cancel, false, PipeLayerMode.Bop);
        Choose(TaskDialogResult.None, false, PipeLayerMode.Bop);
        ConnectionChoice(TaskDialogResult.CommandLink1, true, false);
        ConnectionChoice(TaskDialogResult.CommandLink2, true, true);
        ConnectionChoice(TaskDialogResult.Cancel, false, false);
        ConnectionChoice(TaskDialogResult.None, false, false);
        ConnectionChoice(TaskDialogResult.CommandLink3, false, false);
        Console.WriteLine($"PipeLayer dialog contract: {assertions} assertions passed.");
    }
    private static void Choose(TaskDialogResult response, bool expectedAccepted, PipeLayerMode expectedMode)
    {
        TaskDialog.NextResult = response;
        bool accepted = PipeLayerModeDialog.TryChooseMode(out PipeLayerMode mode);
        Check(accepted == expectedAccepted, "Selection/cancellation result for " + response);
        Check(mode == expectedMode, "Datum mode for " + response);
    }
    private static void ConnectionChoice(TaskDialogResult response, bool accepted, bool allowOpen)
    {
        TaskDialog.NextResult = response;
        Check(PipeLayerModeDialog.TryChooseConnectionPolicy(out bool actualOpen) == accepted, "Connection policy result for " + response);
        Check(actualOpen == allowOpen, "Explicit allow-open mapping for " + response);
    }
    private static void Check(bool value, string label)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(label);
    }
}
