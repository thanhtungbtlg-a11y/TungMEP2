using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.State;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_SyncView : SecuredExternalCommand
{
	protected override void ExecuteCore()
	{
		try
		{
			if (!SyncViewStateStore.TryGet(base.Document, out SyncViewState savedState) || savedState == null)
			{
				TaskDialog.Show("SyncView", "Chưa có dữ liệu. Hãy bấm SaveSyncView trước.");
				return;
			}
			string currentDocKey = SyncViewStateStore.GetDocumentKey(base.Document);
			if (!string.Equals(currentDocKey, savedState.DocumentKey, StringComparison.OrdinalIgnoreCase))
			{
				TaskDialog.Show("SyncView", "Dữ liệu đã lưu thuộc document khác nên không thể đồng bộ.");
				return;
			}
			View activeView = base.UiDocument.ActiveView;
			View3D active3D = null;
			SyncViewKind currentKind;
			if (activeView is View3D view3D)
			{
				currentKind = SyncViewKind.ThreeD;
				active3D = view3D;
			}
			else if (activeView is ViewPlan)
			{
				currentKind = SyncViewKind.Plan;
			}
			else
			{
				if (!(activeView is ViewSheet))
				{
					TaskDialog.Show("SyncView", "Chỉ hỗ trợ ViewPlan, View3D và ViewSheet.");
					return;
				}
				currentKind = SyncViewKind.Sheet;
			}
			if (currentKind != savedState.Kind)
			{
				TaskDialog.Show("SyncView", $"Loại view đã lưu là {savedState.Kind}, view hiện tại là {currentKind}.");
				return;
			}
			UIView activeUiView = TryGetActiveUIView(base.UiDocument);
			if (activeUiView == null)
			{
				TaskDialog.Show("SyncView", "Không tìm thấy UIView của view hiện tại.");
			}
			else if (currentKind == SyncViewKind.ThreeD && active3D != null)
			{
				if (active3D.IsPerspective == savedState.IsPerspective)
				{
					using (Transaction transaction = new Transaction(base.Document, "Sync 3D View State"))
					{
						transaction.Start();
						if (savedState.SectionBox != null)
						{
							active3D.SetSectionBox(CloneSectionBox(savedState.SectionBox));
						}
						active3D.IsSectionBoxActive = savedState.IsSectionBoxActive;
						if (!active3D.IsLocked && savedState.Orientation3D != null)
						{
							active3D.SetOrientation(savedState.Orientation3D);
						}
						transaction.Commit();
						QueueZoomOnIdling(savedState);
						return;
					}
				}
				TaskDialog.Show("SyncView", "View 3D hiện tại khác chế độ Perspective/Orthographic với dữ liệu đã lưu.");
			}
			else
			{
				activeUiView.ZoomAndCenterRectangle(savedState.Corner1, savedState.Corner2);
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("SyncView", ex2.ToString());
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

	private void QueueZoomOnIdling(SyncViewState savedState)
	{
		UIApplication uiApplication = base.UiApplication;
		EventHandler<IdlingEventArgs> handler = null;
		handler = delegate
		{
			uiApplication.Idling -= handler;
			TryGetActiveUIView(base.UiDocument)?.ZoomAndCenterRectangle(savedState.Corner1, savedState.Corner2);
		};
		uiApplication.Idling += handler;
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
