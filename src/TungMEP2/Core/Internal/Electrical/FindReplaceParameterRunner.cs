using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VP_RevitAPI.State;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Electrical;

internal static class FindReplaceParameterRunner
{
	private const string DialogTitle = "FindReplaceParam";

	private const string TransactionName = "Find Replace Parameter";

	private const string FindStoreKey = "FindReplaceParam.Find";

	private const string ReplaceStoreKey = "FindReplaceParam.Replace";

	internal static void Run(UIDocument uiDocument, Document document)
	{
		List<Element> selectedElements = (from element2 in uiDocument.Selection.GetElementIds().Select(document.GetElement)
			where element2 != null
			group element2 by element2.Id into @group
			select @group.First()).ToList();
		if (selectedElements.Count == 0)
		{
			TaskDialog.Show("FindReplaceParam", "Chọn ít nhất một element trước khi chạy lệnh.");
			return;
		}
		string defaultFind = GetStoredText("FindReplaceParam.Find");
		string defaultReplace = GetStoredText("FindReplaceParam.Replace");
		UI_FindReplaceParameterWindow window = new UI_FindReplaceParameterWindow(selectedElements.Count, defaultFind, defaultReplace);
		if (window.ShowDialog() != true)
		{
			return;
		}
		string findText = window.FindText;
		string replaceText = window.ReplaceText;
		if (string.IsNullOrEmpty(findText))
		{
			TaskDialog.Show("FindReplaceParam", "Find không được để trống.");
			return;
		}
		StaticStore.Bag["FindReplaceParam.Find"] = findText;
		StaticStore.Bag["FindReplaceParam.Replace"] = replaceText;
		int eligibleParameterCount = 0;
		int changedParameterCount = 0;
		List<string> issues = new List<string>();
		using (Transaction transaction = new Transaction(document, "Find Replace Parameter"))
		{
			transaction.Start();
			foreach (Element element in selectedElements)
			{
				foreach (Parameter parameter in element.Parameters)
				{
					if (!CanEditParameter(element, parameter))
					{
						continue;
					}
					eligibleParameterCount++;
					string currentValue = parameter.AsString() ?? string.Empty;
					if (currentValue.IndexOf(findText, StringComparison.Ordinal) < 0)
					{
						continue;
					}
					string replacedValue = currentValue.Replace(findText, replaceText);
					if (string.Equals(currentValue, replacedValue, StringComparison.Ordinal))
					{
						continue;
					}
					try
					{
						if (parameter.Set(replacedValue))
						{
							changedParameterCount++;
						}
						else
						{
							issues.Add($"Element {element.Id.Value}: cannot set parameter {GetParameterName(parameter)}.");
						}
					}
					catch (Exception ex)
					{
						issues.Add($"Element {element.Id.Value}: {GetParameterName(parameter)} failed - {ex.Message}");
					}
				}
			}
			transaction.Commit();
		}
		if (issues.Count != 0 || changedParameterCount <= 0)
		{
			if (issues.Count == 0 && (eligibleParameterCount == 0 || changedParameterCount == 0))
			{
				TaskDialog.Show("FindReplaceParam", "Không có parameter string instance phù hợp để thay thế.");
				return;
			}
			string detail = string.Join(Environment.NewLine, issues.Take(8));
			TaskDialog.Show("FindReplaceParam", $"Thay thế hoàn tất với lỗi: {issues.Count}\nĐã thay: {changedParameterCount}\n{detail}");
		}
	}

	private static bool CanEditParameter(Element element, Parameter parameter)
	{
		if (parameter == null)
		{
			return false;
		}
		if (parameter.StorageType != StorageType.String || parameter.IsReadOnly)
		{
			return false;
		}
		return parameter.Element?.Id == element.Id;
	}

	private static string GetStoredText(string key)
	{
		if (!StaticStore.Bag.TryGetValue(key, out object value) || value == null)
		{
			return string.Empty;
		}
		if (value is string text)
		{
			return text;
		}
		return value.ToString() ?? string.Empty;
	}

	private static string GetParameterName(Parameter parameter)
	{
		string name = parameter.Definition?.Name ?? string.Empty;
		return string.IsNullOrWhiteSpace(name) ? "(Unnamed)" : name.Trim();
	}
}
