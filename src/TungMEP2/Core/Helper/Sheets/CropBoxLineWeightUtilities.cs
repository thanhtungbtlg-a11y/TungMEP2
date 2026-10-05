using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets;

public static class CropBoxLineWeightUtilities
{
	public static List<View> GetViewsPlacedOnSheets(Document document, IEnumerable<ViewSheet> sheets)
	{
		List<View> views = new List<View>();
		foreach (ViewSheet sheet in sheets)
		{
			foreach (ElementId viewportId in sheet.GetAllViewports())
			{
				if (document.GetElement(viewportId) is Viewport viewport && document.GetElement(viewport.ViewId) is View view)
				{
					views.Add(view);
				}
			}
		}
		return (from view2 in views
			group view2 by view2.Id into @group
			select @group.First() into view2
			orderby view2.Name
			select view2).ToList();
	}

	public static int? PromptLineWeight()
	{
		Window window = new Window
		{
			Title = "Crop Line Weight",
			Width = 320.0,
			Height = 170.0,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			ResizeMode = ResizeMode.NoResize
		};
		StackPanel root = new StackPanel
		{
			Margin = new Thickness(12.0)
		};
		root.Children.Add(new TextBlock
		{
			Text = "Chọn lineweight (1-16):"
		});
		ComboBox comboBox = new ComboBox
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 12.0),
			ItemsSource = Enumerable.Range(1, 16).ToList(),
			SelectedIndex = 0
		};
		root.Children.Add(comboBox);
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Button okButton = new Button
		{
			Content = "Đồng ý",
			Width = 80.0,
			IsDefault = true,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		Button cancelButton = new Button
		{
			Content = "Hủy",
			Width = 80.0,
			IsCancel = true
		};
		okButton.Click += delegate
		{
			window.DialogResult = true;
		};
		cancelButton.Click += delegate
		{
			window.DialogResult = false;
		};
		buttons.Children.Add(okButton);
		buttons.Children.Add(cancelButton);
		root.Children.Add(buttons);
		window.Content = root;
		if (window.ShowDialog() != true)
		{
			return null;
		}
		return comboBox.SelectedItem as int?;
	}

	public static CropLineWeightReport Apply(Document document, IList<View> views, int lineWeight)
	{
		CropLineWeightReport report = new CropLineWeightReport
		{
			TotalViews = views.Count,
			LineWeight = lineWeight
		};
		OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();
		overrideSettings.SetProjectionLineWeight(lineWeight);
		using TransactionGroup transactionGroup = new TransactionGroup(document, "Set Crop Box Line Weight");
		transactionGroup.Start();
		foreach (View view in views)
		{
			using Transaction transaction = new Transaction(document, "Set Crop Box Line Weight - " + view.Name);
			transaction.Start();
			try
			{
				bool wasActive = view.CropBoxActive;
				bool wasVisible = view.CropBoxVisible;
				if (!view.CropBoxActive)
				{
					view.CropBoxActive = true;
				}
				view.CropBoxVisible = false;
				document.Regenerate();
				ICollection<ElementId> hiddenIds = new FilteredElementCollector(document, view.Id).ToElementIds();
				view.CropBoxVisible = true;
				document.Regenerate();
				List<Element> diffElements = new FilteredElementCollector(document, view.Id).Excluding(hiddenIds).ToElements().ToList();
				Element cropElement = diffElements.FirstOrDefault((Element element) => element.Category != null && element.Category.Id == new ElementId(BuiltInCategory.OST_CropBoundary));
				if (cropElement == null)
				{
					cropElement = diffElements.FirstOrDefault();
				}
				if (cropElement == null)
				{
					throw new InvalidOperationException("Không tìm thấy crop boundary element.");
				}
				view.SetElementOverrides(cropElement.Id, overrideSettings);
				view.CropBoxVisible = wasVisible;
				view.CropBoxActive = wasActive;
				transaction.Commit();
				report.SuccessCount++;
			}
			catch (Exception ex)
			{
				if (transaction.HasStarted())
				{
					transaction.RollBack();
				}
				report.FailedViews.Add(view.Name + ": " + ex.Message);
			}
		}
		transactionGroup.Assimilate();
		return report;
	}
}
