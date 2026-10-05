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
public class Command_MergeCircuits : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			List<Element> source = (from id in base.UiDocument.Selection.GetElementIds()
				select base.Document.GetElement(id) into c
				where c != null
				where ElectricalSystemUtils.GetElectricalSystems(c) != null || ElectricalSystemUtils.IsElectricalEquipment(c) || c.GetType() == typeof(ElectricalSystem)
				select c).ToList();
			Element element1;
			if (source.Count != 1)
			{
				if (base.UiDocument.ActiveGraphicalView.ViewType != ViewType.PanelSchedule)
				{
					Reference reference;
					try
					{
						reference = base.UiDocument.Selection.PickObject(ObjectType.Element, new CircuitedElementFilter(), "Pick a circuited element or circuit");
					}
					catch (Autodesk.Revit.Exceptions.OperationCanceledException)
					{
						return;
					}
					element1 = base.Document.GetElement(reference.ElementId);
				}
				else
				{
					element1 = source.FirstOrDefault();
				}
			}
			else
			{
				element1 = source.FirstOrDefault();
			}
			if (element1 != null)
			{
				if (source.All((Element c) => c.GetType() == typeof(ElectricalSystem)) && base.UiDocument.ActiveGraphicalView.ViewType == ViewType.PanelSchedule)
				{
					MergeFromPanelSchedule(source.Cast<ElectricalSystem>().ToList());
				}
				else if (ElectricalSystemUtils.IsElectricalEquipment(element1) && element1 is FamilyInstance targetPanel)
				{
					MergeToPanel(targetPanel);
				}
				else
				{
					MergeToCircuit(element1);
				}
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			TaskDialog.Show("Merge Circuits", ex3.ToString());
		}
	}

	private void MergeFromPanelSchedule(List<ElectricalSystem> circuits)
	{
		List<string> panelName = circuits.Select((ElectricalSystem es) => (es?.BaseEquipment)?.Name ?? string.Empty).ToList();
		List<int> poleNumber = circuits.Select((ElectricalSystem c) => c.PolesNumber).ToList();
		if (panelName.Any((string o) => o == panelName[0]) || poleNumber.Any((int o) => o != poleNumber[0]))
		{
			using (Transaction transaction = new Transaction(base.Document, "Merge Circuits"))
			{
				transaction.Start();
				List<ElectricalSystem> orderedCircuits = circuits.OrderBy((ElectricalSystem c) => int.Parse(c.CircuitNumber)).ToList();
				int num = -2;
				int polesNumber = orderedCircuits[0].PolesNumber;
				foreach (ElectricalSystem electricalSystem in orderedCircuits)
				{
					if (num == -2)
					{
						num = int.Parse(electricalSystem.CircuitNumber);
					}
					else
					{
						if (num != int.Parse(electricalSystem.CircuitNumber) - polesNumber && num != int.Parse(electricalSystem.CircuitNumber) - polesNumber - 1)
						{
							TaskDialog.Show("OperationFailed", "The circuits must be touching in the Panelboard schedule");
							transaction.RollBack();
							return;
						}
						orderedCircuits[0].AddToCircuit(electricalSystem?.Elements ?? new ElementSet());
						num = int.Parse(electricalSystem.CircuitNumber);
					}
				}
				transaction.Commit();
				return;
			}
		}
		TaskDialog.Show("OperationFailed", "The circuits do not have the same number of poles");
	}

	private void MergeToPanel(FamilyInstance panel)
	{
		using TransactionGroup transactionGroup = new TransactionGroup(base.Document, "Merge Circuits");
		transactionGroup.Start();
		List<Element> pickedElements;
		using (Transaction transaction = new Transaction(base.Document, "Select Circuits"))
		{
			transaction.Start();
			pickedElements = (from r in base.UiDocument.Selection.PickObjects(ObjectType.Element, new ElectricalPanelFilter(panel), "Select a element to merge to")
				select base.Document.GetElement(r.ElementId) into e
				where e != null
				select e).ToList();
			transaction.RollBack();
		}
		using (Transaction transaction2 = new Transaction(base.Document, "Merge Circuits"))
		{
			transaction2.Start();
			List<Element> touched = new List<Element>();
			foreach (Element element in pickedElements)
			{
				if (touched.Contains(element))
				{
					continue;
				}
				ISet<ElectricalSystem> systems = ElectricalSystemUtils.GetElectricalSystems(element);
				if (systems == null)
				{
					continue;
				}
				foreach (ElectricalSystem electricalSystem in systems)
				{
					try
					{
						electricalSystem.SelectPanel(panel);
						foreach (Element item in electricalSystem?.Elements ?? new ElementSet())
						{
							touched.Add(item);
						}
					}
					catch
					{
					}
				}
			}
			transaction2.Commit();
		}
		transactionGroup.Commit();
	}

	private void MergeToCircuit(Element element1)
	{
		ElectricalSystem circuit = null;
		ISet<ElectricalSystem> systems = ElectricalSystemUtils.GetElectricalSystems(element1);
		if (systems != null)
		{
			foreach (ElectricalSystem electricalSystem in systems)
			{
				circuit = electricalSystem;
			}
		}
		if (circuit == null)
		{
			return;
		}
		using TransactionGroup transactionGroup = new TransactionGroup(base.Document, "Merge Circuits");
		transactionGroup.Start();
		List<Element> pickedElements;
		using (Transaction transaction = new Transaction(base.Document, "Select Circuits"))
		{
			transaction.Start();
			pickedElements = (from r in base.UiDocument.Selection.PickObjects(ObjectType.Element, new ElectricalCircuitFilter(circuit), "Select a element to merge to")
				select base.Document.GetElement(r.ElementId) into e
				where e != null
				select e).ToList();
			transaction.RollBack();
		}
		using (Transaction transaction2 = new Transaction(base.Document, "Merge Circuits"))
		{
			transaction2.Start();
			ElementSet elementSet = new ElementSet();
			foreach (Element element2 in pickedElements)
			{
				if (elementSet.Contains(element2))
				{
					continue;
				}
				ISet<ElectricalSystem> electricalSystemSet = ElectricalSystemUtils.GetElectricalSystems(element2);
				if (electricalSystemSet == null)
				{
					elementSet.Insert(element2);
					continue;
				}
				foreach (ElectricalSystem item2 in electricalSystemSet)
				{
					foreach (Element item in item2?.Elements ?? new ElementSet())
					{
						elementSet.Insert(item);
					}
				}
			}
			try
			{
				circuit.AddToCircuit(elementSet);
			}
			catch
			{
			}
			transaction2.Commit();
		}
		transactionGroup.Commit();
	}
}
