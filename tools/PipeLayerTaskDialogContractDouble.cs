using System;
using System.Collections.Generic;

// RevitAPIUI cannot load outside Revit on this host. This narrow double enforces
// the registration-before-default contract observed in the user's Revit 2024
// exception. It exercises the real production dialog builder, never shows UI,
// and does not claim to test native rendering or Revit model operations.
namespace Autodesk.Revit.UI;

[Flags]
public enum TaskDialogCommonButtons { None = 0, Cancel = 8 }
public enum TaskDialogCommandLinkId { CommandLink1 = 1001, CommandLink2 = 1002, CommandLink3 = 1003 }
public enum TaskDialogResult { None = 0, Cancel = 2, CommandLink1 = 1001, CommandLink2 = 1002, CommandLink3 = 1003 }

public sealed class TaskDialog
{
    public static TaskDialogResult NextResult;
    private readonly HashSet<TaskDialogResult> links = new HashSet<TaskDialogResult>();
    private readonly string title;
    private TaskDialogResult defaultButton;
    public TaskDialog(string title) { this.title = title; }
    public string MainInstruction { get; set; }
    public string MainContent { get; set; }
    public TaskDialogCommonButtons CommonButtons { get; set; }
    public TaskDialogResult DefaultButton
    {
        get => defaultButton;
        set
        {
            if (!links.Contains(value) && !(value == TaskDialogResult.Cancel && CommonButtons.HasFlag(TaskDialogCommonButtons.Cancel)))
                throw new ArgumentException("Corresponding button not found.", "defaultButton");
            defaultButton = value;
        }
    }
    public void AddCommandLink(TaskDialogCommandLinkId id, string text)
    {
        if (string.IsNullOrEmpty(text) || text.Contains("\n")) throw new ArgumentException("Invalid command link label");
        links.Add((TaskDialogResult)id);
    }
    public TaskDialogResult Show()
    {
        bool connectionPolicy = title == "PipeLayer — Kết nối";
        if (links.Count != (connectionPolicy ? 2 : 3) || !links.Contains(TaskDialogResult.CommandLink1) ||
            !links.Contains(TaskDialogResult.CommandLink2) || (!connectionPolicy && !links.Contains(TaskDialogResult.CommandLink3)))
            throw new InvalidOperationException("All policy/datum choices must be available before showing the dialog.");
        if (DefaultButton != TaskDialogResult.CommandLink1 || !CommonButtons.HasFlag(TaskDialogCommonButtons.Cancel))
            throw new InvalidOperationException("BOP default and safe cancellation must remain available.");
        return NextResult;
    }
}
