using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Layout.AutoDimFamilyInstancePoint;

internal sealed class AutoDimLinkResolver
{
	private sealed record LinkScored(RevitLinkInstance Link, int ArcScore, int StrScore);

	private const string ArcPrimaryKeyword = "arc";

	private const string StrPrimaryKeyword = "str";

	private static readonly string[] ArcFallbackKeywords = new string[3] { "arch", "architecture", "kt" };

	private static readonly string[] StrFallbackKeywords = new string[4] { "struct", "structure", "ketcau", "kc" };

	public AutoDimLinkResolution Resolve(IReadOnlyList<RevitLinkInstance> links)
	{
		if (links.Count == 0)
		{
			return new AutoDimLinkResolution(null, null, "Không tìm thấy RevitLinkInstance đã load.");
		}
		List<LinkScored> scored = links.Select(delegate(RevitLinkInstance link)
		{
			string normalizedName = NormalizeLinkName(link);
			int arcScore = Score(normalizedName, "arc", ArcFallbackKeywords);
			int strScore = Score(normalizedName, "str", StrFallbackKeywords);
			return new LinkScored(link, arcScore, strScore);
		}).ToList();
		RevitLinkInstance bestArc = PickBest(scored, isArc: true);
		RevitLinkInstance bestStr = PickBest(scored, isArc: false);
		List<string> messageParts = new List<string>();
		if (bestArc == null)
		{
			messageParts.Add("Không nhận diện được ARC link theo tên.");
		}
		if (bestStr == null)
		{
			messageParts.Add("Không nhận diện được STR link theo tên.");
		}
		if (bestArc != null && bestStr != null && bestArc.Id == bestStr.Id)
		{
			messageParts.Add("ARC và STR đang trùng link, vui lòng chọn lại thủ công.");
			return new AutoDimLinkResolution(bestArc, null, string.Join(" ", messageParts));
		}
		string message = ((messageParts.Count == 0) ? "Đã tự nhận diện ARC/STR theo tên link." : string.Join(" ", messageParts));
		return new AutoDimLinkResolution(bestArc, bestStr, message);
	}

	private static RevitLinkInstance? PickBest(IEnumerable<LinkScored> scored, bool isArc)
	{
		int maxScore = (isArc ? scored.Max((LinkScored item) => item.ArcScore) : scored.Max((LinkScored item) => item.StrScore));
		if (maxScore <= 0)
		{
			return null;
		}
		List<LinkScored> top = scored.Where((LinkScored item) => (isArc ? item.ArcScore : item.StrScore) == maxScore).ToList();
		if (top.Count != 1)
		{
			return null;
		}
		return top[0].Link;
	}

	private static int Score(string normalizedName, string primaryKeyword, IEnumerable<string> fallbackKeywords)
	{
		string[] tokens = normalizedName.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		int score = 0;
		if (tokens.Contains<string>(primaryKeyword, StringComparer.OrdinalIgnoreCase))
		{
			score += 100;
		}
		else if (normalizedName.AsSpan().Contains(primaryKeyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
		{
			score += 40;
		}
		foreach (string keyword in fallbackKeywords)
		{
			if (tokens.Contains(keyword, StringComparer.OrdinalIgnoreCase))
			{
				score += 5;
			}
			else if (normalizedName.AsSpan().Contains(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
			{
				score += 2;
			}
		}
		return score;
	}

	private static string NormalizeLinkName(RevitLinkInstance link)
	{
		string rawName = link.GetLinkDocument()?.Title ?? link.Name;
		if (rawName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
		{
			rawName = rawName.Substring(0, rawName.Length - 4);
		}
		StringBuilder builder = new StringBuilder(rawName.Length);
		string text = rawName;
		foreach (char character in text)
		{
			builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
		}
		return builder.ToString();
	}
}
