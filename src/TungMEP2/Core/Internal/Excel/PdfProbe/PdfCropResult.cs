namespace VP_RevitAPI.Internal.Excel.PdfProbe;

internal readonly record struct PdfCropResult(bool IsApplied, string Message)
{
	public static PdfCropResult Applied(string message)
	{
		return new PdfCropResult(IsApplied: true, message);
	}

	public static PdfCropResult Skipped(string message)
	{
		return new PdfCropResult(IsApplied: false, message);
	}
}
