using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VP_RevitAPI.State;

public static class ExcelLegendLinkRecentFileStore
{
	private sealed class PersistedRecentFileState
	{
		public string RecentExcelPath { get; set; } = string.Empty;

		public DateTime LastUpdatedUtc { get; set; }
	}

	private static readonly object LockObject = new object();

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static string Load()
	{
		lock (LockObject)
		{
			try
			{
				string filePath = GetStorageFilePath();
				if (!File.Exists(filePath))
				{
					return string.Empty;
				}
				string json = File.ReadAllText(filePath, Encoding.UTF8);
				return JsonSerializer.Deserialize<PersistedRecentFileState>(json, JsonOptions)?.RecentExcelPath?.Trim() ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}
	}

	public static void Save(string excelPath)
	{
		string normalizedPath = excelPath?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(normalizedPath))
		{
			return;
		}
		lock (LockObject)
		{
			try
			{
				string directoryPath = GetStorageDirectoryPath();
				Directory.CreateDirectory(directoryPath);
				PersistedRecentFileState state = new PersistedRecentFileState
				{
					RecentExcelPath = normalizedPath,
					LastUpdatedUtc = DateTime.UtcNow
				};
				string json = JsonSerializer.Serialize(state, JsonOptions);
				string filePath = GetStorageFilePath();
				string tempPath = filePath + ".tmp";
				File.WriteAllText(tempPath, json, Encoding.UTF8);
				File.Copy(tempPath, filePath, overwrite: true);
				File.Delete(tempPath);
			}
			catch
			{
			}
		}
	}

	private static string GetStorageDirectoryPath()
	{
		string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		return Path.Combine(desktop, "VP_RevitAPI", "ExcelLegendLink");
	}

	private static string GetStorageFilePath()
	{
		return Path.Combine(GetStorageDirectoryPath(), "recent_excel_file.json");
	}
}
