using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VP_RevitAPI.Internal.Excel.PdfProbe;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelDefinedNamePdfExporter
{
	private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm", ".xls" };

	public static ExcelDefinedNameExportSummary Export(string excelPath, IReadOnlyCollection<ExcelDefinedNameExportTarget>? targets)
	{
		DateTime startedAtUtc = DateTime.UtcNow;
		List<ExcelDefinedNameExportItemResult> items = new List<ExcelDefinedNameExportItemResult>();
		int total = 0;
		int exported = 0;
		int failed = 0;
		if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
		{
			failed++;
			items.Add(new ExcelDefinedNameExportItemResult(string.Empty, string.Empty, null, IsExported: false, ExcelDefinedNameExportReasonCode.InvalidPath, "Input path is invalid or file does not exist."));
			return BuildSummary(excelPath, startedAtUtc, total, exported, failed, items);
		}
		string extension = Path.GetExtension(excelPath);
		if (!SupportedExtensions.Contains(extension))
		{
			failed++;
			items.Add(new ExcelDefinedNameExportItemResult(string.Empty, string.Empty, null, IsExported: false, ExcelDefinedNameExportReasonCode.UnsupportedExtension, "Unsupported extension: " + extension));
			return BuildSummary(excelPath, startedAtUtc, total, exported, failed, items);
		}
		IReadOnlyCollection<ExcelDefinedNameExportTarget> normalizedTargets = NormalizeTargets(targets);
		if (normalizedTargets == null || normalizedTargets.Count == 0)
		{
			failed++;
			items.Add(new ExcelDefinedNameExportItemResult(string.Empty, string.Empty, null, IsExported: false, ExcelDefinedNameExportReasonCode.ResolveFailed, "No scoped defined name target is provided."));
			return BuildSummary(excelPath, startedAtUtc, total, exported, failed, items);
		}
		foreach (ExcelDefinedNameExportTarget target in normalizedTargets)
		{
			total++;
			ExcelDefinedNamePdfProbeResult result = ExcelDefinedNamePdfProbeService.ExportSingle(excelPath, target.SheetName, target.DefinedName);
			if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.OutputPath))
			{
				exported++;
				items.Add(new ExcelDefinedNameExportItemResult(target.SheetName, target.DefinedName, result.OutputPath, IsExported: true, null, result.Message));
			}
			else
			{
				failed++;
				items.Add(new ExcelDefinedNameExportItemResult(target.SheetName, target.DefinedName, null, IsExported: false, ExcelDefinedNameExportReasonCode.RenderFailed, result.Message));
			}
		}
		return BuildSummary(excelPath, startedAtUtc, total, exported, failed, items);
	}

	private static ExcelDefinedNameExportSummary BuildSummary(string excelPath, DateTime startedAtUtc, int total, int exported, int failed, IReadOnlyList<ExcelDefinedNameExportItemResult> items)
	{
		return new ExcelDefinedNameExportSummary(excelPath, "PdfLinkProbe", startedAtUtc, DateTime.UtcNow, total, exported, Math.Max(0, total - exported - failed), failed, items.ToList());
	}

	private static IReadOnlyCollection<ExcelDefinedNameExportTarget>? NormalizeTargets(IReadOnlyCollection<ExcelDefinedNameExportTarget>? targets)
	{
		if (targets == null || targets.Count == 0)
		{
			return null;
		}
		return (from item in targets
			where !string.IsNullOrWhiteSpace(item.SheetName) && !string.IsNullOrWhiteSpace(item.DefinedName)
			select new ExcelDefinedNameExportTarget(item.SheetName.Trim(), item.DefinedName.Trim())).Distinct().ToList();
	}
}
