using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Office.Interop.Excel;
using VP_RevitAPI.Helper.Excel.Func;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelDefinedNameRenderSelfTester
{
	public static string Run(string excelPath)
	{
		string logPath = ExcelDefinedNameExportPathPolicy.BuildSelfTestLogPath(excelPath);
		List<string> lines = new List<string>
		{
			"Excel Defined Name Render Self Test",
			"Workbook=" + excelPath,
			$"StartedUtc={DateTime.UtcNow:O}"
		};
		ExcelInteropSession session = null;
		try
		{
			if (string.IsNullOrWhiteSpace(excelPath) || !File.Exists(excelPath))
			{
				lines.Add("ERROR|InvalidPath");
				return WriteLines(logPath, lines);
			}
			session = ExcelInteropSession.OpenReadOnly(excelPath);
			int totalNames = 0;
			int totalAttempts = 0;
			int successAttempts = 0;
			int failedAttempts = 0;
			SheetScopedNameScanner.ForEachDefinedName(session.Workbook, delegate(Worksheet? worksheet, Name name, bool isWorkbookScope)
			{
				totalNames++;
				string text = (isWorkbookScope ? "Workbook" : "Sheet");
				string text2 = (isWorkbookScope ? "Workbook" : (worksheet?.Name ?? "Workbook"));
				string name2 = name.Name;
				Range range = null;
				try
				{
					try
					{
						range = name.RefersToRange;
					}
					catch (Exception ex2)
					{
						lines.Add("NAME|Scope=" + text + "|Sheet=" + text2 + "|Name=" + name2 + "|Resolve=False|Message=" + ex2.Message);
						return;
					}
					if (range == null)
					{
						lines.Add("NAME|Scope=" + text + "|Sheet=" + text2 + "|Name=" + name2 + "|Resolve=False|Message=RefersToRange is null");
					}
					else if (range.Areas.Count > 1)
					{
						lines.Add("NAME|Scope=" + text + "|Sheet=" + text2 + "|Name=" + name2 + "|Resolve=False|Message=Multi-area not supported");
					}
					else
					{
						Worksheet worksheet2 = worksheet ?? range.Worksheet;
						if (worksheet2 == null)
						{
							lines.Add("NAME|Scope=" + text + "|Sheet=" + text2 + "|Name=" + name2 + "|Resolve=False|Message=Worksheet not resolved");
						}
						else
						{
							totalAttempts++;
							string text3 = ExcelDefinedNameExportPathPolicy.BuildImagePath(excelPath, text2, name2 + "__XpsFallback");
							if (NamedRangeImageRenderer.TryRenderJpeg(worksheet2, range, text3, out string error))
							{
								successAttempts++;
								lines.Add("ATTEMPT|Scope=" + text + "|Sheet=" + text2 + "|Name=" + name2 + "|Strategy=XpsFallback|Success=True|Output=" + text3 + "|Message=RenderedBy=XpsFallback;AutoCrop=BestEffort;Format=Png;PdfPreview=" + ExcelRenderFunc.PdfPreviewModeToken + ";PdfPageSetup=" + ExcelRenderFunc.PdfPageSetupModeToken + ";Fit=1x1;Zoom=False;Orientation=Auto");
							}
							else
							{
								failedAttempts++;
								lines.Add("ATTEMPT|Scope=" + text + "|Sheet=" + text2 + "|Name=" + name2 + "|Strategy=XpsFallback|Success=False|Output=" + text3 + "|Message=" + error);
							}
							if (worksheet == null && worksheet2 != null)
							{
								ExcelInteropSession.TryRelease(worksheet2);
							}
						}
					}
				}
				finally
				{
					ExcelInteropSession.TryRelease(range);
				}
			});
			lines.Add($"TotalNames={totalNames}");
			lines.Add($"TotalAttempts={totalAttempts}");
			lines.Add($"SuccessAttempts={successAttempts}");
			lines.Add($"FailedAttempts={failedAttempts}");
		}
		catch (Exception ex)
		{
			lines.Add("ERROR|Unhandled=" + ex.Message);
		}
		finally
		{
			if (session != null)
			{
				session.Dispose();
				foreach (string warning in session.CleanupWarnings)
				{
					lines.Add("INFO|CleanupWarning=" + warning);
				}
			}
			lines.Add($"EndedUtc={DateTime.UtcNow:O}");
		}
		return WriteLines(logPath, lines);
	}

	private static string WriteLines(string logPath, IReadOnlyList<string> lines)
	{
		File.WriteAllLines(logPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		return logPath;
	}
}
