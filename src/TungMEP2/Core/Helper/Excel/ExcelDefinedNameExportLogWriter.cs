using System.IO;
using System.Text;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelDefinedNameExportLogWriter
{
	private static readonly bool EnableExportLogWrite = false;

	public static bool IsEnabled => EnableExportLogWrite;

	public static string Write(string workbookPath, ExcelDefinedNameExportSummary summary)
	{
		if (!EnableExportLogWrite)
		{
			return string.Empty;
		}
		string logPath = ExcelDefinedNameExportPathPolicy.BuildLogPath(workbookPath);
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("Excel Defined Name Export");
		builder.AppendLine("Workbook=" + summary.WorkbookPath);
		builder.AppendLine($"StartedUtc={summary.StartedAtUtc:O}");
		builder.AppendLine($"EndedUtc={summary.EndedAtUtc:O}");
		builder.AppendLine("Quality=" + summary.Quality);
		builder.AppendLine($"Total={summary.Total}");
		builder.AppendLine($"Exported={summary.Exported}");
		builder.AppendLine($"Skipped={summary.Skipped}");
		builder.AppendLine($"Failed={summary.Failed}");
		builder.AppendLine();
		foreach (ExcelDefinedNameExportItemResult item in summary.Items)
		{
			string reason = item.ReasonCode?.ToString() ?? "None";
			string infoTag = (item.IsInformational ? "INFO" : "ITEM");
			string output = item.OutputPath ?? string.Empty;
			builder.AppendLine($"{infoTag}|Sheet={item.SheetName}|Name={item.DefinedName}|Exported={item.IsExported}|Reason={reason}|Output={output}|Message={item.Message}");
		}
		File.WriteAllText(logPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		return logPath;
	}
}
