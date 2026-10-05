using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_CoverGrid2D : SecuredExternalCommand
{
	private const string DialogTitle = "CoverGrid2D";

	protected override void ExecuteCore()
	{
		try
		{
			if (!(base.Document.ActiveView is ViewPlan { IsTemplate: false, ViewType: ViewType.FloorPlan } activePlan))
			{
				TaskDialog.Show("CoverGrid2D", "Vui lòng mở 1 ViewFloorPlan rồi chạy lại lệnh.");
				return;
			}
			List<Grid> grids = new FilteredElementCollector(base.Document, activePlan.Id).OfClass(typeof(Grid)).Cast<Grid>().ToList();
			if (grids.Count == 0)
			{
				TaskDialog.Show("CoverGrid2D", "View hiện tại không có Grid nào đang hiển thị.");
				return;
			}
			int converted = 0;
			int already2D = 0;
			int failed = 0;
			using Transaction transaction = new Transaction(base.Document, "Cover Grid 2D in FloorPlan");
			transaction.Start();
			foreach (Grid grid in grids)
			{
				if (!IsModelExtentInView(grid, activePlan))
				{
					already2D++;
				}
				else if (TryConvertGridTo2D(grid, activePlan))
				{
					converted++;
				}
				else
				{
					failed++;
				}
			}
			transaction.Commit();
			TaskDialog.Show("CoverGrid2D", "View: " + activePlan.Name + "\n" + $"Tổng Grid: {grids.Count}\n" + $"Đã chuyển 3D -> 2D: {converted}\n" + $"Đã ở 2D sẵn: {already2D}\n" + $"Thất bại: {failed}");
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("CoverGrid2D", ex2.ToString());
		}
	}

	private static bool IsModelExtentInView(Grid grid, View view)
	{
		DatumExtentType? end0 = TryGetDatumExtentType(grid, DatumEnds.End0, view);
		DatumExtentType? end1 = TryGetDatumExtentType(grid, DatumEnds.End1, view);
		return end0 != DatumExtentType.ViewSpecific || end1 != DatumExtentType.ViewSpecific;
	}

	private static DatumExtentType? TryGetDatumExtentType(Grid grid, DatumEnds end, View view)
	{
		try
		{
			return grid.GetDatumExtentTypeInView(end, view);
		}
		catch
		{
			return null;
		}
	}

	private static bool TryConvertGridTo2D(Grid grid, View view)
	{
		try
		{
			grid.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
			grid.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
