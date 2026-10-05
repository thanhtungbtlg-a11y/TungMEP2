using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VP_RevitAPI.Internal.Ctn.PipeSpacing;

namespace VP_RevitAPI.UI;

internal sealed class PipeSpacingDialogPreview
{
	internal PipeSpacingDialogPreview(
		IReadOnlyList<PipeSpacingAnalysisRow> rows,
		double minimumClearGapMm,
		long anchorKey,
		IReadOnlyList<string> warnings)
	{
		Rows = rows ?? Array.Empty<PipeSpacingAnalysisRow>();
		MinimumClearGapMm = minimumClearGapMm;
		AnchorKey = anchorKey;
		Warnings = warnings ?? Array.Empty<string>();
	}

	internal IReadOnlyList<PipeSpacingAnalysisRow> Rows { get; }
	internal double MinimumClearGapMm { get; }
	internal long AnchorKey { get; }
	internal IReadOnlyList<string> Warnings { get; }
}

internal sealed class UI_PipeSpacingWindow : Window
{
	public sealed class DnRuleRow
	{
		public DnRuleRow()
		{
		}

		public double MaxDnMm { get; set; }
		public double ClearGapMm { get; set; }
	}

	private readonly Func<PipeSpacingSettings, PipeSpacingDialogPreview> _previewProvider;
	private readonly ComboBox _mode;
	private readonly TextBox _clearGap;
	private readonly TextBox _centerSpacing;
	private readonly TextBox _fallbackGap;
	private readonly TextBox _angleTolerance;
	private readonly CheckBox _includeInsulation;
	private readonly CheckBox _includeFittingEnvelope;
	private readonly CheckBox _previewBeforeApply;
	private readonly DataGrid _dnRules;
	private readonly DataGrid _previewGrid;
	private readonly TextBlock _status;

	internal UI_PipeSpacingWindow(
		PipeSpacingSettings initialSettings,
		Func<PipeSpacingSettings, PipeSpacingDialogPreview> previewProvider)
	{
		_previewProvider = previewProvider ?? throw new ArgumentNullException(nameof(previewProvider));
		SelectedSettings = (initialSettings ?? PipeSpacingSettings.CreateDefault()).Normalize();

		Title = "TungMEP2 - Pipe Spacing";
		Width = 920;
		Height = 690;
		MinWidth = 820;
		MinHeight = 600;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;
		FontFamily = new FontFamily("Segoe UI");
		Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));

		Grid root = new Grid { Margin = new Thickness(14) };
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		Content = root;

		TextBlock heading = new TextBlock
		{
			Text = "Dàn khoảng cách dãy Pipe theo khoảng hở thực",
			FontSize = 17,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0, 0, 0, 10)
		};
		root.Children.Add(heading);

		Grid settings = new Grid { Margin = new Thickness(0, 0, 0, 10) };
		for (int index = 0; index < 5; index++) settings.ColumnDefinitions.Add(new ColumnDefinition());
		settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		Grid.SetRow(settings, 1);
		root.Children.Add(settings);

		_mode = AddCombo(settings, "Mode", 0, 0, new[] { "Equal Clear Gap", "Equal Center Spacing", "DN Rule Table" });
		_clearGap = AddText(settings, "Clear gap (mm)", 0, 1);
		_centerSpacing = AddText(settings, "Center spacing (mm)", 0, 2);
		_fallbackGap = AddText(settings, "Fallback gap (mm)", 0, 3);
		_angleTolerance = AddText(settings, "Angle tolerance (°)", 0, 4);
		_includeInsulation = AddCheck(settings, "Include insulation", 2, 0);
		_includeFittingEnvelope = AddCheck(settings, "Include fitting envelope", 2, 1);
		_previewBeforeApply = AddCheck(settings, "PreviewBeforeApply", 2, 2);
		_mode.SelectionChanged += delegate { UpdateModeState(); };

		Grid contentGrid = new Grid();
		contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
		contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
		contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		Grid.SetRow(contentGrid, 2);
		root.Children.Add(contentGrid);

		StackPanel rulesPanel = new StackPanel();
		rulesPanel.Children.Add(new TextBlock { Text = "DN Rule Table", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
		_dnRules = new DataGrid
		{
			AutoGenerateColumns = false,
			CanUserAddRows = true,
			CanUserDeleteRows = true,
			HeadersVisibility = DataGridHeadersVisibility.Column,
			Height = 320
		};
		_dnRules.Columns.Add(new DataGridTextColumn { Header = "Max DN (mm)", Binding = new Binding(nameof(DnRuleRow.MaxDnMm)) });
		_dnRules.Columns.Add(new DataGridTextColumn { Header = "Gap (mm)", Binding = new Binding(nameof(DnRuleRow.ClearGapMm)) });
		rulesPanel.Children.Add(_dnRules);
		rulesPanel.Children.Add(new TextBlock
		{
			Text = "Mặc định: DN ≤ 1000 dùng gap 75 mm. DN ngoài bảng dùng Fallback gap.",
			Margin = new Thickness(0, 6, 0, 0),
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brushes.DimGray
		});
		contentGrid.Children.Add(rulesPanel);

		StackPanel previewPanel = new StackPanel();
		Grid.SetColumn(previewPanel, 2);
		contentGrid.Children.Add(previewPanel);
		previewPanel.Children.Add(new TextBlock { Text = "Preview result", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
		_previewGrid = new DataGrid
		{
			AutoGenerateColumns = true,
			IsReadOnly = true,
			CanUserAddRows = false,
			HeadersVisibility = DataGridHeadersVisibility.Column,
			Height = 360
		};
		previewPanel.Children.Add(_previewGrid);
		_status = new TextBlock { Text = "Bấm Preview để kiểm tra vị trí dự kiến.", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
		previewPanel.Children.Add(_status);

		StackPanel actions = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0, 12, 0, 0)
		};
		Button reset = Button("Reset defaults", false);
		reset.Click += delegate { LoadSettings(PipeSpacingSettings.CreateDefault()); };
		Button preview = Button("Preview", false);
		preview.Margin = new Thickness(8, 0, 0, 0);
		preview.Click += delegate { RefreshPreview(false); };
		Button cancel = Button("Cancel", false);
		cancel.Margin = new Thickness(8, 0, 0, 0);
		cancel.Click += delegate { DialogResult = false; };
		Button apply = Button("Apply", true);
		apply.Margin = new Thickness(8, 0, 0, 0);
		apply.Click += delegate { RefreshPreview(true); };
		actions.Children.Add(reset);
		actions.Children.Add(preview);
		actions.Children.Add(cancel);
		actions.Children.Add(apply);
		Grid.SetRow(actions, 3);
		root.Children.Add(actions);

		LoadSettings(SelectedSettings);
	}

	internal PipeSpacingSettings SelectedSettings { get; private set; }
	internal bool PreviewBeforeApply => SelectedSettings?.PreviewBeforeApply ?? true;

	private void RefreshPreview(bool closeOnSuccess)
	{
		if (!TryReadSettings(out PipeSpacingSettings settings, out string error))
		{
			_status.Text = error;
			_status.Foreground = Brushes.Firebrick;
			return;
		}
		try
		{
			PipeSpacingDialogPreview preview = _previewProvider(settings);
			_previewGrid.ItemsSource = preview.Rows;
			_status.Text = $"Anchor: {preview.AnchorKey}; minimum clear gap: {preview.MinimumClearGapMm:0.##} mm" +
			               (preview.Warnings.Count > 0 ? ". " + string.Join("; ", preview.Warnings) : string.Empty);
			_status.Foreground = preview.Warnings.Count > 0 ? Brushes.DarkGoldenrod : Brushes.DarkGreen;
			SelectedSettings = settings;
			if (closeOnSuccess)
			{
				DialogResult = true;
			}
		}
		catch (Exception exception)
		{
			_status.Text = exception.Message;
			_status.Foreground = Brushes.Firebrick;
		}
	}

	private bool TryReadSettings(out PipeSpacingSettings settings, out string error)
	{
		settings = null;
		error = string.Empty;
		if (!TryNumber(_clearGap.Text, out double clearGap) ||
		    !TryNumber(_centerSpacing.Text, out double centerSpacing) ||
		    !TryNumber(_fallbackGap.Text, out double fallbackGap) ||
		    !TryNumber(_angleTolerance.Text, out double angleTolerance))
		{
			error = "Các giá trị khoảng cách/góc phải là số hợp lệ.";
			return false;
		}

		List<PipeSpacingDnRule> rules = (_dnRules.ItemsSource as IEnumerable<DnRuleRow> ?? Array.Empty<DnRuleRow>())
			.Where(row => row != null)
			.Select(row => new PipeSpacingDnRule(row.MaxDnMm, row.ClearGapMm))
			.ToList();
		settings = new PipeSpacingSettings
		{
			Mode = (PipeSpacingMode)Math.Max(0, _mode.SelectedIndex),
			ClearGapMm = clearGap,
			CenterSpacingMm = centerSpacing,
			FallbackClearGapMm = fallbackGap,
			IncludeInsulation = _includeInsulation.IsChecked == true,
			IncludeFittingEnvelope = _includeFittingEnvelope.IsChecked == true,
			ParallelAngleToleranceDegrees = angleTolerance,
			PreviewBeforeApply = _previewBeforeApply.IsChecked == true,
			DnRules = rules
		}.Normalize();
		return true;
	}

	private void LoadSettings(PipeSpacingSettings settings)
	{
		PipeSpacingSettings normalized = settings.Normalize();
		_mode.SelectedIndex = (int)normalized.Mode;
		_clearGap.Text = normalized.ClearGapMm.ToString("0.###", CultureInfo.CurrentCulture);
		_centerSpacing.Text = normalized.CenterSpacingMm.ToString("0.###", CultureInfo.CurrentCulture);
		_fallbackGap.Text = normalized.FallbackClearGapMm.ToString("0.###", CultureInfo.CurrentCulture);
		_angleTolerance.Text = normalized.ParallelAngleToleranceDegrees.ToString("0.###", CultureInfo.CurrentCulture);
		_includeInsulation.IsChecked = normalized.IncludeInsulation;
		_includeFittingEnvelope.IsChecked = normalized.IncludeFittingEnvelope;
		_previewBeforeApply.IsChecked = normalized.PreviewBeforeApply;
		_dnRules.ItemsSource = normalized.DnRules.Select(rule => new DnRuleRow
		{
			MaxDnMm = rule.MaxNominalDiameter,
			ClearGapMm = rule.ClearGap
		}).ToList();
		SelectedSettings = normalized;
		UpdateModeState();
	}

	private void UpdateModeState()
	{
		PipeSpacingMode mode = (PipeSpacingMode)Math.Max(0, _mode.SelectedIndex);
		_clearGap.IsEnabled = mode == PipeSpacingMode.EqualClearGap;
		_centerSpacing.IsEnabled = mode == PipeSpacingMode.EqualCenterSpacing;
		_fallbackGap.IsEnabled = mode == PipeSpacingMode.DnRuleTable;
		_dnRules.IsEnabled = mode == PipeSpacingMode.DnRuleTable;
	}

	private static ComboBox AddCombo(Grid grid, string label, int row, int column, IEnumerable<string> items)
	{
		StackPanel panel = Cell(grid, label, row, column);
		ComboBox combo = new ComboBox { Height = 28, ItemsSource = items.ToList() };
		panel.Children.Add(combo);
		return combo;
	}

	private static TextBox AddText(Grid grid, string label, int row, int column)
	{
		StackPanel panel = Cell(grid, label, row, column);
		TextBox box = new TextBox { Height = 28, Padding = new Thickness(5, 2, 5, 2) };
		panel.Children.Add(box);
		return box;
	}

	private static CheckBox AddCheck(Grid grid, string text, int row, int column)
	{
		CheckBox box = new CheckBox { Content = text, Margin = new Thickness(4, 8, 12, 4) };
		Grid.SetRow(box, row);
		Grid.SetColumn(box, column);
		grid.Children.Add(box);
		return box;
	}

	private static StackPanel Cell(Grid grid, string label, int row, int column)
	{
		StackPanel panel = new StackPanel { Margin = new Thickness(4) };
		panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
		Grid.SetRow(panel, row);
		Grid.SetColumn(panel, column);
		grid.Children.Add(panel);
		return panel;
	}

	private static Button Button(string text, bool primary)
	{
		return new Button
		{
			Content = text,
			MinWidth = 90,
			Height = 32,
			Padding = new Thickness(10, 3, 10, 3),
			Background = primary ? new SolidColorBrush(Color.FromRgb(18, 100, 171)) : Brushes.White,
			Foreground = primary ? Brushes.White : Brushes.Black
		};
	}

	private static bool TryNumber(string text, out double value)
	{
		return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
		       double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}
}
