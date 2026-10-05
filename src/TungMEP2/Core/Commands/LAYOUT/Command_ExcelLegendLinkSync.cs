using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_ExcelLegendLinkSync : SecuredExternalCommand
{
	private const string DialogTitle = "Excel Legend Link Sync";

	private const string KeepPdfPreviewEnvVar = "VP_EXCEL_KEEP_PDF_PREVIEW";

	protected override void ExecuteCore()
	{
		Environment.SetEnvironmentVariable("VP_EXCEL_KEEP_PDF_PREVIEW", "1");
		ElementId activeLegendId = ElementId.InvalidElementId;
		View activeView = base.UiDocument.ActiveView;
		if (activeView != null && activeView.ViewType == ViewType.Legend)
		{
			activeLegendId = activeView.Id;
		}
		UI_ExcelLegendLinkSyncWindow window = new UI_ExcelLegendLinkSyncWindow(base.Document, activeLegendId);
		window.ShowDialog();
	}
}
