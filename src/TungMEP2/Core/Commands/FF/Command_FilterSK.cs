using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;

namespace VP_RevitAPI.Commands.FF;

[Transaction(TransactionMode.Manual)]
public class Command_FilterSK : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			ICollection<ElementId> selectedIds = base.UiDocument.Selection.GetElementIds();
			if (selectedIds.Count == 0)
			{
				TaskDialog.Show("FilterSK", "Vui lòng chọn Elements.");
				return;
			}
			List<ElementId> targetSelection = new List<ElementId>();
			HashSet<long> selectedTracker = new HashSet<long>();
			foreach (ElementId selectedId in selectedIds)
			{
				Element element = base.Document.GetElement(selectedId);
				if (element != null)
				{
					if (element is Group group)
					{
						AddSprinklerMembers(group, targetSelection, selectedTracker);
					}
					else if (IsSprinkler(element) && selectedTracker.Add(element.Id.Value))
					{
						targetSelection.Add(element.Id);
					}
				}
			}
			base.UiDocument.Selection.SetElementIds(targetSelection);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("FilterSK", ex2.ToString());
		}
	}

	private static bool IsSprinkler(Element element)
	{
		Category category = element.Category;
		return category != null && category.Id?.Value == -2008099;
	}

	private void AddSprinklerMembers(Group group, ICollection<ElementId> targetSelection, ISet<long> selectedTracker)
	{
		foreach (ElementId memberId in group.GetMemberIds())
		{
			Element member = base.Document.GetElement(memberId);
			if (member != null && IsSprinkler(member) && selectedTracker.Add(member.Id.Value))
			{
				targetSelection.Add(member.Id);
			}
		}
	}
}
