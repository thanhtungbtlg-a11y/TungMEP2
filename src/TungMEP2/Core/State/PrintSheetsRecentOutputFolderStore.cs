using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VP_RevitAPI.State;

public static class PrintSheetsRecentOutputFolderStore
{
	private sealed class PersistedRecentOutputFolderState
	{
		// Kept for backward compatibility with earlier TungMEP2 builds and diagnostics.
		public string RecentOutputFolder { get; set; } = string.Empty;

		public string RecentPdfOutputFolder { get; set; } = string.Empty;

		public string RecentDwgOutputFolder { get; set; } = string.Empty;

		public DateTime LastUpdatedUtc { get; set; }
	}

	private static readonly object LockObject = new object();

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static string Load()
	{
		return LoadPdfOutputFolder();
	}

	public static string LoadPdfOutputFolder()
	{
		PersistedRecentOutputFolderState state = LoadState();
		return FirstNonEmpty(
			state.RecentPdfOutputFolder,
			state.RecentOutputFolder,
			GetDefaultOutputFolderPath());
	}

	public static string LoadDwgOutputFolder()
	{
		PersistedRecentOutputFolderState state = LoadState();
		return FirstNonEmpty(
			state.RecentDwgOutputFolder,
			state.RecentOutputFolder,
			GetDefaultOutputFolderPath());
	}

	public static void Save(string folderPath)
	{
		Save(folderPath, folderPath);
	}

	public static void Save(string pdfFolderPath, string dwgFolderPath)
	{
		string normalizedPdfFolderPath = pdfFolderPath?.Trim() ?? string.Empty;
		string normalizedDwgFolderPath = dwgFolderPath?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(normalizedPdfFolderPath) &&
			string.IsNullOrWhiteSpace(normalizedDwgFolderPath))
		{
			return;
		}

		lock (LockObject)
		{
			string tempPath = string.Empty;
			try
			{
				PersistedRecentOutputFolderState previousState = LoadStateUnsafe();
				string recentPdfOutputFolder = FirstNonEmpty(
					normalizedPdfFolderPath,
					previousState.RecentPdfOutputFolder,
					previousState.RecentOutputFolder,
					GetDefaultOutputFolderPath());
				string recentDwgOutputFolder = FirstNonEmpty(
					normalizedDwgFolderPath,
					previousState.RecentDwgOutputFolder,
					previousState.RecentOutputFolder,
					GetDefaultOutputFolderPath());

				string directoryPath = GetStorageDirectoryPath();
				Directory.CreateDirectory(directoryPath);
				PersistedRecentOutputFolderState state = new PersistedRecentOutputFolderState
				{
					RecentOutputFolder = recentPdfOutputFolder,
					RecentPdfOutputFolder = recentPdfOutputFolder,
					RecentDwgOutputFolder = recentDwgOutputFolder,
					LastUpdatedUtc = DateTime.UtcNow
				};
				string json = JsonSerializer.Serialize(state, JsonOptions);
				string filePath = GetStorageFilePath();
				tempPath = filePath + ".tmp";
				File.WriteAllText(tempPath, json, Encoding.UTF8);
				if (File.Exists(filePath))
				{
					File.Replace(tempPath, filePath, null);
				}
				else
				{
					File.Move(tempPath, filePath);
				}
			}
			catch
			{
			}
			finally
			{
				if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
				{
					try
					{
						File.Delete(tempPath);
					}
					catch
					{
					}
				}
			}
		}
	}

	private static PersistedRecentOutputFolderState LoadState()
	{
		lock (LockObject)
		{
			return LoadStateUnsafe();
		}
	}

	private static PersistedRecentOutputFolderState LoadStateUnsafe()
	{
		try
		{
			string filePath = GetStorageFilePath();
			if (!File.Exists(filePath))
			{
				return new PersistedRecentOutputFolderState();
			}

			string json = File.ReadAllText(filePath, Encoding.UTF8);
			return JsonSerializer.Deserialize<PersistedRecentOutputFolderState>(json, JsonOptions) ??
				new PersistedRecentOutputFolderState();
		}
		catch
		{
			return new PersistedRecentOutputFolderState();
		}
	}

	private static string FirstNonEmpty(params string[] values)
	{
		foreach (string value in values)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				return value.Trim();
			}
		}

		return string.Empty;
	}

	private static string GetStorageDirectoryPath()
	{
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		return Path.Combine(localAppData, "TungMEP2", "State", "PrintSheets");
	}

	private static string GetStorageFilePath()
	{
		return Path.Combine(GetStorageDirectoryPath(), "recent_output_folder.json");
	}

	private static string GetDefaultOutputFolderPath()
	{
		string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		return Path.Combine(desktop, "TungMEP2", "PrintSheets");
	}
}
