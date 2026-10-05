using Microsoft.Office.Interop.Excel;

namespace VP_RevitAPI.Helper.Excel;

internal static class NamedRangeImageRenderer
{
	public static bool TryRenderJpeg(Worksheet worksheet, Range range, string outputPath, out string error)
	{
		return XpsRangeImageRenderer.TryRenderJpeg(worksheet, range, outputPath, out error);
	}
}
