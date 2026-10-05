using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Commands.HVAC;

[Transaction(TransactionMode.Manual)]
public class Command_ConnectWithTap : SecuredExternalCommand
{
	private sealed class DuctSelectionFilter : ISelectionFilter
	{
		public ElementId? PreviousElementId { get; set; }

		public bool AllowElement(Element element)
		{
			return element is Duct && (PreviousElementId == null || element.Id != PreviousElementId);
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	protected override void ExecuteCore()
	{
		try
		{
			DuctSelectionFilter filter = new DuctSelectionFilter();
			Reference mainReference = base.UiDocument.Selection.PickObject(ObjectType.Element, filter, "Chọn ống gió chính");
			Element mainElement = base.Document.GetElement(mainReference);
			filter.PreviousElementId = mainElement.Id;
			Reference branchReference = base.UiDocument.Selection.PickObject(ObjectType.Element, filter, "Chọn ống gió nhánh");
			Element branchElement = base.Document.GetElement(branchReference);
			if (!(mainElement is MEPCurve mainCurveOwner) || !(mainElement.Location is LocationCurve mainLocation))
			{
				TaskDialog.Show("ConnectWithTap", "Ống gió chính không hợp lệ.");
				return;
			}
			List<Connector> branchConnectors = ConnectorUtilities.GetConnectors(branchElement) ?? new List<Connector>();
			List<Connector> endConnectors = branchConnectors.Where((Connector connector) => connector.ConnectorType == ConnectorType.End).ToList();
			if (endConnectors.Count == 0)
			{
				TaskDialog.Show("ConnectWithTap", "Ống gió nhánh không có đầu connector hợp lệ.");
				return;
			}
			List<Connector> candidates = endConnectors.Where((Connector connector) => !connector.IsConnected).ToList();
			if (candidates.Count == 0)
			{
				candidates = endConnectors;
			}
			Curve mainCurve = mainLocation.Curve;
			Connector branchConnector = candidates.OrderBy((Connector connector) => mainCurve.Distance(connector.Origin)).First();
			using Transaction transaction = new Transaction(base.Document, "Kết nối ống gió bằng tap");
			transaction.Start();
			base.Document.Create.NewTakeoffFitting(branchConnector, mainCurveOwner);
			transaction.Commit();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("ConnectWithTap", ex2.ToString());
		}
	}
}
