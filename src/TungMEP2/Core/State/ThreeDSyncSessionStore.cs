using System;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.State;

public static class ThreeDSyncSessionStore
{
	private static readonly object LockObject = new object();

	private static ThreeDSyncSession? _currentSession;

	public static string GetDocumentKey(Document document)
	{
		string path = document.PathName ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(path))
		{
			return path;
		}
		return "UNSAVED:" + document.Title;
	}

	public static void Save(ThreeDSyncSession session)
	{
		lock (LockObject)
		{
			_currentSession = session;
		}
	}

	public static bool TryGet(Document document, out ThreeDSyncSession? session)
	{
		string key = GetDocumentKey(document);
		lock (LockObject)
		{
			if (_currentSession != null && string.Equals(_currentSession.DocumentKey, key, StringComparison.OrdinalIgnoreCase))
			{
				session = _currentSession;
				return true;
			}
		}
		session = null;
		return false;
	}

	public static void Clear()
	{
		lock (LockObject)
		{
			_currentSession = null;
		}
	}
}
