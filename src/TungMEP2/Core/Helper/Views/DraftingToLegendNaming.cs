using System;
using System.Collections.Generic;

namespace VP_RevitAPI.Helper.Views;

public static class DraftingToLegendNaming
{
	public const string DefaultConvertedSuffix = " (Converted from Drafting)";

	public static string BuildUniqueLegendName(ISet<string> existingLegendNames, string desiredName, string? convertedSuffix = null)
	{
		if (existingLegendNames == null)
		{
			throw new ArgumentNullException("existingLegendNames");
		}
		string desired = Normalize(desiredName);
		if (string.IsNullOrWhiteSpace(desired))
		{
			desired = "unnamed";
		}
		if (existingLegendNames.Add(desired))
		{
			return desired;
		}
		string suffix = ((convertedSuffix != null && convertedSuffix.Trim().Length != 0) ? convertedSuffix.Trim() : " (Converted from Drafting)");
		string baseCandidate = desired + suffix;
		if (existingLegendNames.Add(baseCandidate))
		{
			return baseCandidate;
		}
		for (int i = 2; i <= 999; i++)
		{
			string candidate = $"{baseCandidate} ({i})";
			if (existingLegendNames.Add(candidate))
			{
				return candidate;
			}
		}
		string fallback = baseCandidate + " (999+)";
		existingLegendNames.Add(fallback);
		return fallback;
	}

	private static string Normalize(string? value)
	{
		return (value ?? string.Empty).Trim();
	}
}
