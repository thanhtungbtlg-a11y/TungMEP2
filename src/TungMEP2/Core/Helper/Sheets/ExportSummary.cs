using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Sheets;

public sealed class ExportSummary
{
	public int PdfCount { get; }

	public int DwgCount { get; }

	public IReadOnlyList<string> Errors { get; }

	public bool WasCancelled { get; }

	public ExportSummary(int pdfCount, int dwgCount, IReadOnlyList<string> errors, bool wasCancelled = false)
	{
		PdfCount = pdfCount;
		DwgCount = dwgCount;
		Errors = errors;
		WasCancelled = wasCancelled;
	}
}
