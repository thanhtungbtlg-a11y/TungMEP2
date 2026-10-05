using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_Elbow45To90 : SecuredExternalCommand
{
	private readonly struct Cluster
	{
		public Pipe MiddlePipe { get; }

		public FamilyInstance Fitting1 { get; }

		public FamilyInstance Fitting2 { get; }

		public Cluster(Pipe middlePipe, FamilyInstance fitting1, FamilyInstance fitting2)
		{
			MiddlePipe = middlePipe;
			Fitting1 = fitting1;
			Fitting2 = fitting2;
		}
	}

	private sealed class PipeAndFittingSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			if (elem is Pipe)
			{
				return true;
			}
			if (elem is FamilyInstance familyInstance && IsPipeFitting(familyInstance) && familyInstance.MEPModel?.ConnectorManager?.Connectors != null)
			{
				return true;
			}
			return false;
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
			List<Element> elements = GetSelectedElements();
			List<Cluster> clusters;
			int skipped;
			(clusters, skipped) = BuildClusters(elements);
			if (clusters.Count == 0)
			{
				elements = PickElements();
				(clusters, skipped) = BuildClusters(elements);
			}
			if (clusters.Count == 0)
			{
				TaskDialog.Show("Elbow45to90", "Không tìm được cụm hợp lệ (1 pipe + 2 pipe fitting).");
				return;
			}
			using TransactionGroup group = new TransactionGroup(base.Document, "Elbow45to90");
			group.Start();
			foreach (Cluster cluster in clusters)
			{
				using Transaction tx = new Transaction(base.Document, "Elbow45to90");
				tx.Start();
				try
				{
					PipeUtilities.Elbow45To90(cluster.MiddlePipe, cluster.Fitting1, cluster.Fitting2);
					tx.Commit();
				}
				catch
				{
					tx.RollBack();
				}
			}
			group.Assimilate();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Elbow45to90", ex2.ToString());
		}
	}

	private List<Element> GetSelectedElements()
	{
		ICollection<ElementId> selectionIds = base.UiDocument.Selection.GetElementIds();
		return (from e in selectionIds.Select(base.Document.GetElement)
			where e != null
			select e).ToList();
	}

	private List<Element> PickElements()
	{
		IList<Reference> references = base.UiDocument.Selection.PickObjects(ObjectType.Element, new PipeAndFittingSelectionFilter(), "Quét chọn ống và phụ kiện ống");
		return (from r in references
			select base.Document.GetElement(r) into e
			where e != null
			select e).ToList();
	}

	private static (List<Cluster> clusters, int skipped) BuildClusters(IEnumerable<Element> elements)
	{
		List<Pipe> pipes = elements.OfType<Pipe>().ToList();
		HashSet<ElementId> fittingIds = (from f in elements.OfType<FamilyInstance>().Where(IsPipeFitting)
			select f.Id).ToHashSet();
		List<Cluster> clusters = new List<Cluster>();
		HashSet<ElementId> processedPipeIds = new HashSet<ElementId>();
		int skipped = 0;
		foreach (Pipe pipe in pipes)
		{
			if (!processedPipeIds.Add(pipe.Id))
			{
				continue;
			}
			List<FamilyInstance> fittings = GetConnectedFittings(pipe, fittingIds);
			if (fittings.Count != 2)
			{
				skipped++;
				continue;
			}
			List<Pipe> pipes2 = PipeUtilities.GetConnectedPipesFromFitting(fittings[0]);
			List<Pipe> pipes3 = PipeUtilities.GetConnectedPipesFromFitting(fittings[1]);
			if (pipes2.Count != 2 || pipes3.Count != 2)
			{
				skipped++;
			}
			else if (!pipes2.Any((Pipe p) => p.Id == pipe.Id) || !pipes3.Any((Pipe p) => p.Id == pipe.Id))
			{
				skipped++;
			}
			else
			{
				clusters.Add(new Cluster(pipe, fittings[0], fittings[1]));
			}
		}
		return (clusters: clusters, skipped: skipped);
	}

	private static List<FamilyInstance> GetConnectedFittings(Pipe pipe, HashSet<ElementId> fittingIds)
	{
		ConnectorSet connectorSet = pipe.ConnectorManager.Connectors;
		List<FamilyInstance> fittings = new List<FamilyInstance>();
		HashSet<ElementId> ids = new HashSet<ElementId>();
		foreach (Connector connector in connectorSet)
		{
			if (connector.ConnectorType != ConnectorType.End || !connector.IsConnected)
			{
				continue;
			}
			foreach (Connector refConnector in connector.AllRefs)
			{
				if (!(refConnector.Owner.Id == pipe.Id) && refConnector.Owner is FamilyInstance fitting && fittingIds.Contains(fitting.Id) && ids.Add(fitting.Id))
				{
					fittings.Add(fitting);
				}
			}
		}
		return fittings;
	}

	private static bool IsPipeFitting(FamilyInstance fitting)
	{
		Category category = fitting.Category;
		return category != null && category.Id.IntegerValue == -2008049;
	}
}
