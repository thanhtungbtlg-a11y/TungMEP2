using System;
using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Sheets.DuplicateSheets;

public sealed class SheetDuplicateReport
{
	public int CreatedSheetCount { get; set; }

	public int FailedSheetCount { get; set; }

	public List<string> Messages { get; } = new List<string>();

	public string ToDisplayText()
	{
		if (Messages.Count == 0)
		{
			return $"Created: {CreatedSheetCount}\nFailed: {FailedSheetCount}";
		}
		return $"Created: {CreatedSheetCount}\nFailed: {FailedSheetCount}\n\n" + string.Join(Environment.NewLine, Messages);
	}
}
