using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Excel;

public sealed record ExcelLegendLinkMetadata(string Provider, string WorkbookPath, string SheetName, string DefinedName, long LastWriteTicks, long ImageLastWriteTicks, long ImageFileSize)
{
	public string ScopedDefinedName => BuildScopedDefinedName(SheetName, DefinedName);

	private const char LastWriteStampSeparator = ';';

	public const string ProviderName = "VP.ExcelLegendLink";

	public const string ProviderValue = "VP.ExcelLegendLink";

	public const string LegacyParameterName = "VP_ExcelLink";

	public const string LegacyViewMarker = "XLINK2|";

	public const string ProviderParameterName = "VP_Excel_Provider";

	public const string WorkbookPathParameterName = "VP_Excel_WorkbookPath";

	public const string ScopedDefinedNameParameterName = "VP_Excel_ScopedDefinedName";

	public const string SheetNameParameterName = "VP_Excel_SheetName";

	public const string DefinedNameParameterName = "VP_Excel_DefinedName";

	public const string LastWriteTicksParameterName = "VP_Excel_LastWriteTicks";

	private static readonly string[] RequiredParameterNames = new string[4] { "VP_Excel_Provider", "VP_Excel_WorkbookPath", "VP_Excel_ScopedDefinedName", "VP_Excel_LastWriteTicks" };

	private static readonly string[] ClearableParameterNames = new string[7] { "VP_Excel_Provider", "VP_Excel_WorkbookPath", "VP_Excel_ScopedDefinedName", "VP_Excel_LastWriteTicks", "VP_Excel_SheetName", "VP_Excel_DefinedName", "VP_ExcelLink" };

	public static ExcelLegendLinkMetadata Create(string workbookPath, string sheetName, string definedName, long lastWriteTicks, long imageLastWriteTicks, long imageFileSize)
	{
		return new ExcelLegendLinkMetadata("VP.ExcelLegendLink", workbookPath, sheetName, definedName, lastWriteTicks, imageLastWriteTicks, imageFileSize);
	}

	public string ToParameterValue()
	{
		string wb = Uri.EscapeDataString(WorkbookPath ?? string.Empty);
		string sd = Uri.EscapeDataString(ScopedDefinedName);
		string stamp = BuildLastWriteStamp(LastWriteTicks, ImageLastWriteTicks, ImageFileSize);
		return "p=" + Provider + "|wb=" + wb + "|sd=" + sd + "|ts=" + stamp;
	}

	public static bool TryParseFromParameterValue(string value, out ExcelLegendLinkMetadata? metadata, out string error)
	{
		metadata = null;
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(value))
		{
			error = "Metadata parameter value is empty.";
			return false;
		}
		Dictionary<string, string> map = ParseKeyValueTokens(value);
		if (!map.TryGetValue("p", out var provider) || !map.TryGetValue("wb", out var workbookToken) || !map.TryGetValue("ts", out var ticksText))
		{
			error = "Metadata parameter is missing required fields.";
			return false;
		}
		if (!TryParseLastWriteStamp(ticksText, out var ticks, out var imageTicks, out var imageSize))
		{
			error = "Invalid metadata timestamp.";
			return false;
		}
		string parsedScopedToken;
		string scopedToken = (map.TryGetValue("sd", out parsedScopedToken) ? parsedScopedToken : string.Empty);
		string parsedDefinedNameToken;
		string definedNameToken = (map.TryGetValue("dn", out parsedDefinedNameToken) ? parsedDefinedNameToken : string.Empty);
		string parsedSheetToken;
		string sheetToken = (map.TryGetValue("sh", out parsedSheetToken) ? parsedSheetToken : string.Empty);
		string scopedDefinedName = Uri.UnescapeDataString(scopedToken);
		if (string.IsNullOrWhiteSpace(scopedDefinedName) && !string.IsNullOrWhiteSpace(definedNameToken))
		{
			scopedDefinedName = BuildScopedDefinedName(Uri.UnescapeDataString(sheetToken), Uri.UnescapeDataString(definedNameToken));
		}
		if (!TrySplitScopedDefinedName(scopedDefinedName, out string parsedSheetName, out string parsedDefinedName))
		{
			error = "Metadata scoped defined name is invalid.";
			return false;
		}
		metadata = new ExcelLegendLinkMetadata(provider, Uri.UnescapeDataString(workbookToken), parsedSheetName, parsedDefinedName, ticks, imageTicks, imageSize);
		if (!string.Equals(metadata.Provider, "VP.ExcelLegendLink", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(metadata.WorkbookPath) || string.IsNullOrWhiteSpace(metadata.DefinedName))
		{
			metadata = null;
			error = "Metadata content is invalid.";
			return false;
		}
		return true;
	}

	public static bool HasTimestampChanged(long storedTicks, long currentTicks)
	{
		return storedTicks != currentTicks;
	}

	public static bool HasSourceChanged(ExcelLegendLinkMetadata metadata, long currentExcelTicks, long currentImageTicks, long currentImageFileSize)
	{
		if (metadata.ImageLastWriteTicks <= 0 || metadata.ImageFileSize < 0)
		{
			return true;
		}
		return metadata.LastWriteTicks != currentExcelTicks || metadata.ImageLastWriteTicks != currentImageTicks || metadata.ImageFileSize != currentImageFileSize;
	}

	public static string BuildLastWriteStamp(long excelTicks, long imageTicks, long imageSize)
	{
		return excelTicks.ToString(CultureInfo.InvariantCulture) + ";" + imageTicks.ToString(CultureInfo.InvariantCulture) + ";" + imageSize.ToString(CultureInfo.InvariantCulture);
	}

	public static bool TryParseLastWriteStamp(string stamp, out long excelTicks, out long imageTicks, out long imageSize)
	{
		excelTicks = 0L;
		imageTicks = 0L;
		imageSize = -1L;
		if (string.IsNullOrWhiteSpace(stamp))
		{
			return false;
		}
		string normalizedStamp = stamp.Trim();
		string[] parts = normalizedStamp.Split(';');
		if (parts.Length == 1)
		{
			if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out excelTicks))
			{
				return false;
			}
			imageTicks = 0L;
			imageSize = -1L;
			return true;
		}
		if (parts.Length != 3)
		{
			return false;
		}
		return long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out excelTicks) && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out imageTicks) && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out imageSize);
	}

	public static bool IsProviderMatch(View legend)
	{
		string value = legend.LookupParameter("VP_Excel_Provider")?.AsString() ?? string.Empty;
		if (string.Equals(value, "VP.ExcelLegendLink", StringComparison.Ordinal))
		{
			return true;
		}
		string legendName = legend.Name ?? string.Empty;
		return legendName.IndexOf("XLINK2|", StringComparison.Ordinal) >= 0;
	}

	public static bool IsProviderMatch(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		return text.IndexOf("VP.ExcelLegendLink", StringComparison.Ordinal) >= 0 || text.IndexOf("XLINK2|", StringComparison.Ordinal) >= 0;
	}

	public static bool TryRead(View legend, out ExcelLegendLinkMetadata? metadata, out string error)
	{
		metadata = null;
		error = string.Empty;
		string provider = legend.LookupParameter("VP_Excel_Provider")?.AsString() ?? string.Empty;
		string workbookPath = legend.LookupParameter("VP_Excel_WorkbookPath")?.AsString() ?? string.Empty;
		string scopedDefinedName = legend.LookupParameter("VP_Excel_ScopedDefinedName")?.AsString() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(scopedDefinedName))
		{
			string legacySheetName = legend.LookupParameter("VP_Excel_SheetName")?.AsString() ?? string.Empty;
			string legacyDefinedName = legend.LookupParameter("VP_Excel_DefinedName")?.AsString() ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(legacyDefinedName))
			{
				scopedDefinedName = BuildScopedDefinedName(legacySheetName, legacyDefinedName);
			}
		}
		string lastWriteTicksText = legend.LookupParameter("VP_Excel_LastWriteTicks")?.AsString() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(workbookPath) || string.IsNullOrWhiteSpace(scopedDefinedName) || string.IsNullOrWhiteSpace(lastWriteTicksText))
		{
			error = "Missing VP_Excel_* parameter values.";
			return false;
		}
		if (!string.Equals(provider, "VP.ExcelLegendLink", StringComparison.Ordinal))
		{
			error = "Provider value mismatch.";
			return false;
		}
		if (!TrySplitScopedDefinedName(scopedDefinedName, out string parsedSheetName, out string parsedDefinedName))
		{
			error = "Invalid VP_Excel_ScopedDefinedName value.";
			return false;
		}
		if (!TryParseLastWriteStamp(lastWriteTicksText, out var ticks, out var imageTicks, out var imageSize))
		{
			error = "Invalid VP_Excel_LastWriteTicks value.";
			return false;
		}
		metadata = new ExcelLegendLinkMetadata(provider, workbookPath, parsedSheetName, parsedDefinedName, ticks, imageTicks, imageSize);
		return true;
	}

	public static bool TryWrite(View legend, ExcelLegendLinkMetadata metadata, out string error)
	{
		error = string.Empty;
		if (!TrySet(legend, "VP_Excel_Provider", metadata.Provider, out error) || !TrySet(legend, "VP_Excel_WorkbookPath", metadata.WorkbookPath, out error) || !TrySet(legend, "VP_Excel_ScopedDefinedName", metadata.ScopedDefinedName, out error) || !TrySet(legend, "VP_Excel_LastWriteTicks", BuildLastWriteStamp(metadata.LastWriteTicks, metadata.ImageLastWriteTicks, metadata.ImageFileSize), out error))
		{
			return false;
		}
		return true;
	}

	public static bool TryClear(View legend, out string error)
	{
		error = string.Empty;
		string[] clearableParameterNames = ClearableParameterNames;
		foreach (string parameterName in clearableParameterNames)
		{
			if (!TrySetAllIfExists(legend, parameterName, string.Empty, out error))
			{
				return false;
			}
		}
		return true;
	}

	public static bool EnsureParameters(Document document, out string error)
	{
		error = string.Empty;
		if (HasAllParameters(document))
		{
			return true;
		}
		using Transaction tx = new Transaction(document, "Create VP_Excel parameters");
		tx.Start();
		try
		{
			string[] requiredParameterNames = RequiredParameterNames;
			foreach (string parameterName in requiredParameterNames)
			{
				if (!HasParameterBinding(document, parameterName))
				{
					string definitionError;
					Definition definition = GetOrCreateExternalDefinition(document, parameterName, out definitionError);
					if (definition == null)
					{
						tx.RollBack();
						error = definitionError;
						return false;
					}
					Category viewsCategory = Category.GetCategory(document, BuiltInCategory.OST_Views);
					CategorySet categorySet = document.Application.Create.NewCategorySet();
					categorySet.Insert(viewsCategory);
					InstanceBinding binding = document.Application.Create.NewInstanceBinding(categorySet);
					BindingMap map = document.ParameterBindings;
					bool inserted = map.Insert(definition, binding, BuiltInParameterGroup.PG_DATA);
					if (!inserted)
					{
						inserted = map.ReInsert(definition, binding, BuiltInParameterGroup.PG_DATA);
					}
					if (!inserted)
					{
						tx.RollBack();
						error = "Cannot insert binding for " + parameterName + ".";
						return false;
					}
				}
			}
			tx.Commit();
			return true;
		}
		catch (Exception ex)
		{
			if (tx.HasStarted())
			{
				tx.RollBack();
			}
			error = ex.Message;
			return false;
		}
	}

	public static string BuildCleanViewName(string currentName, string workbookPath, string definedName)
	{
		string baseName = StripLegacySuffix(currentName);
		string workbookName = Path.GetFileName(workbookPath) ?? "Workbook";
		string suffix = " VP_ExcelLink " + SanitizeToken(workbookName) + " " + SanitizeToken(definedName);
		string merged = (baseName + suffix).Trim();
		if (merged.Length > 240)
		{
			merged = merged.Substring(0, 240).TrimEnd();
		}
		return merged;
	}

	public static bool TryMigrateFromLegacy(View legend, out ExcelLegendLinkMetadata? metadata, out string error)
	{
		metadata = null;
		error = string.Empty;
		string legacyValue = legend.LookupParameter("VP_ExcelLink")?.AsString() ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(legacyValue) && TryParseLegacyParameter(legacyValue, out metadata, out error) && metadata != null)
		{
			return true;
		}
		return TryParseLegacyViewName(legend.Name ?? string.Empty, out metadata, out error);
	}

	public static bool TryParseFromViewName(string viewName, out ExcelLegendLinkMetadata? metadata, out string error)
	{
		return TryParseLegacyViewName(viewName, out metadata, out error);
	}

	private static bool HasAllParameters(Document document)
	{
		return RequiredParameterNames.All((string parameterName) => HasParameterBinding(document, parameterName));
	}

	private static bool HasParameterBinding(Document document, string parameterName)
	{
		BindingMap map = document.ParameterBindings;
		DefinitionBindingMapIterator iterator = map.ForwardIterator();
		iterator.Reset();
		while (iterator.MoveNext())
		{
			Definition definition = iterator.Key;
			if (string.Equals(definition.Name, parameterName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static Definition GetOrCreateExternalDefinition(Document document, string parameterName, out string error)
	{
		error = string.Empty;
		Autodesk.Revit.ApplicationServices.Application application = document.Application;
		string originalPath = application.SharedParametersFilename;
		string tempPath = Path.Combine(Path.GetTempPath(), "vp_excel_link_shared_params.txt");
		try
		{
			if (!File.Exists(tempPath))
			{
				File.WriteAllText(tempPath, "# VP_RevitAPI shared parameters");
			}
			application.SharedParametersFilename = tempPath;
			DefinitionFile definitionFile = application.OpenSharedParameterFile();
			if (definitionFile == null)
			{
				error = "Cannot open shared parameter file.";
				return null;
			}
			DefinitionGroup definitionGroup = definitionFile.Groups.get_Item("VP_RevitAPI") ?? definitionFile.Groups.Create("VP_RevitAPI");
			Definition definition = definitionGroup.Definitions.get_Item(parameterName);
			if (definition != null)
			{
				return definition;
			}
			ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(parameterName, SpecTypeId.String.Text);
			return definitionGroup.Definitions.Create(options);
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return null;
		}
		finally
		{
			application.SharedParametersFilename = originalPath;
		}
	}

	private static bool TrySet(View legend, string parameterName, string value, out string error)
	{
		error = string.Empty;
		Parameter parameter = legend.LookupParameter(parameterName);
		if (parameter == null || parameter.IsReadOnly)
		{
			error = "Parameter '" + parameterName + "' is not writable.";
			return false;
		}
		return parameter.Set(value ?? string.Empty);
	}

	private static bool TrySetAllIfExists(View legend, string parameterName, string value, out string error)
	{
		error = string.Empty;
		IList<Parameter> parameters = legend.GetParameters(parameterName);
		if (parameters.Count == 0)
		{
			return true;
		}
		foreach (Parameter parameter in parameters)
		{
			if (parameter.IsReadOnly)
			{
				error = "Parameter '" + parameterName + "' is not writable.";
				return false;
			}
			string targetValue = value ?? string.Empty;
			if (!parameter.Set(targetValue))
			{
				string currentValue = parameter.AsString() ?? string.Empty;
				if (!string.Equals(currentValue, targetValue, StringComparison.Ordinal))
				{
					error = "Parameter '" + parameterName + "' could not be cleared.";
					return false;
				}
			}
		}
		return true;
	}

	public static string BuildScopedDefinedName(string sheetName, string definedName)
	{
		string normalizedDefinedName = (definedName ?? string.Empty).Trim();
		if (normalizedDefinedName.Length == 0)
		{
			return string.Empty;
		}
		string normalizedSheetName = (sheetName ?? string.Empty).Trim();
		if (normalizedSheetName.Length == 0)
		{
			normalizedSheetName = "Workbook";
		}
		return normalizedSheetName + "_" + normalizedDefinedName;
	}

	public static bool TrySplitScopedDefinedName(string scopedDefinedName, out string sheetName, out string definedName)
	{
		sheetName = string.Empty;
		definedName = string.Empty;
		if (string.IsNullOrWhiteSpace(scopedDefinedName))
		{
			return false;
		}
		string normalized = scopedDefinedName.Trim();
		int separatorIndex = normalized.IndexOf('_');
		if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
		{
			return false;
		}
		sheetName = normalized.Substring(0, separatorIndex).Trim();
		definedName = normalized.Substring(separatorIndex + 1).Trim();
		if (sheetName.Length == 0 || definedName.Length == 0)
		{
			return false;
		}
		return true;
	}

	private static bool TryParseLegacyParameter(string value, out ExcelLegendLinkMetadata? metadata, out string error)
	{
		metadata = null;
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(value))
		{
			error = "Legacy parameter is empty.";
			return false;
		}
		Dictionary<string, string> map = ParseKeyValueTokens(value);
		if (!map.TryGetValue("wb", out var workbookToken) || !map.TryGetValue("dnh", out var hash) || !map.TryGetValue("ts", out var ticksText))
		{
			error = "Legacy parameter missing required tokens.";
			return false;
		}
		if (!long.TryParse(ticksText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
		{
			error = "Legacy parameter has invalid ticks.";
			return false;
		}
		string workbookName = Uri.UnescapeDataString(workbookToken);
		string definedName = hash;
		metadata = new ExcelLegendLinkMetadata("VP.ExcelLegendLink", workbookName, string.Empty, definedName, ticks, 0L, -1L);
		return true;
	}

	private static bool TryParseLegacyViewName(string viewName, out ExcelLegendLinkMetadata? metadata, out string error)
	{
		metadata = null;
		error = string.Empty;
		int markerIndex = viewName.IndexOf("XLINK2|", StringComparison.Ordinal);
		if (markerIndex < 0)
		{
			error = "No legacy metadata in view name.";
			return false;
		}
		string payload = viewName.Substring(markerIndex + "XLINK2|".Length);
		Dictionary<string, string> map = ParseKeyValueTokens(payload);
		if (!map.TryGetValue("wb", out var workbookToken) || !map.TryGetValue("dnh", out var hash) || !map.TryGetValue("ts", out var ticksText))
		{
			error = "Legacy view metadata missing required tokens.";
			return false;
		}
		if (!long.TryParse(ticksText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
		{
			error = "Legacy view metadata has invalid ticks.";
			return false;
		}
		metadata = new ExcelLegendLinkMetadata("VP.ExcelLegendLink", Uri.UnescapeDataString(workbookToken), string.Empty, hash, ticks, 0L, -1L);
		return true;
	}

	private static Dictionary<string, string> ParseKeyValueTokens(string value)
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] parts = value.Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries);
		string[] array = parts;
		foreach (string part in array)
		{
			int equalIndex = part.IndexOf('=');
			if (equalIndex > 0 && equalIndex < part.Length - 1)
			{
				string key = part.Substring(0, equalIndex);
				string tokenValue = part.Substring(equalIndex + 1);
				map[key] = tokenValue;
			}
		}
		return map;
	}

	private static string StripLegacySuffix(string viewName)
	{
		if (string.IsNullOrWhiteSpace(viewName))
		{
			return string.Empty;
		}
		int markerIndex = viewName.IndexOf("XLINK2|", StringComparison.Ordinal);
		if (markerIndex < 0)
		{
			return viewName.Trim();
		}
		return viewName.Substring(0, markerIndex).TrimEnd();
	}

	private static string SanitizeToken(string value)
	{
		string source = (value ?? string.Empty).Trim();
		if (source.Length == 0)
		{
			return "NA";
		}
		StringBuilder builder = new StringBuilder(source.Length);
		string text = source;
		foreach (char ch in text)
		{
			if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == ' ')
			{
				builder.Append(ch);
			}
			else
			{
				builder.Append('_');
			}
		}
		string normalized = builder.ToString();
		while (normalized.Contains("  "))
		{
			normalized = normalized.Replace("  ", " ");
		}
		return normalized.Trim();
	}

	[CompilerGenerated]
	private ExcelLegendLinkMetadata(ExcelLegendLinkMetadata original)
	{
		Provider = original.Provider;
		WorkbookPath = original.WorkbookPath;
		SheetName = original.SheetName;
		DefinedName = original.DefinedName;
		LastWriteTicks = original.LastWriteTicks;
		ImageLastWriteTicks = original.ImageLastWriteTicks;
		ImageFileSize = original.ImageFileSize;
	}
}
