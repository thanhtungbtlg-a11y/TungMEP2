using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Helper.Layout.AutoDimFamilyInstancePoint;
using VP_RevitAPI.UI;

namespace VP_RevitAPI.Commands.LAYOUT;

[Transaction(TransactionMode.Manual)]
public class Command_AutoDimPointBuildCache : SecuredExternalCommand
{
	private sealed class RevitLinkIdComparer : IEqualityComparer<RevitLinkInstance>
	{
		public bool Equals(RevitLinkInstance? x, RevitLinkInstance? y)
		{
			if (x == null || y == null)
			{
				return false;
			}
			return x.Id == y.Id;
		}

		public int GetHashCode(RevitLinkInstance obj)
		{
			return obj.Id.GetHashCode();
		}
	}

	private const string DialogTitle = "AutoDIM Cache";

	protected override void ExecuteCore()
	{
		try
		{
			List<RevitLinkInstance> links = (from link in (from id in base.UiDocument.Selection.GetElementIds()
					select base.Document.GetElement(id)).OfType<RevitLinkInstance>()
				where link.GetLinkDocument() != null
				select link).Distinct<RevitLinkInstance>(new RevitLinkIdComparer()).ToList();
			if (links.Count != 2)
			{
				TaskDialog.Show("AutoDIM Cache", "Vui lòng chọn đúng 2 Revit Link (ARC và STR) trước khi chạy DimCache.");
				return;
			}
			AutoDimLinkResolver resolver = new AutoDimLinkResolver();
			AutoDimLinkResolution resolution = resolver.Resolve(links);
			UI_AutoDimCacheSettingsWindow settingsWindow = new UI_AutoDimCacheSettingsWindow(links, resolution.SuggestedArc, resolution.SuggestedStr, 2000.0, resolution.Message);
			if (settingsWindow.ShowDialog() == true && settingsWindow.Settings != null)
			{
				AutoDimCacheSettings settings = settingsWindow.Settings;
				RevitLinkInstance arcLink = settings.ArcLink;
				RevitLinkInstance strLink = settings.StrLink;
				List<Wall> walls = new FilteredElementCollector(base.Document, base.Document.ActiveView.Id, arcLink.Id).OfClass(typeof(Wall)).OfType<Wall>().ToList();
				List<FamilyInstance> columns = new FilteredElementCollector(base.Document, base.Document.ActiveView.Id, strLink.Id).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().OfCategory(BuiltInCategory.OST_StructuralColumns)
					.OfType<FamilyInstance>()
					.ToList();
				List<FamilyInstance> framings = new FilteredElementCollector(base.Document, base.Document.ActiveView.Id, strLink.Id).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().OfCategory(BuiltInCategory.OST_StructuralFraming)
					.OfType<FamilyInstance>()
					.ToList();
				AutoDimDatabaseExcel databaseExcel = new AutoDimDatabaseExcel();
				databaseExcel.Write(arcLink.Id, strLink.Id, walls.Select((Wall w) => w.Id).ToList(), columns.Select((FamilyInstance c) => c.Id).ToList(), framings.Select((FamilyInstance f) => f.Id).ToList(), settings.DistanceMillimeters);
				TaskDialog.Show("AutoDIM Cache", "Đã tạo cache:" + Environment.NewLine + databaseExcel.FilePath + Environment.NewLine + Environment.NewLine + $"Wall: {walls.Count}{Environment.NewLine}Column: {columns.Count}{Environment.NewLine}Framing: {framings.Count}" + $"{Environment.NewLine}Distance: {settings.DistanceMillimeters} mm");
			}
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("AutoDIM Cache", ex2.ToString());
		}
	}
}
