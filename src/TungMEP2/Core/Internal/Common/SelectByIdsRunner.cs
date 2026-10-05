using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Common;

internal static class SelectByIdsRunner
{
	private sealed class ParseResult
	{
		internal int TotalInputLines { get; }

		internal int InvalidLineCount { get; }

		internal IReadOnlyList<int> ValidIds { get; }

		internal ParseResult(int totalInputLines, int invalidLineCount, IReadOnlyList<int> validIds)
		{
			TotalInputLines = totalInputLines;
			InvalidLineCount = invalidLineCount;
			ValidIds = validIds ?? Array.Empty<int>();
		}
	}

	private const string DialogTitle = "Select By IDs";

	internal static void Run(UIDocument uiDocument, Document document)
	{
		try
		{
			UI_SelectByIdsWindow window = new UI_SelectByIdsWindow();
			if (window.ShowDialog() != true)
			{
				return;
			}
			string rawInput = window.RawInput;
			ParseResult parsed = ParseInput(rawInput);
			if (parsed.ValidIds.Count == 0)
			{
				TaskDialog.Show("Select By IDs", BuildSummaryMessage(parsed, 0, 0));
				return;
			}
			List<ElementId> foundIds = new List<ElementId>();
			int missingCount = 0;
			foreach (int id in parsed.ValidIds)
			{
				ElementId elementId = new ElementId(id);
				if (document.GetElement(elementId) == null)
				{
					missingCount++;
				}
				else
				{
					foundIds.Add(elementId);
				}
			}
			if (foundIds.Count > 0)
			{
				uiDocument.Selection.SetElementIds(foundIds);
			}
			if (missingCount > 0 || parsed.InvalidLineCount > 0 || foundIds.Count == 0)
			{
				TaskDialog.Show("Select By IDs", BuildSummaryMessage(parsed, foundIds.Count, missingCount));
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Select By IDs", ex2.ToString());
		}
	}

	private static ParseResult ParseInput(string rawInput)
	{
		string normalizedInput = rawInput ?? string.Empty;
		string[] lines = normalizedInput.Split(new string[3] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
		List<int> validIds = new List<int>();
		int invalidCount = 0;
		int nonEmptyLineCount = 0;
		string[] array = lines;
		foreach (string line in array)
		{
			string trimmedLine = (line ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(trimmedLine))
			{
				continue;
			}
			nonEmptyLineCount++;
			string[] tokens = trimmedLine.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			bool lineHasInvalidToken = false;
			string[] array2 = tokens;
			foreach (string token in array2)
			{
				if (!int.TryParse(token, out var parsedId) || parsedId <= 0)
				{
					lineHasInvalidToken = true;
				}
				else
				{
					validIds.Add(parsedId);
				}
			}
			if (lineHasInvalidToken)
			{
				invalidCount++;
			}
		}
		List<int> distinctValidIds = validIds.Distinct().ToList();
		return new ParseResult(nonEmptyLineCount, invalidCount, distinctValidIds);
	}

	private static string BuildSummaryMessage(ParseResult parsed, int selectedCount, int missingCount)
	{
		return $"Tổng dòng nhập: {parsed.TotalInputLines}" + $"\nID hợp lệ (unique): {parsed.ValidIds.Count}" + $"\nĐã chọn: {selectedCount}" + $"\nID không tồn tại: {missingCount}" + $"\nDòng lỗi bị bỏ qua: {parsed.InvalidLineCount}";
	}
}
