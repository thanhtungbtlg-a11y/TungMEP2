using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets;

public sealed class PdfQualityOption
{
	public string Name { get; }

	public int Dpi { get; }

	public PDFExportQualityType ExportQuality { get; }

	public bool IsDefault { get; }

	public PdfQualityOption(string name, int dpi, PDFExportQualityType exportQuality, bool isDefault)
	{
		Name = name;
		Dpi = dpi;
		ExportQuality = exportQuality;
		IsDefault = isDefault;
	}
}
