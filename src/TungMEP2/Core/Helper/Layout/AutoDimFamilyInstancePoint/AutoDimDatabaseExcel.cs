using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using OfficeOpenXml;

namespace VP_RevitAPI.Helper.Layout.AutoDimFamilyInstancePoint;

internal sealed class AutoDimDatabaseExcel
{
	private const string FileName = "AutoDIMDataBase.xlsx";

	private const string SheetRvl = "RVL";

	private const string SheetDatabase = "DataBase";

	private const double DefaultDistanceMillimeters = 2000.0;

	private readonly string _filePath;

	public string FilePath => _filePath;

	public AutoDimDatabaseExcel()
	{
		_filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AutoDIMDataBase.xlsx");
	}

	public void Write(ElementId arcLinkId, ElementId strLinkId, IReadOnlyList<ElementId> wallIds, IReadOnlyList<ElementId> columnIds, IReadOnlyList<ElementId> framingIds, double distanceMillimeters = 2000.0)
	{
		ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
		if (File.Exists(_filePath))
		{
			File.Delete(_filePath);
		}
		using ExcelPackage package = new ExcelPackage();
		ExcelWorksheet rvlSheet = package.Workbook.Worksheets.Add("RVL");
		ExcelWorksheet dataSheet = package.Workbook.Worksheets.Add("DataBase");
		rvlSheet.Cells[1, 1].Value = "ARCLink";
		rvlSheet.Cells[1, 2].Value = arcLinkId.Value.ToString();
		rvlSheet.Cells[2, 1].Value = "STRLink";
		rvlSheet.Cells[2, 2].Value = strLinkId.Value.ToString();
		rvlSheet.Cells[3, 1].Value = "Distance";
		rvlSheet.Cells[3, 2].Value = distanceMillimeters;
		dataSheet.Cells[1, 1].Value = "Wall";
		for (int index = 0; index < wallIds.Count; index++)
		{
			dataSheet.Cells[index + 2, 1].Value = wallIds[index].Value.ToString();
		}
		dataSheet.Cells[1, 2].Value = "Column";
		for (int i = 0; i < columnIds.Count; i++)
		{
			dataSheet.Cells[i + 2, 2].Value = columnIds[i].Value.ToString();
		}
		dataSheet.Cells[1, 3].Value = "Framing";
		for (int j = 0; j < framingIds.Count; j++)
		{
			dataSheet.Cells[j + 2, 3].Value = framingIds[j].Value.ToString();
		}
		package.SaveAs(new FileInfo(_filePath));
	}

	public bool Exists()
	{
		return File.Exists(_filePath);
	}

	public bool TryRead(Document hostDocument, out AutoDimDatabaseSnapshot? snapshot, out string error)
	{
		snapshot = null;
		error = string.Empty;
		if (!Exists())
		{
			error = "Không tìm thấy file AutoDIMDataBase.xlsx tại Desktop.";
			return false;
		}
		ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
		try
		{
			using ExcelPackage package = new ExcelPackage(new FileInfo(_filePath));
			ExcelWorksheet rvlSheet = package.Workbook.Worksheets["RVL"];
			ExcelWorksheet dataSheet = package.Workbook.Worksheets["DataBase"];
			if (rvlSheet == null || dataSheet == null)
			{
				error = "File Excel thiếu sheet RVL hoặc DataBase.";
				return false;
			}
			long? arcId = ReadLong(rvlSheet.Cells[1, 2].Value) ?? ReadLong(rvlSheet.Cells[2, 1].Value);
			long? strId = ReadLong(rvlSheet.Cells[2, 2].Value) ?? ReadLong(rvlSheet.Cells[1, 3].Value);
			if (!arcId.HasValue || !strId.HasValue)
			{
				error = "Không đọc được ARCLink/STRLink từ sheet RVL.";
				return false;
			}
			RevitLinkInstance arcLink = hostDocument.GetElement(new ElementId(arcId.Value)) as RevitLinkInstance;
			RevitLinkInstance strLink = hostDocument.GetElement(new ElementId(strId.Value)) as RevitLinkInstance;
			if (arcLink == null || strLink == null)
			{
				error = "ARCLink hoặc STRLink không còn tồn tại trong model hiện tại.";
				return false;
			}
			Document arcLinkDocument = arcLink.GetLinkDocument();
			Document strLinkDocument = strLink.GetLinkDocument();
			if (arcLinkDocument == null || strLinkDocument == null)
			{
				error = "Không truy cập được Link Document của ARC hoặc STR.";
				return false;
			}
			List<Wall> walls = ReadElements<Wall>(dataSheet, 1, arcLinkDocument);
			List<FamilyInstance> columns = ReadElements<FamilyInstance>(dataSheet, 2, strLinkDocument);
			List<FamilyInstance> framings = ReadElements<FamilyInstance>(dataSheet, 3, strLinkDocument);
			double distanceMillimeters = ReadDouble(rvlSheet.Cells[3, 2].Value) ?? 2000.0;
			snapshot = new AutoDimDatabaseSnapshot(arcLink, strLink, arcLink.GetTransform(), strLink.GetTransform(), walls, columns, framings, distanceMillimeters);
			return true;
		}
		catch (Exception ex)
		{
			error = "Lỗi đọc file Excel: " + ex.Message;
			return false;
		}
	}

	private static List<TElement> ReadElements<TElement>(ExcelWorksheet worksheet, int column, Document linkDocument) where TElement : Element
	{
		int lastRow = worksheet.Dimension?.End.Row ?? 0;
		List<TElement> elements = new List<TElement>();
		for (int row = 2; row <= lastRow; row++)
		{
			long? idValue = ReadLong(worksheet.Cells[row, column].Value);
			if (idValue.HasValue)
			{
				Element element = linkDocument.GetElement(new ElementId(idValue.Value));
				if (element is TElement typed)
				{
					elements.Add(typed);
				}
			}
		}
		return elements;
	}

	private static long? ReadLong(object? value)
	{
		if (value == null)
		{
			return null;
		}
		if (long.TryParse(value.ToString(), out var parsed))
		{
			return parsed;
		}
		return null;
	}

	private static double? ReadDouble(object? value)
	{
		if (value == null)
		{
			return null;
		}
		if (double.TryParse(value.ToString(), out var parsed))
		{
			return parsed;
		}
		return null;
	}
}
