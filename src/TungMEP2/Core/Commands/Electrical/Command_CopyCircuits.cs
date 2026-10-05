using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Electrical;

namespace VP_RevitAPI.Commands.Electrical;

[Transaction(TransactionMode.Manual)]
public class Command_CopyCircuits : SecuredExternalCommand
{
	private sealed record CircuitCopy(ElementId CircuitId, List<ElementId> ElementIds);

	private sealed record PanelCircuitCopy(ElementId PanelId, List<CircuitCopy> Circuits);

	protected override void ExecuteCore()
	{
		try
		{
			List<Element> sourceSelection = (from id in base.UiDocument.Selection.GetElementIds()
				select base.Document.GetElement(id) into c
				where c != null
				where ElectricalSystemUtils.GetElectricalSystems(c) != null || ElectricalSystemUtils.IsElectricalEquipment(c)
				select c).ToList();
			IList<Element> sourceElements = (sourceSelection.Any() ? sourceSelection : (from r in base.UiDocument.Selection.PickObjects(ObjectType.Element, new CircuitedElementFilter(), "Pick a Panel or circuited element")
				select base.Document.GetElement(r.ElementId) into e
				where e != null
				select e).ToList());
			List<FamilyInstance> selectedPanels = sourceElements.Where(ElectricalSystemUtils.IsElectricalEquipment).OfType<FamilyInstance>().ToList();
			List<Element> selectedNonPanels = sourceElements.Where((Element e) => !ElectricalSystemUtils.IsElectricalEquipment(e)).ToList();
			List<PanelCircuitCopy> panelCopies = BuildPanelCopies(selectedPanels);
			List<PanelCircuitCopy> samePanelCircuitCopies = BuildSamePanelCopies(selectedNonPanels, panelCopies);
			XYZ translation = PickTranslationVector();
			int copiedPanels = 0;
			int copiedPanelCircuits = 0;
			int copiedSamePanelCircuits = 0;
			using TransactionGroup transactionGroup = new TransactionGroup(base.Document, "Copy Circuits");
			transactionGroup.Start();
			foreach (PanelCircuitCopy panelCopy in panelCopies)
			{
				FamilyInstance newPanel = null;
				PanelScheduleView sourcePanelSchedule = null;
				PanelScheduleView newPanelSchedule = null;
				using (Transaction transaction = new Transaction(base.Document, "Create Panels"))
				{
					try
					{
						transaction.Start();
						Element sourcePanel = base.Document.GetElement(panelCopy.PanelId);
						if (sourcePanel == null)
						{
							transactionGroup.RollBack();
							return;
						}
						newPanel = (from id in ElementTransformUtils.CopyElement(base.Document, sourcePanel.Id, translation)
							select base.Document.GetElement(id)).OfType<FamilyInstance>().FirstOrDefault();
						if (newPanel == null)
						{
							TaskDialog.Show("Error", "New panel cannot be created from source panel " + sourcePanel.Name);
							transactionGroup.RollBack();
							return;
						}
						Parameter panelNameParameter = newPanel.LookupParameter("Panel Name");
						if (panelNameParameter != null && !panelNameParameter.IsReadOnly)
						{
							panelNameParameter.Set(sourcePanel.Name + "_Copy");
						}
						sourcePanelSchedule = new FilteredElementCollector(base.Document).OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>().FirstOrDefault((PanelScheduleView x) => x.GetPanel() == sourcePanel.Id);
						if (sourcePanelSchedule == null)
						{
							TaskDialog.Show("Error", "Source Panel must have Panel Schedule");
							transactionGroup.RollBack();
							return;
						}
						copiedPanels++;
						newPanelSchedule = PanelScheduleView.CreateInstanceView(base.Document, sourcePanelSchedule.GetTemplate(), newPanel.Id);
						if (newPanelSchedule == null)
						{
							TaskDialog.Show("Error", "Cannot create new panel schedule from source panel schedule");
							transactionGroup.RollBack();
							return;
						}
						transaction.Commit();
					}
					catch (Exception ex)
					{
						TaskDialog.Show("Warning", ex.Message);
						transactionGroup.RollBack();
						return;
					}
				}
				foreach (CircuitCopy circuitCopy in panelCopy.Circuits)
				{
					if (!(base.Document.GetElement(circuitCopy.CircuitId) is ElectricalSystem sourceCircuit))
					{
						continue;
					}
					List<ElementId> sourceMemberIds = (from e in circuitCopy.ElementIds.Select(base.Document.GetElement)
						where e != null
						select e.Id).ToList();
					ElectricalSystemType systemType = sourceCircuit.SystemType;
					ElectricalSystem newCircuit = null;
					if (sourceMemberIds.Select((ElementId x) => x.IntegerValue).Contains(panelCopy.PanelId.IntegerValue))
					{
						continue;
					}
					using (Transaction transaction2 = new Transaction(base.Document, "Create Circuits"))
					{
						transaction2.Start();
						try
						{
							List<ElementId> copiedElementIds = ElementTransformUtils.CopyElements(base.Document, sourceMemberIds, translation).ToList();
							newCircuit = ElectricalSystem.Create(base.Document, copiedElementIds, systemType);
							if (newCircuit == null)
							{
								TaskDialog.Show("Error", "New circuit cannot be created from source circuit " + sourceCircuit.CircuitNumber);
								transactionGroup.RollBack();
								return;
							}
							newCircuit.Rating = sourceCircuit.Rating;
							newCircuit.SelectPanel(newPanel);
							newCircuit.LoadName = sourceCircuit.LoadName;
							copiedPanelCircuits++;
						}
						catch (Exception ex2)
						{
							TaskDialog.Show("Error", ex2.Message);
							transactionGroup.RollBack();
							return;
						}
						transaction2.Commit();
					}
					using Transaction transaction3 = new Transaction(base.Document, "Move Circuits");
					transaction3.Start();
					try
					{
						int sourceRow = 0;
						int sourceCol = 0;
						for (int row = 1; row < sourcePanelSchedule.GetTableData().NumberOfSlots; row++)
						{
							for (int col = 1; col < sourcePanelSchedule.GetTableData().NumberOfSlots; col++)
							{
								ElectricalSystem cellCircuit = sourcePanelSchedule.GetCircuitByCell(row, col);
								if (cellCircuit != null && cellCircuit.CircuitNumber == sourceCircuit.CircuitNumber)
								{
									sourceRow = row;
									sourceCol = col;
									break;
								}
							}
						}
						int targetRow = 0;
						int targetCol = 0;
						for (int row2 = 1; row2 < newPanelSchedule.GetTableData().NumberOfSlots; row2++)
						{
							for (int col2 = 1; col2 < newPanelSchedule.GetTableData().NumberOfSlots; col2++)
							{
								ElectricalSystem cellCircuit2 = newPanelSchedule.GetCircuitByCell(row2, col2);
								if (cellCircuit2 != null && cellCircuit2.CircuitNumber == newCircuit.CircuitNumber)
								{
									targetRow = row2;
									targetCol = col2;
									break;
								}
							}
						}
						if (targetRow > 0 && targetCol > 0 && sourceRow > 0 && sourceCol > 0 && (targetRow != sourceRow || targetCol != sourceCol))
						{
							newPanelSchedule.MoveSlotTo(targetRow, targetCol, sourceRow, sourceCol);
						}
					}
					catch (Exception ex3)
					{
						TaskDialog.Show("Error", ex3.Message);
						transactionGroup.RollBack();
						return;
					}
					transaction3.Commit();
				}
			}
			using (Transaction transaction4 = new Transaction(base.Document, "Copy Circuits"))
			{
				transaction4.Start();
				try
				{
					foreach (PanelCircuitCopy panelGroup in samePanelCircuitCopies)
					{
						if (!(base.Document.GetElement(panelGroup.PanelId) is FamilyInstance panel))
						{
							continue;
						}
						foreach (CircuitCopy circuit in panelGroup.Circuits.OrderBy((CircuitCopy c) => int.Parse((base.Document.GetElement(c.CircuitId) as ElectricalSystem).CircuitNumber)).ToList())
						{
							if (base.Document.GetElement(circuit.CircuitId) is ElectricalSystem sourceCircuit2)
							{
								List<ElementId> sourceElementIds = (from e in circuit.ElementIds.Select(base.Document.GetElement)
									where e != null
									select e.Id).ToList();
								List<ElementId> copiedElementIds2 = ElementTransformUtils.CopyElements(base.Document, sourceElementIds, translation).ToList();
								ElectricalSystem.Create(base.Document, copiedElementIds2, sourceCircuit2.SystemType).SelectPanel(panel);
								copiedSamePanelCircuits++;
							}
						}
					}
				}
				catch (Exception ex4)
				{
					TaskDialog.Show("Error", ex4.Message);
					transaction4.RollBack();
					return;
				}
				transaction4.Commit();
			}
			transactionGroup.Assimilate();
			TaskDialog.Show("Circuits Copied", $"{copiedPanels} new panel - with {copiedPanelCircuits} total circuits copied.\n{copiedSamePanelCircuits} same panel circuits copied.");
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex6)
		{
			TaskDialog.Show("Copy Circuits", ex6.ToString());
		}
	}

	private XYZ PickTranslationVector()
	{
		XYZ point1;
		XYZ point2;
		if (base.UiDocument.ActiveGraphicalView.ViewType == ViewType.Elevation)
		{
			Reference reference1;
			Reference reference2;
			if (new FilteredElementCollector(base.Document, base.Document.ActiveView.Id).OfClass(typeof(Level)).Cast<Level>().Any())
			{
				reference1 = base.UiDocument.Selection.PickObject(ObjectType.Element, new LevelFilter(), "Select an Origin Level");
				reference2 = base.UiDocument.Selection.PickObject(ObjectType.Element, new LevelFilter(), "Select a Destination Level");
			}
			else
			{
				Category levelCategory = Category.GetCategory(base.Document, BuiltInCategory.OST_Levels);
				if (levelCategory == null)
				{
					throw new System.InvalidOperationException("Cannot resolve level category");
				}
				reference1 = base.UiDocument.Selection.PickObject(ObjectType.LinkedElement, new LinkedCategoryFilter(levelCategory), "Select an Origin Level");
				reference2 = base.UiDocument.Selection.PickObject(ObjectType.LinkedElement, new LinkedCategoryFilter(levelCategory), "Select a Destination Level");
			}
			point1 = new XYZ(0.0, 0.0, reference1.GlobalPoint.Z);
			point2 = new XYZ(0.0, 0.0, reference2.GlobalPoint.Z);
		}
		else
		{
			try
			{
				point1 = base.UiDocument.Selection.PickPoint("Pick an Origin Point");
				point2 = base.UiDocument.Selection.PickPoint("Pick an Origin Point");
			}
			catch
			{
				TaskDialog.Show("Error", "You cannot copy in your current view. \n\nPlease switch to a Plan view or an Elevation view");
				throw;
			}
		}
		return point2 - point1;
	}

	private List<PanelCircuitCopy> BuildPanelCopies(List<FamilyInstance> selectedPanels)
	{
		List<PanelCircuitCopy> result = new List<PanelCircuitCopy>();
		foreach (FamilyInstance panel in selectedPanels)
		{
			List<CircuitCopy> circuits = (from es in ElectricalSystemUtils.GetCircuitsByPanel(base.Document, panel.Id)
				select new CircuitCopy(es.Id, ElectricalSystemUtils.GetCircuitElementIds(es))).ToList();
			result.Add(new PanelCircuitCopy(panel.Id, circuits));
		}
		return result;
	}

	private List<PanelCircuitCopy> BuildSamePanelCopies(IEnumerable<Element> selectedElements, List<PanelCircuitCopy> panelCopies)
	{
		HashSet<int> copiedPanelIds = panelCopies.Select((PanelCircuitCopy p) => p.PanelId.IntegerValue).ToHashSet();
		Dictionary<ElementId, List<CircuitCopy>> grouped = new Dictionary<ElementId, List<CircuitCopy>>();
		HashSet<int> addedCircuits = new HashSet<int>();
		foreach (Element element in selectedElements)
		{
			ISet<ElectricalSystem> systems = ElectricalSystemUtils.GetElectricalSystems(element);
			if (systems == null)
			{
				continue;
			}
			foreach (ElectricalSystem electricalSystem in systems)
			{
				FamilyInstance panel = ElectricalSystemUtils.GetBaseEquipment(electricalSystem);
				if (panel != null && !copiedPanelIds.Contains(panel.Id.IntegerValue) && addedCircuits.Add(electricalSystem.Id.IntegerValue))
				{
					if (!grouped.TryGetValue(panel.Id, out var circuits))
					{
						circuits = new List<CircuitCopy>();
						grouped[panel.Id] = circuits;
					}
					circuits.Add(new CircuitCopy(electricalSystem.Id, ElectricalSystemUtils.GetCircuitElementIds(electricalSystem)));
				}
			}
		}
		return grouped.Select((KeyValuePair<ElementId, List<CircuitCopy>> kvp) => new PanelCircuitCopy(kvp.Key, kvp.Value)).ToList();
	}
}
