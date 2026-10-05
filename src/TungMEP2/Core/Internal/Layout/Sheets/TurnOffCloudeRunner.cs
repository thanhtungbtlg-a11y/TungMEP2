using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VP_RevitAPI.Helper.Sheets;

namespace VP_RevitAPI.Internal.Layout.Sheets;

public static class TurnOffCloudeRunner
{
	public static void Run(UIDocument uiDocument, Document document)
	{
		List<ViewSheet> sheets = SheetExportUtilities.ResolveSheetsFromSelectionOrPrintSet(uiDocument);
		if (sheets.Count == 0)
		{
			throw new InvalidOperationException("Không có sheet hợp lệ để xử lý.");
		}
		TaskDialogResult action = AskUserAction();
		if (action == TaskDialogResult.Cancel)
		{
			return;
		}
		bool hide = action == TaskDialogResult.CommandLink1;
		ElementId revisionCloudCategoryId = new ElementId(BuiltInCategory.OST_RevisionClouds);
		using Transaction transaction = new Transaction(document, hide ? "Hide Revision Clouds" : "Show Revision Clouds");
		transaction.Start();
		int updatedCount = 0;
		int skippedCount = 0;
		foreach (ViewSheet sheet in sheets)
		{
			if (!sheet.CanCategoryBeHidden(revisionCloudCategoryId))
			{
				skippedCount++;
				continue;
			}
			sheet.SetCategoryHidden(revisionCloudCategoryId, hide);
			updatedCount++;
		}
		transaction.Commit();
		TaskDialog.Show("TurnOffCloude", string.Format("{0} Revision Clouds.\nUpdated: {1}\nSkipped: {2}", hide ? "Đã tắt" : "Đã bật", updatedCount, skippedCount));
	}

	private static TaskDialogResult AskUserAction()
	{
		TaskDialog dialog = new TaskDialog("TurnOffCloude")
		{
			MainInstruction = "Revision Clouds",
			MainContent = "Chọn thao tác cho các sheet đã chọn:"
		};
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Hide Revision Clouds");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Show Revision Clouds");
		dialog.CommonButtons = TaskDialogCommonButtons.Cancel;
		return dialog.Show();
	}
}
