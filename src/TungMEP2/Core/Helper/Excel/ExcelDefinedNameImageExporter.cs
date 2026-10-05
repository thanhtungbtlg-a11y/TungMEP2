using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Office.Interop.Excel;
using VP_RevitAPI.Helper.Excel.Func;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelDefinedNameImageExporter
{
	private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm", ".xls" };

	public static ExcelDefinedNameExportSummary Export(string excelPath)
	{
		return Export(excelPath, null);
	}

	public static ExcelDefinedNameExportSummary Export(string excelPath, IReadOnlyCollection<ExcelDefinedNameExportTarget>? targets)
	{
		DateTime startedAtUtc = DateTime.UtcNow;
		List<ExcelDefinedNameExportItemResult> items = new List<ExcelDefinedNameExportItemResult>();
		int total = 0;
		int exported = 0;
		int skipped = 0;
		int failed = 0;
		ExcelInteropSession session = null;
		try
		{
			if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
			{
				int num = failed;
				failed = num + 1;
				items.Add(new ExcelDefinedNameExportItemResult(string.Empty, string.Empty, null, IsExported: false, ExcelDefinedNameExportReasonCode.InvalidPath, "Input path is invalid or file does not exist."));
				return BuildSummary(excelPath, startedAtUtc, total, exported, skipped, failed, items);
			}
			string extension = Path.GetExtension(excelPath);
			if (!SupportedExtensions.Contains(extension))
			{
				int num = failed;
				failed = num + 1;
				items.Add(new ExcelDefinedNameExportItemResult(string.Empty, string.Empty, null, IsExported: false, ExcelDefinedNameExportReasonCode.UnsupportedExtension, "Unsupported extension: " + extension));
				return BuildSummary(excelPath, startedAtUtc, total, exported, skipped, failed, items);
			}
			try
			{
				session = ExcelInteropSession.OpenReadOnly(excelPath);
			}
			catch (Exception ex)
			{
				int num = failed;
				failed = num + 1;
				items.Add(new ExcelDefinedNameExportItemResult(string.Empty, string.Empty, null, IsExported: false, ExcelDefinedNameExportReasonCode.WorkbookOpenFailed, ex.Message));
				return BuildSummary(excelPath, startedAtUtc, total, exported, skipped, failed, items);
			}
			HashSet<string> seenOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<string> iterationCleanupWarnings = new List<string>();
			IReadOnlyCollection<ExcelDefinedNameExportTarget> filteredTargets = NormalizeTargets(targets);
			SheetScopedNameScanner.ForEachDefinedName(session.Workbook, delegate(Worksheet? worksheet, Name name, bool isWorkbookScope)
			{
				string sheetName = (isWorkbookScope ? "Workbook" : (worksheet?.Name ?? "Workbook"));
				string name2 = name.Name;
				if (!IsRequestedTarget(filteredTargets, sheetName, name2))
				{
					return;
				}
				int num2 = total;
				total = num2 + 1;
				Range range = null;
				try
				{
					try
					{
						range = name.RefersToRange;
					}
					catch (Exception ex2)
					{
						num2 = skipped;
						skipped = num2 + 1;
						items.Add(new ExcelDefinedNameExportItemResult(sheetName, name2, null, IsExported: false, ExcelDefinedNameExportReasonCode.ResolveFailed, ex2.Message));
						return;
					}
					if (range == null)
					{
						num2 = skipped;
						skipped = num2 + 1;
						items.Add(new ExcelDefinedNameExportItemResult(sheetName, name2, null, IsExported: false, ExcelDefinedNameExportReasonCode.NonRange, "Defined name does not resolve to a range."));
					}
					else if (range.Areas.Count > 1)
					{
						num2 = skipped;
						skipped = num2 + 1;
						items.Add(new ExcelDefinedNameExportItemResult(sheetName, name2, null, IsExported: false, ExcelDefinedNameExportReasonCode.MultiArea, "Multi-area ranges are not supported."));
					}
					else
					{
						string text = ExcelDefinedNameExportPathPolicy.BuildImagePath(excelPath, sheetName, name2);
						if (!seenOutputPaths.Add(text))
						{
							items.Add(new ExcelDefinedNameExportItemResult(sheetName, name2, text, IsExported: false, ExcelDefinedNameExportReasonCode.OverwriteByCollision, "Output path collides after sanitization. Later export overwrites earlier output.", IsInformational: true));
						}
						Worksheet worksheet2 = worksheet;
						if (worksheet2 == null)
						{
							worksheet2 = range.Worksheet;
						}
						if (worksheet2 == null)
						{
							num2 = failed;
							failed = num2 + 1;
							items.Add(new ExcelDefinedNameExportItemResult(sheetName, name2, text, IsExported: false, ExcelDefinedNameExportReasonCode.RenderFailed, "Cannot resolve worksheet for named range render."));
						}
						else
						{
							string error;
							bool flag = NamedRangeImageRenderer.TryRenderJpeg(worksheet2, range, text, out error);
							if (worksheet2 != worksheet)
							{
								ExcelInteropSession.TryRelease(worksheet2);
							}
							if (!flag)
							{
								num2 = failed;
								failed = num2 + 1;
								items.Add(new ExcelDefinedNameExportItemResult(sheetName, name2, text, IsExported: false, ExcelDefinedNameExportReasonCode.RenderFailed, error));
							}
							else
							{
								num2 = exported;
								exported = num2 + 1;
								items.Add(new ExcelDefinedNameExportItemResult(sheetName, name2, text, IsExported: true, null, "Exported. RenderedBy=XpsFallback;AutoCrop=BestEffort;Format=Png;PdfPreview=" + ExcelRenderFunc.PdfPreviewModeToken + ";PdfPageSetup=" + ExcelRenderFunc.PdfPageSetupModeToken + ";Fit=1x1;Zoom=False;Orientation=Auto"));
							}
						}
					}
				}
				finally
				{
					ExcelInteropSession.TryRelease(range);
				}
			}, iterationCleanupWarnings);
			foreach (string warning in iterationCleanupWarnings)
			{
				items.Add(new ExcelDefinedNameExportItemResult(string.Empty, string.Empty, null, IsExported: false, ExcelDefinedNameExportReasonCode.InteropCleanupWarning, warning, IsInformational: true));
			}
			skipped = total - exported - failed;
			return BuildSummary(excelPath, startedAtUtc, total, exported, skipped, failed, items);
		}
		finally
		{
			if (session != null)
			{
				session.Dispose();
				foreach (string warning2 in session.CleanupWarnings)
				{
					items.Add(new ExcelDefinedNameExportItemResult(string.Empty, string.Empty, null, IsExported: false, ExcelDefinedNameExportReasonCode.InteropCleanupWarning, warning2, IsInformational: true));
				}
			}
			ExcelDefinedNameExportSummary summary = BuildSummary(excelPath, startedAtUtc, total, exported, Math.Max(0, total - exported - failed), failed, items);
			if (ExcelDefinedNameExportLogWriter.IsEnabled)
			{
				ExcelDefinedNameExportLogWriter.Write(excelPath, summary);
			}
		}
	}

	private static ExcelDefinedNameExportSummary BuildSummary(string excelPath, DateTime startedAtUtc, int total, int exported, int skipped, int failed, IReadOnlyList<ExcelDefinedNameExportItemResult> items)
	{
		return new ExcelDefinedNameExportSummary(excelPath, "Standard", startedAtUtc, DateTime.UtcNow, total, exported, skipped, failed, items.ToList());
	}

	private static IReadOnlyCollection<ExcelDefinedNameExportTarget>? NormalizeTargets(IReadOnlyCollection<ExcelDefinedNameExportTarget>? targets)
	{
		if (targets == null || targets.Count == 0)
		{
			return null;
		}
		return (from item in targets
			where !string.IsNullOrWhiteSpace(item.DefinedName)
			select new ExcelDefinedNameExportTarget(item.SheetName?.Trim() ?? string.Empty, item.DefinedName.Trim())).Distinct().ToList();
	}

	private static bool IsRequestedTarget(IReadOnlyCollection<ExcelDefinedNameExportTarget>? targets, string sheetName, string definedName)
	{
		if (targets == null)
		{
			return true;
		}
		foreach (ExcelDefinedNameExportTarget target in targets)
		{
			if (!string.Equals(target.DefinedName, definedName, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(target.SheetName) && !string.Equals(target.SheetName, sheetName, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			return true;
		}
		return false;
	}
}
