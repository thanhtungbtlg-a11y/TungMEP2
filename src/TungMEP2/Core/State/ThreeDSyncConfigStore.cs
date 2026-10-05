using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.State;

public static class ThreeDSyncConfigStore
{
	private sealed class PersistedConfigRoot
	{
		public int Version { get; set; }

		public List<PersistedProjectConfig> Projects { get; set; } = new List<PersistedProjectConfig>();
	}

	private sealed class PersistedProjectConfig
	{
		public string DocumentKey { get; set; } = string.Empty;

		public DateTime LastUpdatedUtc { get; set; }

		public PersistedViewReference? PlanView { get; set; }

		public List<PersistedViewReference> ThreeDViews { get; set; } = new List<PersistedViewReference>();
	}

	private sealed class PersistedViewReference
	{
		public string UniqueId { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;
	}

	private const int CurrentVersion = 1;

	private static readonly object LockObject = new object();

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static ThreeDSyncViewSelectionConfig? Load(Document document)
	{
		string documentKey = ThreeDSyncSessionStore.GetDocumentKey(document);
		lock (LockObject)
		{
			PersistedConfigRoot root = LoadRoot();
			PersistedProjectConfig project = root.Projects.FirstOrDefault((PersistedProjectConfig item) => string.Equals(item.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase));
			return (project == null) ? null : ToRuntime(project);
		}
	}

	public static void Save(Document document, ElementId planViewId, IReadOnlyList<ElementId> view3DIds)
	{
		if (document == null || planViewId == null || view3DIds == null || view3DIds.Count == 0 || !(document.GetElement(planViewId) is ViewPlan { IsTemplate: false } planView))
		{
			return;
		}
		List<ThreeDSyncViewReference> selectedThreeDViews = (from id in view3DIds.Distinct()
			select document.GetElement(id) as View3D into view
			where view != null && !view.IsTemplate && !view.IsPerspective
			select new ThreeDSyncViewReference
			{
				UniqueId = (view.UniqueId ?? string.Empty),
				Name = (view.Name ?? string.Empty)
			} into view
			where !string.IsNullOrWhiteSpace(view.UniqueId) || !string.IsNullOrWhiteSpace(view.Name)
			select view).ToList();
		if (selectedThreeDViews.Count == 0)
		{
			return;
		}
		ThreeDSyncViewSelectionConfig config = new ThreeDSyncViewSelectionConfig
		{
			DocumentKey = ThreeDSyncSessionStore.GetDocumentKey(document),
			PlanViewUniqueId = (planView.UniqueId ?? string.Empty),
			PlanViewName = (planView.Name ?? string.Empty),
			ThreeDViews = selectedThreeDViews
		};
		lock (LockObject)
		{
			PersistedConfigRoot root = LoadRoot();
			PersistedProjectConfig persistedProject = ToPersisted(config);
			persistedProject.LastUpdatedUtc = DateTime.UtcNow;
			int index = root.Projects.FindIndex((PersistedProjectConfig item) => string.Equals(item.DocumentKey, persistedProject.DocumentKey, StringComparison.OrdinalIgnoreCase));
			if (index >= 0)
			{
				root.Projects[index] = persistedProject;
			}
			else
			{
				root.Projects.Add(persistedProject);
			}
			SaveRoot(root);
		}
	}

	private static PersistedConfigRoot LoadRoot()
	{
		try
		{
			string path = GetStorageFilePath();
			if (!File.Exists(path))
			{
				return new PersistedConfigRoot
				{
					Version = 1,
					Projects = new List<PersistedProjectConfig>()
				};
			}
			string json = File.ReadAllText(path, Encoding.UTF8);
			PersistedConfigRoot root = JsonSerializer.Deserialize<PersistedConfigRoot>(json, JsonOptions);
			if (root == null)
			{
				return new PersistedConfigRoot
				{
					Version = 1,
					Projects = new List<PersistedProjectConfig>()
				};
			}
			root.Version = 1;
			PersistedConfigRoot persistedConfigRoot = root;
			if (persistedConfigRoot.Projects == null)
			{
				List<PersistedProjectConfig> list = (persistedConfigRoot.Projects = new List<PersistedProjectConfig>());
			}
			return root;
		}
		catch
		{
			return new PersistedConfigRoot
			{
				Version = 1,
				Projects = new List<PersistedProjectConfig>()
			};
		}
	}

	private static void SaveRoot(PersistedConfigRoot root)
	{
		try
		{
			string directory = GetStorageDirectoryPath();
			Directory.CreateDirectory(directory);
			root.Version = 1;
			string path = GetStorageFilePath();
			string tempPath = path + ".tmp";
			string json = JsonSerializer.Serialize(root, JsonOptions);
			File.WriteAllText(tempPath, json, Encoding.UTF8);
			File.Copy(tempPath, path, overwrite: true);
			File.Delete(tempPath);
		}
		catch
		{
		}
	}

	private static string GetStorageDirectoryPath()
	{
		string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		return Path.Combine(desktop, "VP_RevitAPI");
	}

	private static string GetStorageFilePath()
	{
		return Path.Combine(GetStorageDirectoryPath(), "3dsync_config.json");
	}

	private static PersistedProjectConfig ToPersisted(ThreeDSyncViewSelectionConfig config)
	{
		return new PersistedProjectConfig
		{
			DocumentKey = config.DocumentKey,
			PlanView = new PersistedViewReference
			{
				UniqueId = config.PlanViewUniqueId,
				Name = config.PlanViewName
			},
			ThreeDViews = config.ThreeDViews.Select((ThreeDSyncViewReference view) => new PersistedViewReference
			{
				UniqueId = view.UniqueId,
				Name = view.Name
			}).ToList()
		};
	}

	private static ThreeDSyncViewSelectionConfig ToRuntime(PersistedProjectConfig config)
	{
		return new ThreeDSyncViewSelectionConfig
		{
			DocumentKey = (config.DocumentKey ?? string.Empty),
			PlanViewUniqueId = (config.PlanView?.UniqueId ?? string.Empty),
			PlanViewName = (config.PlanView?.Name ?? string.Empty),
			ThreeDViews = (from view in config.ThreeDViews ?? new List<PersistedViewReference>()
				select new ThreeDSyncViewReference
				{
					UniqueId = (view.UniqueId ?? string.Empty),
					Name = (view.Name ?? string.Empty)
				} into view
				where !string.IsNullOrWhiteSpace(view.UniqueId) || !string.IsNullOrWhiteSpace(view.Name)
				select view).ToList()
		};
	}
}
