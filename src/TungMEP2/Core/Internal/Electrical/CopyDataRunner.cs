using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Electrical;

internal static class CopyDataRunner
{
	private struct ParameterValue
	{
		internal StorageType StorageType { get; }

		internal string? StringValue { get; }

		internal int IntegerValue { get; }

		internal double DoubleValue { get; }

		internal ElementId ElementIdValue { get; }

		internal ParameterValue(StorageType storageType, string? stringValue, int integerValue, double doubleValue, ElementId elementIdValue)
		{
			StorageType = storageType;
			StringValue = stringValue;
			IntegerValue = integerValue;
			DoubleValue = doubleValue;
			ElementIdValue = elementIdValue;
		}
	}

	private const string DialogTitle = "Copy Data";

	private static readonly string[] ParameterNames = new string[3] { "E_Area", "E_Wire", "E_LoadName" };

	internal static void Run(UIDocument uiDocument, Document document)
	{
		Reference sourceReference = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.ElectricalDataSource(), "Pick source element to copy E_Area, E_Wire, E_LoadName");
		Element sourceElement = document.GetElement(sourceReference);
		if (!TryGetSourceValues(sourceElement, out Dictionary<string, ParameterValue> sourceValues, out string sourceError))
		{
			TaskDialog.Show("Copy Data", sourceError);
			return;
		}
		IList<Reference> targetReferences = uiDocument.Selection.PickObjects(ObjectType.Element, new Isf.ElectricalDataSource(), "Pick target elements to copy data");
		List<Element> targetElements = (from reference in targetReferences
			select document.GetElement(reference) into element
			where element != null
			select element).ToList();
		if (targetElements.Count == 0)
		{
			return;
		}
		List<string> issues = new List<string>();
		using Transaction transaction = new Transaction(document, "Copy Data");
		transaction.Start();
		foreach (Element targetElement in targetElements)
		{
			string[] parameterNames = ParameterNames;
			foreach (string parameterName in parameterNames)
			{
				if (sourceValues.TryGetValue(parameterName, out var sourceValue))
				{
					Parameter targetParameter = targetElement.LookupParameter(parameterName);
					string setError;
					if (targetParameter == null)
					{
						issues.Add($"Element {targetElement.Id.Value}: missing parameter {parameterName}.");
					}
					else if (targetParameter.IsReadOnly)
					{
						issues.Add($"Element {targetElement.Id.Value}: parameter {parameterName} is read-only.");
					}
					else if (!TrySetTargetValue(targetParameter, sourceValue, out setError))
					{
						issues.Add($"Element {targetElement.Id.Value}: {parameterName} failed - {setError}");
					}
				}
			}
		}
		transaction.Commit();
		if (issues.Count != 0)
		{
			string message = "Copy completed with issues:" + Environment.NewLine + string.Join(Environment.NewLine, issues);
			TaskDialog.Show("Copy Data", message);
		}
	}

	private static bool TryGetSourceValues(Element? sourceElement, out Dictionary<string, ParameterValue> values, out string errorMessage)
	{
		values = new Dictionary<string, ParameterValue>(StringComparer.Ordinal);
		if (sourceElement == null)
		{
			errorMessage = "Source element is not valid.";
			return false;
		}
		string[] parameterNames = ParameterNames;
		foreach (string parameterName in parameterNames)
		{
			Parameter parameter = sourceElement.LookupParameter(parameterName);
			if (parameter != null && TryGetParameterValue(parameter, out ParameterValue value, out string _))
			{
				values[parameterName] = value;
			}
		}
		if (values.Count == 0)
		{
			errorMessage = "Source không có dữ liệu để copy.";
			return false;
		}
		errorMessage = string.Empty;
		return true;
	}

	private static bool TryGetParameterValue(Parameter parameter, out ParameterValue value, out string errorMessage)
	{
		if (!parameter.HasValue)
		{
			value = default(ParameterValue);
			errorMessage = "value is empty.";
			return false;
		}
		switch (parameter.StorageType)
		{
		case StorageType.String:
		{
			string stringValue = parameter.AsString();
			if (string.IsNullOrWhiteSpace(stringValue))
			{
				value = default(ParameterValue);
				errorMessage = "value is empty.";
				return false;
			}
			value = new ParameterValue(StorageType.String, stringValue, 0, 0.0, ElementId.InvalidElementId);
			errorMessage = string.Empty;
			return true;
		}
		case StorageType.Integer:
			value = new ParameterValue(StorageType.Integer, null, parameter.AsInteger(), 0.0, ElementId.InvalidElementId);
			errorMessage = string.Empty;
			return true;
		case StorageType.Double:
			value = new ParameterValue(StorageType.Double, null, 0, parameter.AsDouble(), ElementId.InvalidElementId);
			errorMessage = string.Empty;
			return true;
		case StorageType.ElementId:
			value = new ParameterValue(StorageType.ElementId, null, 0, 0.0, parameter.AsElementId());
			errorMessage = string.Empty;
			return true;
		default:
			value = default(ParameterValue);
			errorMessage = $"unsupported storage type {parameter.StorageType}.";
			return false;
		}
	}

	private static bool TrySetTargetValue(Parameter parameter, ParameterValue sourceValue, out string errorMessage)
	{
		if (parameter.StorageType != sourceValue.StorageType)
		{
			errorMessage = $"storage type mismatch (target {parameter.StorageType}, source {sourceValue.StorageType}).";
			return false;
		}
		try
		{
			switch (sourceValue.StorageType)
			{
			case StorageType.String:
				parameter.Set(sourceValue.StringValue ?? string.Empty);
				break;
			case StorageType.Integer:
				parameter.Set(sourceValue.IntegerValue);
				break;
			case StorageType.Double:
				parameter.Set(sourceValue.DoubleValue);
				break;
			case StorageType.ElementId:
				parameter.Set(sourceValue.ElementIdValue);
				break;
			default:
				errorMessage = $"unsupported storage type {sourceValue.StorageType}.";
				return false;
			}
		}
		catch (Exception ex)
		{
			errorMessage = ex.Message;
			return false;
		}
		errorMessage = string.Empty;
		return true;
	}
}
