using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.State;

public static class SyncViewStateStore
{
	private sealed class PersistedSyncViewState
	{
		public string DocumentKey { get; set; } = string.Empty;

		public SyncViewKind Kind { get; set; }

		public PersistedXyz Corner1 { get; set; } = new PersistedXyz();

		public PersistedXyz Corner2 { get; set; } = new PersistedXyz();

		public PersistedViewOrientation3D? Orientation3D { get; set; }

		public bool IsPerspective { get; set; }

		public bool IsSectionBoxActive { get; set; }

		public PersistedBoundingBox? SectionBox { get; set; }
	}

	private sealed class PersistedViewOrientation3D
	{
		public PersistedXyz EyePosition { get; set; } = new PersistedXyz();

		public PersistedXyz UpDirection { get; set; } = new PersistedXyz();

		public PersistedXyz ForwardDirection { get; set; } = new PersistedXyz();
	}

	private sealed class PersistedBoundingBox
	{
		public PersistedXyz Min { get; set; } = new PersistedXyz();

		public PersistedXyz Max { get; set; } = new PersistedXyz();

		public PersistedXyz Origin { get; set; } = new PersistedXyz();

		public PersistedXyz BasisX { get; set; } = new PersistedXyz();

		public PersistedXyz BasisY { get; set; } = new PersistedXyz();

		public PersistedXyz BasisZ { get; set; } = new PersistedXyz();
	}

	private sealed class PersistedXyz
	{
		public double X { get; set; }

		public double Y { get; set; }

		public double Z { get; set; }
	}

	private static readonly object LockObject = new object();

	private static readonly Dictionary<string, SyncViewState> StateByDocumentKey = new Dictionary<string, SyncViewState>(StringComparer.OrdinalIgnoreCase);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static string GetDocumentKey(Document document)
	{
		string path = document.PathName ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(path))
		{
			return path;
		}
		return GetUnsavedTitleKey(document);
	}

	public static void Save(Document document, SyncViewState state)
	{
		string key = GetDocumentKey(document);
		if (!string.Equals(state.DocumentKey, key, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("State document key does not match current document key.");
		}
		lock (LockObject)
		{
			StateByDocumentKey[key] = state;
		}
		TrySaveToDisk(state);
	}

	public static bool TryGet(Document document, out SyncViewState? state)
	{
		foreach (string key in GetCandidateKeys(document))
		{
			lock (LockObject)
			{
				if (StateByDocumentKey.TryGetValue(key, out SyncViewState found))
				{
					state = found;
					return true;
				}
			}
			if (TryLoadFromDisk(key, out SyncViewState loaded) && loaded != null)
			{
				lock (LockObject)
				{
					StateByDocumentKey[key] = loaded;
				}
				state = loaded;
				return true;
			}
		}
		state = null;
		return false;
	}

	private static IEnumerable<string> GetCandidateKeys(Document document)
	{
		string path = document.PathName ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(path))
		{
			yield return path;
		}
		yield return GetUnsavedTitleKey(document);
	}

	private static string GetUnsavedTitleKey(Document document)
	{
		return "UNSAVED:" + document.Title;
	}

	private static void TrySaveToDisk(SyncViewState state)
	{
		try
		{
			string directory = GetStorageDirectoryPath();
			Directory.CreateDirectory(directory);
			PersistedSyncViewState persisted = ToPersistedState(state);
			string json = JsonSerializer.Serialize(persisted, JsonOptions);
			string finalPath = GetStorageFilePath(state.DocumentKey);
			string tempPath = finalPath + ".tmp";
			File.WriteAllText(tempPath, json, Encoding.UTF8);
			File.Copy(tempPath, finalPath, overwrite: true);
			File.Delete(tempPath);
		}
		catch
		{
		}
	}

	private static bool TryLoadFromDisk(string key, out SyncViewState? state)
	{
		try
		{
			string path = GetStorageFilePath(key);
			if (!File.Exists(path))
			{
				state = null;
				return false;
			}
			string json = File.ReadAllText(path, Encoding.UTF8);
			PersistedSyncViewState persisted = JsonSerializer.Deserialize<PersistedSyncViewState>(json, JsonOptions);
			if (persisted == null)
			{
				state = null;
				return false;
			}
			state = ToRuntimeState(persisted);
			return true;
		}
		catch
		{
			state = null;
			return false;
		}
	}

	private static string GetStorageDirectoryPath()
	{
		string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		return Path.Combine(desktop, "VP_RevitAPI", "SyncView");
	}

	private static string GetStorageFilePath(string key)
	{
		string hash = ComputeSha256(key);
		return Path.Combine(GetStorageDirectoryPath(), "syncview_" + hash + ".json");
	}

	private static string ComputeSha256(string input)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(input);
		using SHA256 sha256 = SHA256.Create();
		byte[] hash = sha256.ComputeHash(bytes);
		StringBuilder builder = new StringBuilder(hash.Length * 2);
		byte[] array = hash;
		foreach (byte b in array)
		{
			builder.Append(b.ToString("x2"));
		}
		return builder.ToString();
	}

	private static PersistedSyncViewState ToPersistedState(SyncViewState state)
	{
		return new PersistedSyncViewState
		{
			DocumentKey = state.DocumentKey,
			Kind = state.Kind,
			Corner1 = ToXyzDto(state.Corner1),
			Corner2 = ToXyzDto(state.Corner2),
			Orientation3D = ((state.Orientation3D == null) ? null : new PersistedViewOrientation3D
			{
				EyePosition = ToXyzDto(state.Orientation3D.EyePosition),
				UpDirection = ToXyzDto(state.Orientation3D.UpDirection),
				ForwardDirection = ToXyzDto(state.Orientation3D.ForwardDirection)
			}),
			IsPerspective = state.IsPerspective,
			IsSectionBoxActive = state.IsSectionBoxActive,
			SectionBox = ((state.SectionBox == null) ? null : new PersistedBoundingBox
			{
				Min = ToXyzDto(state.SectionBox.Min),
				Max = ToXyzDto(state.SectionBox.Max),
				Origin = ToXyzDto(state.SectionBox.Transform.Origin),
				BasisX = ToXyzDto(state.SectionBox.Transform.BasisX),
				BasisY = ToXyzDto(state.SectionBox.Transform.BasisY),
				BasisZ = ToXyzDto(state.SectionBox.Transform.BasisZ)
			})
		};
	}

	private static SyncViewState ToRuntimeState(PersistedSyncViewState state)
	{
		ViewOrientation3D orientation = null;
		if (state.Orientation3D != null)
		{
			orientation = new ViewOrientation3D(ToXyz(state.Orientation3D.EyePosition), ToXyz(state.Orientation3D.UpDirection), ToXyz(state.Orientation3D.ForwardDirection));
		}
		BoundingBoxXYZ sectionBox = null;
		if (state.SectionBox != null)
		{
			Transform transform = Transform.Identity;
			transform.Origin = ToXyz(state.SectionBox.Origin);
			transform.BasisX = ToXyz(state.SectionBox.BasisX);
			transform.BasisY = ToXyz(state.SectionBox.BasisY);
			transform.BasisZ = ToXyz(state.SectionBox.BasisZ);
			sectionBox = new BoundingBoxXYZ
			{
				Min = ToXyz(state.SectionBox.Min),
				Max = ToXyz(state.SectionBox.Max),
				Transform = transform
			};
		}
		return new SyncViewState
		{
			DocumentKey = state.DocumentKey,
			Kind = state.Kind,
			Corner1 = ToXyz(state.Corner1),
			Corner2 = ToXyz(state.Corner2),
			Orientation3D = orientation,
			IsPerspective = state.IsPerspective,
			IsSectionBoxActive = state.IsSectionBoxActive,
			SectionBox = sectionBox
		};
	}

	private static PersistedXyz ToXyzDto(XYZ xyz)
	{
		return new PersistedXyz
		{
			X = xyz.X,
			Y = xyz.Y,
			Z = xyz.Z
		};
	}

	private static XYZ ToXyz(PersistedXyz xyz)
	{
		return new XYZ(xyz.X, xyz.Y, xyz.Z);
	}
}
