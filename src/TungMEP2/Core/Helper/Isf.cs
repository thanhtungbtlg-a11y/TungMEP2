using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper.Electrical;

namespace VP_RevitAPI.Helper;

public static class Isf
{
	public sealed class FamilyInstanceOnly : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem is FamilyInstance;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public sealed class LocationCurve : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem?.Location is Autodesk.Revit.DB.LocationCurve;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public sealed class StraightSegment : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			if (!(elem?.Location is Autodesk.Revit.DB.LocationCurve locationCurve))
			{
				return false;
			}
			return locationCurve.Curve is Line;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public sealed class MepConnector : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem is MEPCurve || (elem is FamilyInstance familyInstance && familyInstance.MEPModel != null);
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public sealed class Fitting : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem is FamilyInstance familyInstance && familyInstance.MEPModel?.ConnectorManager?.Connectors != null;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public sealed class Sample : ISelectionFilter
	{
		private readonly Domain _domain;

		public Sample(Domain domain)
		{
			_domain = domain;
		}

		public bool AllowElement(Element elem)
		{
			Domain domain = _domain;
			if (1 == 0)
			{
			}
			bool result;
			switch (domain)
			{
			case Domain.DomainPiping:
				result = elem is Autodesk.Revit.DB.Plumbing.Pipe;
				break;
			case Domain.DomainHvac:
				result = elem is Duct;
				break;
			case Domain.DomainElectrical:
			{
				bool flag = ((elem is Conduit || elem is CableTray) ? true : false);
				result = flag;
				break;
			}
			default:
			{
				bool flag = ((elem is Autodesk.Revit.DB.Plumbing.Pipe || elem is Duct || elem is Conduit || elem is CableTray) ? true : false);
				result = flag;
				break;
			}
			}
			if (1 == 0)
			{
			}
			return result;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public sealed class Pipe : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return elem is Autodesk.Revit.DB.Plumbing.Pipe;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public sealed class Sprinkler : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			int result;
			if (elem is FamilyInstance)
			{
				Category category = elem.Category;
				result = ((category != null && category.Id?.Value == -2008099) ? 1 : 0);
			}
			else
			{
				result = 0;
			}
			return (byte)result != 0;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	public sealed class ElectricalDataSource : ISelectionFilter
	{
		public bool AllowElement(Element elem)
		{
			return IsAllowedCategory(elem);
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return false;
		}

		internal static bool IsAllowedCategory(Element? element)
		{
			return ElectricalDataCategoryPolicy.IsAllowed(element);
		}
	}
}
