namespace VP_RevitAPI.Internal.Excel.PdfProbe;

internal sealed record ExcelDefinedNamePdfProbeResult(bool IsSuccess, string Message, string? OutputPath, double PageWidthMm, double PageHeightMm, bool IsLandscape)
{
	public static ExcelDefinedNamePdfProbeResult Success(string outputPath, double pageWidthMm, double pageHeightMm, bool isLandscape, string message)
	{
		return new ExcelDefinedNamePdfProbeResult(IsSuccess: true, message, outputPath, pageWidthMm, pageHeightMm, isLandscape);
	}

	public static ExcelDefinedNamePdfProbeResult Fail(string message)
	{
		return new ExcelDefinedNamePdfProbeResult(IsSuccess: false, message, null, 0.0, 0.0, IsLandscape: false);
	}
}
