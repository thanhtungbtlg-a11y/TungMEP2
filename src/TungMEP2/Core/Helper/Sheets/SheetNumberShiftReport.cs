using System;
using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Sheets;

public sealed class SheetNumberShiftReport
{
	public int Delta { get; set; }

	public int TotalCount { get; set; }

	public int SuccessCount { get; set; }

	public int SkippedCount { get; set; }

	public int FailedCount { get; set; }

	public List<string> Messages { get; } = new List<string>();

	public string ToDisplayText()
	{
		string operation = ((Delta > 0) ? "Increment" : "Decrement");
		return $"Operation: {operation}\nTotal: {TotalCount}\nSuccess: {SuccessCount}\nSkipped: {SkippedCount}\nFailed: {FailedCount}" + ((Messages.Count == 0) ? string.Empty : ("\n\n" + string.Join(Environment.NewLine, Messages)));
	}
}
