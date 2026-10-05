using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper.Electrical;

namespace VP_RevitAPI.Internal.Electrical;

internal static class RewireCircuitRasterRunner
{
	private sealed class CircuitOrCircuitedElementFilter : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			if (elem is ElectricalSystem)
			{
				return true;
			}
			return ElectricalSystemUtils.GetElectricalSystems(elem) != null;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return false;
		}
	}

	private const string DialogTitle = "Wire Raster";

	private const double RowToleranceMillimeters = 100.0;

	internal static void Run(UIDocument uiDocument, Document document)
	{
		WireRasterDebugLog debugLog = WireRasterDebugLog.TryCreateOnDesktop();
		debugLog?.Log("WireRaster command started.");
		try
		{
			ElectricalSystem electricalSystem = TryResolveSelectedCircuit(uiDocument, document);
			if (electricalSystem == null)
			{
				debugLog?.Log("No unique circuit from selection. Asking user to pick one element/circuit.");
				Reference pickedReference = uiDocument.Selection.PickObject(ObjectType.Element, new CircuitOrCircuitedElementFilter(), "Pick a circuit or one element in the target circuit");
				Element pickedElement = document.GetElement(pickedReference);
				electricalSystem = TryResolveSingleCircuitFromElement(pickedElement);
				if (electricalSystem == null)
				{
					debugLog?.Log("Could not resolve exactly one circuit from picked element.");
					TaskDialog.Show("Wire Raster", BuildMessageWithLogPath("Cannot resolve exactly one circuit from picked element.", debugLog));
					return;
				}
			}
			debugLog?.Log($"Resolved circuit id={electricalSystem.Id.Value}");
			List<ElementId> orderedLoadIds = CircuitRasterOrderingService.GetRasterOrderedLoadIds(document, electricalSystem, 100.0, debugLog);
			debugLog?.Log($"Ordered load count: {orderedLoadIds.Count}");
			if (orderedLoadIds.Count <= 1)
			{
				TaskDialog.Show("Wire Raster", BuildMessageWithLogPath("Circuit has fewer than two loads to reorder.", debugLog));
				return;
			}
			View activeView = uiDocument.ActiveView;
			debugLog?.Log($"Active view id={activeView.Id.Value}, name='{activeView.Name}'");
			using TransactionGroup transactionGroup = new TransactionGroup(document, "Wire Raster");
			transactionGroup.Start();
			debugLog?.Log("TransactionGroup started.");
			using Transaction transaction = new Transaction(document, "Wire Raster");
			transaction.Start();
			debugLog?.Log("Transaction started.");
			CircuitWireRebuildResult rebuildResult = CircuitWireRebuildService.RebuildWiresBestEffort(document, activeView, electricalSystem, orderedLoadIds, debugLog);
			transaction.Commit();
			transactionGroup.Assimilate();
			debugLog?.Log("Transaction committed and TransactionGroup assimilated.");
			if (rebuildResult.FailedElementIds.Count == 0)
			{
				string successMessage = $"Wire raster completed. Deleted {rebuildResult.DeletedWireCount} old wires and created {rebuildResult.CreatedWireCount} wires.";
				TaskDialog.Show("Wire Raster", BuildMessageWithLogPath(successMessage, debugLog));
				return;
			}
			string failedIds = string.Join(", ", rebuildResult.FailedElementIds.Select((ElementId id) => id.Value.ToString()));
			string partialMessage = $"Wire raster completed with skipped pairs. Deleted {rebuildResult.DeletedWireCount} old wires, created {rebuildResult.CreatedWireCount} wires. Failed element ids: {failedIds}";
			TaskDialog.Show("Wire Raster", BuildMessageWithLogPath(partialMessage, debugLog));
		}
		finally
		{
			try
			{
				debugLog?.Log("WireRaster command finished.");
				debugLog?.Save();
			}
			catch
			{
			}
		}
	}

	private static string BuildMessageWithLogPath(string message, WireRasterDebugLog? debugLog)
	{
		if (debugLog == null)
		{
			return message;
		}
		return message + Environment.NewLine + Environment.NewLine + "Log: " + debugLog.FilePath;
	}

	private static ElectricalSystem? TryResolveSelectedCircuit(UIDocument uiDocument, Document document)
	{
		List<ElectricalSystem> circuits = (from element in uiDocument.Selection.GetElementIds().Select(document.GetElement)
			where element != null
			select element).SelectMany(GetCircuitCandidates).Distinct().ToList();
		if (circuits.Count == 1)
		{
			return circuits[0];
		}
		return null;
	}

	private static ElectricalSystem? TryResolveSingleCircuitFromElement(Element element)
	{
		List<ElectricalSystem> circuits = GetCircuitCandidates(element).Distinct().ToList();
		if (circuits.Count == 1)
		{
			return circuits[0];
		}
		return null;
	}

	private static IEnumerable<ElectricalSystem> GetCircuitCandidates(Element element)
	{
		if (element is ElectricalSystem electricalSystem)
		{
			yield return electricalSystem;
			yield break;
		}
		ISet<ElectricalSystem> electricalSystems = ElectricalSystemUtils.GetElectricalSystems(element);
		if (electricalSystems == null)
		{
			yield break;
		}
		foreach (ElectricalSystem item in electricalSystems)
		{
			yield return item;
		}
	}
}
