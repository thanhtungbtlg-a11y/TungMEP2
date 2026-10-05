using Microsoft.Office.Interop.Excel;
using VP_RevitAPI.Helper.Excel.Func;

namespace VP_RevitAPI.Helper.Excel;

internal static class XpsRangeImageRenderer
{
	public static bool TryRenderJpeg(Worksheet worksheet, Range range, string outputPath, out string error)
	{
		return ExcelRenderFunc.RenderRangeToJpegByXps(worksheet, range, outputPath, out error);
	}
}
