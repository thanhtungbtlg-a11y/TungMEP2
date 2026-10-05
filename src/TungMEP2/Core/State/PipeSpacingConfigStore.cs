using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using VP_RevitAPI.Internal.Ctn.PipeSpacing;

namespace VP_RevitAPI.State;

internal static class PipeSpacingConfigStore
{
	private sealed class PersistedRoot
	{
		public int Version { get; set; }
		public List<PersistedProject> Projects { get; set; } = new List<PersistedProject>();
	}

	private sealed class PersistedProject
	{
		public string DocumentKey { get; set; } = string.Empty;
		public DateTime LastUpdatedUtc { get; set; }
		public PersistedSettings Settings { get; set; } = new PersistedSettings();
	}

	private sealed class PersistedSettings
	{
		public int Version { get; set; }
		public int Mode { get; set; }
		public double ClearGapMm { get; set; }
		public double CenterSpacingMm { get; set; }
		public double FallbackClearGapMm { get; set; }
		public bool IncludeInsulation { get; set; }
		public bool IncludeFittingEnvelope { get; set; }
		public double ParallelAngleToleranceDegrees { get; set; }
		public bool PreviewBeforeApply { get; set; }
		public List<PersistedDnRule> DnRules { get; set; } = new List<PersistedDnRule>();
	}

	private sealed class PersistedDnRule
	{
		public double MaxNominalDiameter { get; set; }
		public double ClearGap { get; set; }
	}

	private static readonly object LockObject = new object();
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	internal static PipeSpacingSettings Load(Document document)
	{
		string key = document == null ? string.Empty : ThreeDSyncSessionStore.GetDocumentKey(document);
		lock (LockObject)
		{
			try
			{
				string path = GetStorageFilePath();
				if (!File.Exists(path))
				{
					return PipeSpacingSettings.CreateDefault();
				}
				return DeserializeForDocument(File.ReadAllText(path, Encoding.UTF8), key);
			}
			catch
			{
				return PipeSpacingSettings.CreateDefault();
			}
		}
	}

	internal static void Save(Document document, PipeSpacingSettings settings)
	{
		if (document == null || settings == null)
		{
			return;
		}
		string key = ThreeDSyncSessionStore.GetDocumentKey(document);
		PipeSpacingSettings normalized = settings.Normalize();
		lock (LockObject)
		{
			try
			{
				PersistedRoot root = LoadRootFromDisk();
				PersistedProject project = new PersistedProject
				{
					DocumentKey = key,
					LastUpdatedUtc = DateTime.UtcNow,
					Settings = ToPersisted(normalized)
				};
				int index = root.Projects.FindIndex(item =>
					string.Equals(item.DocumentKey, key, StringComparison.OrdinalIgnoreCase));
				if (index >= 0)
				{
					root.Projects[index] = project;
				}
				else
				{
					root.Projects.Add(project);
				}
				root.Version = PipeSpacingSettings.CurrentVersion;
				string directory = GetStorageDirectoryPath();
				Directory.CreateDirectory(directory);
				string path = GetStorageFilePath();
				string temporaryPath = path + ".tmp";
				File.WriteAllText(temporaryPath, JsonSerializer.Serialize(root, JsonOptions), new UTF8Encoding(false));
				File.Copy(temporaryPath, path, true);
				File.Delete(temporaryPath);
			}
			catch
			{
				// Settings persistence must never stop a Revit command.
			}
		}
	}

	internal static PipeSpacingSettings DeserializeForDocument(string json, string documentKey)
	{
		try
		{
			PersistedRoot root = JsonSerializer.Deserialize<PersistedRoot>(json ?? string.Empty, JsonOptions);
			if (root == null || root.Version != PipeSpacingSettings.CurrentVersion)
			{
				return PipeSpacingSettings.CreateDefault();
			}
			PersistedProject project = root?.Projects?.FirstOrDefault(item =>
				string.Equals(item.DocumentKey ?? string.Empty, documentKey ?? string.Empty, StringComparison.OrdinalIgnoreCase));
			return project?.Settings == null || project.Settings.Version != PipeSpacingSettings.CurrentVersion
				? PipeSpacingSettings.CreateDefault()
				: FromPersisted(project.Settings).Normalize();
		}
		catch
		{
			return PipeSpacingSettings.CreateDefault();
		}
	}

	private static PersistedRoot LoadRootFromDisk()
	{
		try
		{
			string path = GetStorageFilePath();
			if (!File.Exists(path))
			{
				return NewRoot();
			}
			PersistedRoot root = JsonSerializer.Deserialize<PersistedRoot>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
			if (root == null)
			{
				return NewRoot();
			}
			root.Projects ??= new List<PersistedProject>();
			return root;
		}
		catch
		{
			return NewRoot();
		}
	}

	private static PersistedRoot NewRoot()
	{
		return new PersistedRoot
		{
			Version = PipeSpacingSettings.CurrentVersion,
			Projects = new List<PersistedProject>()
		};
	}

	private static PersistedSettings ToPersisted(PipeSpacingSettings settings)
	{
		return new PersistedSettings
		{
			Version = settings.Version,
			Mode = (int)settings.Mode,
			ClearGapMm = settings.ClearGapMm,
			CenterSpacingMm = settings.CenterSpacingMm,
			FallbackClearGapMm = settings.FallbackClearGapMm,
			IncludeInsulation = settings.IncludeInsulation,
			IncludeFittingEnvelope = settings.IncludeFittingEnvelope,
			ParallelAngleToleranceDegrees = settings.ParallelAngleToleranceDegrees,
			PreviewBeforeApply = settings.PreviewBeforeApply,
			DnRules = settings.DnRules.Select(rule => new PersistedDnRule
			{
				MaxNominalDiameter = rule.MaxNominalDiameter,
				ClearGap = rule.ClearGap
			}).ToList()
		};
	}

	private static PipeSpacingSettings FromPersisted(PersistedSettings persisted)
	{
		return new PipeSpacingSettings
		{
			Version = persisted.Version,
			Mode = (PipeSpacingMode)persisted.Mode,
			ClearGapMm = persisted.ClearGapMm,
			CenterSpacingMm = persisted.CenterSpacingMm,
			FallbackClearGapMm = persisted.FallbackClearGapMm,
			IncludeInsulation = persisted.IncludeInsulation,
			IncludeFittingEnvelope = persisted.IncludeFittingEnvelope,
			ParallelAngleToleranceDegrees = persisted.ParallelAngleToleranceDegrees,
			PreviewBeforeApply = persisted.PreviewBeforeApply,
			DnRules = (persisted.DnRules ?? new List<PersistedDnRule>())
				.Select(rule => new PipeSpacingDnRule(rule.MaxNominalDiameter, rule.ClearGap))
				.ToList()
		};
	}

	private static string GetStorageDirectoryPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VP_RevitAPI");
	}

	private static string GetStorageFilePath()
	{
		return Path.Combine(GetStorageDirectoryPath(), "pipe_spacing_config.json");
	}
}
