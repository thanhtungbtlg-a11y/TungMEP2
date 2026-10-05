using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VP_RevitAPI.Helper.ThreeDSync;
using VP_RevitAPI.State;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Internal.Combine;

internal static class ThreeDSyncRunner
{
	internal static void Run(UIApplication uiApplication, UIDocument uiDocument, Document document)
	{
		UI_HandyControlBootstrapper.EnsureInitialized();
		ThreeDSyncViewSelectionConfig initialSelection = ThreeDSyncConfigStore.Load(document);
		UI_3DSyncWindow window = new UI_3DSyncWindow(uiDocument, initialSelection);
		if (window.ShowDialog() != true)
		{
			return;
		}
		if (window.Action == ThreeDSyncDialogAction.Stop)
		{
			if (!ThreeDSyncService.Stop(uiApplication, document))
			{
				TaskDialog.Show("3DSync", "Khong co tien trinh dong bo 3D nao dang chay.");
			}
		}
		else if (window.Action == ThreeDSyncDialogAction.Start && !(window.SelectedPlanViewId == null) && window.SelectedView3DIds.Count != 0)
		{
			ElementId planViewId = window.SelectedPlanViewId;
			ThreeDSyncConfigStore.Save(document, planViewId, window.SelectedView3DIds);
			if (!ThreeDSyncService.Start(uiApplication, document, planViewId, window.SelectedView3DIds))
			{
				TaskDialog.Show("3DSync", "Khong the bat dau dong bo 3D.");
			}
		}
	}
}
