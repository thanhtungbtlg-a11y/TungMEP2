using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.State;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_SaveSyncView : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			View activeView = base.UiDocument.ActiveView;
			ViewOrientation3D orientation3D = null;
			bool isPerspective = false;
			bool isSectionBoxActive = false;
			BoundingBoxXYZ sectionBox = null;
			SyncViewKind kind;
			if (activeView is View3D view3D)
			{
				kind = SyncViewKind.ThreeD;
				orientation3D = view3D.GetOrientation();
				isPerspective = view3D.IsPerspective;
				isSectionBoxActive = view3D.IsSectionBoxActive;
				sectionBox = CloneSectionBox(view3D.GetSectionBox());
			}
			else if (activeView is ViewPlan)
			{
				kind = SyncViewKind.Plan;
			}
			else
			{
				if (!(activeView is ViewSheet))
				{
					TaskDialog.Show("SaveSyncView", "Chỉ hỗ trợ ViewPlan, View3D và ViewSheet.");
					return;
				}
				kind = SyncViewKind.Sheet;
			}
			UIView activeUiView = TryGetActiveUIView(base.UiDocument);
			if (activeUiView == null)
			{
				TaskDialog.Show("SaveSyncView", "Không tìm thấy UIView của view hiện tại.");
				return;
			}
			IList<XYZ> corners = activeUiView.GetZoomCorners();
			if (corners.Count != 2)
			{
				TaskDialog.Show("SaveSyncView", "Không đọc được vùng zoom hiện tại.");
				return;
			}
			string docKey = SyncViewStateStore.GetDocumentKey(base.Document);
			SyncViewState state = new SyncViewState
			{
				DocumentKey = docKey,
				Kind = kind,
				Corner1 = corners[0],
				Corner2 = corners[1],
				Orientation3D = orientation3D,
				IsPerspective = isPerspective,
				IsSectionBoxActive = isSectionBoxActive,
				SectionBox = sectionBox
			};
			SyncViewStateStore.Save(base.Document, state);
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("SaveSyncView", ex2.ToString());
		}
	}

	private static UIView? TryGetActiveUIView(UIDocument uiDocument)
	{
		foreach (UIView uiView in uiDocument.GetOpenUIViews())
		{
			if (uiView.ViewId == uiDocument.ActiveView.Id)
			{
				return uiView;
			}
		}
		return null;
	}

	private static BoundingBoxXYZ CloneSectionBox(BoundingBoxXYZ source)
	{
		return new BoundingBoxXYZ
		{
			Min = source.Min,
			Max = source.Max,
			Transform = source.Transform
		};
	}
}
