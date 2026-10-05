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

internal static class RerouteUnderOverMultipleRunner
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

	private const string DialogTitle = "Reroute Under/Over Multiple";

	private const string OffsetStoreKey = "RerouteUnderOver.OffsetMm";

	private const string AngleStoreKey = "RerouteUnderOver.AngleDeg";

	internal static void Run(UIDocument uiDocument, Document document)
	{
		IList<Reference> references = uiDocument.Selection.PickObjects(ObjectType.Element, new SupportedMepCurveFilter(), "Select elements to reroute");
		List<Element> targetElements = (from element in references.Select((Reference reference) => document.GetElement(reference.ElementId)).Where(RerouteUnderOverService.IsSupportedStraightMepCurve)
			group element by element.Id into source
			select source.First()).ToList();
		if (targetElements.Count == 0)
		{
			return;
		}
		XYZ startPoint = uiDocument.Selection.PickPoint("Select start point of reroute");
		XYZ endPoint = uiDocument.Selection.PickPoint("Select end point of reroute");
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
		using (TransactionGroup group = new TransactionGroup(document, "Reroute Under/Over Multiple"))
		{
			group.Start();
			foreach (Element target in targetElements)
			{
				using Transaction transaction = new Transaction(document, "Reroute Under/Over Multiple");
				transaction.Start();
				if (!RerouteUnderOverService.TryReroute(document, target, startPoint, endPoint, window.SelectedOffsetMillimeters, window.SelectedAngleDegrees, out string error))
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
			TaskDialog.Show("Reroute Under/Over Multiple", $"Completed with failures: {failures.Count}/{targetElements.Count}.{Environment.NewLine}{details}");
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
