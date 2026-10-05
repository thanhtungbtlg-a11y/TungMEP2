using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Commands.FF;

[Transaction(TransactionMode.Manual)]
public class Command_SetDnBySkSelect : SecuredExternalCommand
{
	private sealed class SkDataItem
	{
		public double DN { get; set; }

		public double L { get; set; }
	}

	protected override void ExecuteCore()
	{
		try
		{
			List<ElementId> sprinklerIds = CollectSelectedSprinklers();
			if (sprinklerIds.Count == 0)
			{
				TaskDialog.Show("Đặt DN theo SK", "Vui lòng chọn SK.");
				return;
			}
			List<SkDataItem> skData = LoadSkData();
			if (skData.Count == 0)
			{
				TaskDialog.Show("Đặt DN theo SK", "Không tìm thấy dữ liệu SKData.json.");
				return;
			}
			Reference pipeReference = base.UiDocument.Selection.PickObject(ObjectType.Element, new Isf.Pipe(), "Vui lòng chọn ống Pipe");
			if (!(base.Document.GetElement(pipeReference) is Pipe pipe))
			{
				TaskDialog.Show("Đặt DN theo SK", "Không lấy được Pipe.");
				return;
			}
			double flow = sprinklerIds.Count;
			SkDataItem selected = skData.FirstOrDefault((SkDataItem item) => flow <= item.L) ?? skData.Last();
			double targetDiameterInternal = UnitUtils.ConvertToInternalUnits(selected.DN, UnitTypeId.Millimeters);
			using Transaction transaction = new Transaction(base.Document, "Set DN from SK Select");
			transaction.Start();
			Parameter diameterParameter = ((Element)pipe).get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
			if (diameterParameter != null && !diameterParameter.IsReadOnly)
			{
				diameterParameter.Set(targetDiameterInternal);
			}
			transaction.Commit();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Đặt DN theo SK", ex2.Message);
		}
	}

	private List<ElementId> CollectSelectedSprinklers()
	{
		ICollection<ElementId> selectedIds = base.UiDocument.Selection.GetElementIds();
		List<ElementId> result = new List<ElementId>();
		HashSet<long> tracker = new HashSet<long>();
		foreach (ElementId selectedId in selectedIds)
		{
			Element element = base.Document.GetElement(selectedId);
			if (element == null)
			{
				continue;
			}
			if (element is Group group)
			{
				foreach (ElementId memberId in group.GetMemberIds())
				{
					Element member = base.Document.GetElement(memberId);
					if (member != null && IsSprinkler(member) && tracker.Add(member.Id.Value))
					{
						result.Add(member.Id);
					}
				}
			}
			else if (IsSprinkler(element) && tracker.Add(element.Id.Value))
			{
				result.Add(element.Id);
			}
		}
		return result;
	}

	private static bool IsSprinkler(Element element)
	{
		Category category = element.Category;
		return category != null && category.Id?.Value == -2008099;
	}

	private static List<SkDataItem> LoadSkData()
	{
		string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
		string skDataPath = Path.Combine(baseDir, "Store", "SKData.json");
		if (!File.Exists(skDataPath))
		{
			return new List<SkDataItem>();
		}
		string json = File.ReadAllText(skDataPath);
		List<SkDataItem> data = JsonSerializer.Deserialize<List<SkDataItem>>(json);
		if (data == null)
		{
			return new List<SkDataItem>();
		}
		return data.OrderBy((SkDataItem item) => item.L).ToList();
	}
}
