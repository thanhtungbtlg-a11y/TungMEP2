using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper.COMBINE;
using VP_RevitAPI.State;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Combine;

internal static class RerouteUnderOverRunner
{
	private sealed class SupportedMepCurveFilter : ISelectionFilter
	{
		public bool AllowElement(Element element)
		{
			return RerouteUnderOverService.IsSupportedStraightMepCurve(element);
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	private sealed class FixedElementFilter : ISelectionFilter
	{
		private readonly ElementId _elementId;

		public FixedElementFilter(ElementId elementId)
		{
			_elementId = elementId;
		}

		public bool AllowElement(Element element)
		{
			return element != null && element.Id == _elementId;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	private const string DialogTitle = "Reroute Under/Over";

	private const string OffsetStoreKey = "RerouteUnderOver.OffsetMm";

	private const string AngleStoreKey = "RerouteUnderOver.AngleDeg";

	internal static void Run(UIDocument uiDocument, Document document)
	{
		List<Element> targetElements = uiDocument.Selection.GetElementIds().Select(document.GetElement).Where(RerouteUnderOverService.IsSupportedStraightMepCurve)
			.ToList();
		XYZ breakPoint1;
		XYZ breakPoint2;
		if (targetElements.Count == 0)
		{
			Reference firstReference = uiDocument.Selection.PickObject(ObjectType.PointOnElement, new SupportedMepCurveFilter(), "Select element and first break point");
			Element firstElement = document.GetElement(firstReference);
			targetElements.Add(firstElement);
			breakPoint1 = firstReference.GlobalPoint;
			Reference secondReference = uiDocument.Selection.PickObject(ObjectType.PointOnElement, new FixedElementFilter(firstElement.Id), "Select second break point");
			breakPoint2 = secondReference.GlobalPoint;
		}
		else
		{
			Element primary = targetElements[0];
			breakPoint1 = uiDocument.Selection.PickObject(ObjectType.PointOnElement, new FixedElementFilter(primary.Id), "Select first break point").GlobalPoint;
			breakPoint2 = uiDocument.Selection.PickObject(ObjectType.PointOnElement, new FixedElementFilter(primary.Id), "Select second break point").GlobalPoint;
		}
		List<double> allowedAngles = new List<double> { 45.0, 90.0 };
		double defaultOffset = GetStoredValue("RerouteUnderOver.OffsetMm", 300.0);
		double defaultAngle = GetStoredValue("RerouteUnderOver.AngleDeg", 45.0);
		if (RerouteUnderOverService.TryGetSuggestedVerticalOffsetMillimeters(targetElements, out var suggestedOffset))
		{
			defaultOffset = suggestedOffset;
		}
		UI_RerouteUnderOverWindow window = new UI_RerouteUnderOverWindow(allowedAngles, defaultOffset, defaultAngle);
		if (window.ShowDialog() != true)
		{
			return;
		}
		StaticStore.Bag["RerouteUnderOver.OffsetMm"] = window.SelectedOffsetMillimeters;
		StaticStore.Bag["RerouteUnderOver.AngleDeg"] = window.SelectedAngleDegrees;
		List<string> failures = new List<string>();
		using (TransactionGroup group = new TransactionGroup(document, "Reroute Under/Over"))
		{
			group.Start();
			foreach (Element target in targetElements)
			{
				using Transaction transaction = new Transaction(document, "Reroute Under/Over");
				transaction.Start();
				if (!RerouteUnderOverService.TryReroute(document, target, breakPoint1, breakPoint2, window.SelectedOffsetMillimeters, window.SelectedAngleDegrees, out string error))
				{
					transaction.RollBack();
					failures.Add($"{target.Id.IntegerValue}: {error}");
				}
				else
				{
					transaction.Commit();
				}
			}
			group.Assimilate();
		}
		if (failures.Count > 0)
		{
			string details = string.Join(Environment.NewLine, failures.Take(8));
			TaskDialog.Show("Reroute Under/Over", $"Completed with failures: {failures.Count}/{targetElements.Count}.{Environment.NewLine}{details}");
		}
	}

	private static double GetStoredValue(string key, double fallback)
	{
		if (!StaticStore.Bag.TryGetValue(key, out object value) || value == null)
		{
			return fallback;
		}
		if (value is double number)
		{
			return number;
		}
		return fallback;
	}
}
