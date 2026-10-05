using System;
using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Sheets.AlignViewports;

public sealed class AlignViewportsReport
{
	public int AlignedSheetCount { get; set; }

	public int SkippedSheetCount { get; set; }

	public List<string> Messages { get; } = new List<string>();

	public string ToDisplayText()
	{
		if (Messages.Count == 0)
		{
			return $"Aligned: {AlignedSheetCount}\nSkipped: {SkippedSheetCount}";
		}
		return $"Aligned: {AlignedSheetCount}\nSkipped: {SkippedSheetCount}\n\n" + string.Join(Environment.NewLine, Messages);
	}
}
