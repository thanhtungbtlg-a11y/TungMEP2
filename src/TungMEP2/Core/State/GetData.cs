using System.IO;
using System.Reflection;
using System.Text.Json;

namespace VP_RevitAPI.State;

public static class GetData
{
	public static string FromJson(string fileName, string key)
	{
		string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		string storeDir = Path.Combine(baseDir, "Store");
		string jsonPath = Path.Combine(storeDir, fileName + ".json");
		string json = File.ReadAllText(jsonPath);
		using JsonDocument doc = JsonDocument.Parse(json);
		JsonElement root = doc.RootElement;
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value))
		{
			return (value.ValueKind == JsonValueKind.String) ? value.GetString() : value.ToString();
		}
		if (root.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in root.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("key", out var itemKey) || itemKey.ValueKind != JsonValueKind.String || !string.Equals(itemKey.GetString(), key))
				{
					continue;
				}
				if (!item.TryGetProperty("value", out var itemValue))
				{
					return string.Empty;
				}
				return (itemValue.ValueKind == JsonValueKind.String) ? itemValue.GetString() : itemValue.ToString();
			}
		}
		return string.Empty;
	}
}
