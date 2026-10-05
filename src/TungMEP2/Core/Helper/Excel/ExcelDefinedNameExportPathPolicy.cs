using System.IO;
using System.Linq;

namespace VP_RevitAPI.Helper.Excel;

public static class ExcelDefinedNameExportPathPolicy
{
	public static string BuildImageFileName(string workbookPath, string sheetName, string definedName)
	{
		string workbookBaseName = GetWorkbookBaseName(workbookPath);
		string raw = workbookBaseName + "_" + sheetName + "_" + definedName;
		string sanitized = SanitizeFileName(raw);
		return sanitized + ".png";
	}

	public static string BuildImagePath(string workbookPath, string sheetName, string definedName)
	{
		string directory = GetWorkbookDirectory(workbookPath);
		return Path.Combine(directory, BuildImageFileName(workbookPath, sheetName, definedName));
	}

	public static string BuildPdfFileName(string workbookPath, string sheetName, string definedName)
	{
		string workbookBaseName = GetWorkbookBaseName(workbookPath);
		string raw = workbookBaseName + "_" + sheetName + "_" + definedName;
		string sanitized = SanitizeFileName(raw);
		return sanitized + ".pdf";
	}

	public static string BuildPdfPath(string workbookPath, string sheetName, string definedName)
	{
		string directory = GetWorkbookDirectory(workbookPath);
		return Path.Combine(directory, BuildPdfFileName(workbookPath, sheetName, definedName));
	}

	public static string BuildLogPath(string workbookPath)
	{
		string directory = GetWorkbookDirectory(workbookPath);
		string fileName = SanitizeFileName(GetWorkbookBaseName(workbookPath)) + ".log";
		return Path.Combine(directory, fileName);
	}

	public static string BuildSelfTestLogPath(string workbookPath)
	{
		string directory = GetWorkbookDirectory(workbookPath);
		string fileName = SanitizeFileName(GetWorkbookBaseName(workbookPath)) + ".selftest.log";
		return Path.Combine(directory, fileName);
	}

	private static string GetWorkbookBaseName(string workbookPath)
	{
		string workbookBaseName = Path.GetFileNameWithoutExtension(workbookPath)?.Trim();
		if (string.IsNullOrWhiteSpace(workbookBaseName))
		{
			workbookBaseName = "Workbook";
		}
		return workbookBaseName;
	}

	private static string GetWorkbookDirectory(string workbookPath)
	{
		string directory = Path.GetDirectoryName(workbookPath);
		if (string.IsNullOrWhiteSpace(directory))
		{
			directory = Directory.GetCurrentDirectory();
		}
		Directory.CreateDirectory(directory);
		return directory;
	}

	private static string SanitizeFileName(string value)
	{
		char[] invalidChars = Path.GetInvalidFileNameChars();
		char[] sanitized = value.Select((char ch) => invalidChars.Contains(ch) ? '_' : ch).ToArray();
		string normalized = new string(sanitized).Trim();
		return string.IsNullOrWhiteSpace(normalized) ? "Unnamed" : normalized;
	}
}
