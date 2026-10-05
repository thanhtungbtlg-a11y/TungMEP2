using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Sheets;

public sealed class PdfRenameSummary
{
	public int RenamedCount { get; }

	public int SkippedCount { get; }

	public IReadOnlyList<string> Errors { get; }

	public PdfRenameSummary(int renamedCount, int skippedCount, IReadOnlyList<string> errors)
	{
		RenamedCount = renamedCount;
		SkippedCount = skippedCount;
		Errors = errors;
	}
}
