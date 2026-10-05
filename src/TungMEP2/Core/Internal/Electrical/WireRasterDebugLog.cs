using System;
using System.IO;
using System.Text;

namespace VP_RevitAPI.Internal.Electrical;

internal sealed class WireRasterDebugLog
{
	private readonly StringBuilder _builder = new StringBuilder();

	internal string FilePath { get; }

	private WireRasterDebugLog(string filePath)
	{
		FilePath = filePath;
	}

	internal static WireRasterDebugLog? TryCreateOnDesktop()
	{
		try
		{
			string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
			string fileName = $"WireRasterLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
			string filePath = Path.Combine(desktop, fileName);
			WireRasterDebugLog logger = new WireRasterDebugLog(filePath);
			logger.Log("WireRaster debug log created.");
			return logger;
		}
		catch
		{
			return null;
		}
	}

	internal void Log(string message)
	{
		_builder.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("] ")
			.AppendLine(message);
	}

	internal void Save()
	{
		string directory = Path.GetDirectoryName(FilePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}
		File.WriteAllText(FilePath, _builder.ToString());
	}
}
