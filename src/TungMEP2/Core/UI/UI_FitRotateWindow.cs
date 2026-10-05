using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using VP_RevitAPI.Helper;
using MediaColor = System.Windows.Media.Color;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace VP_RevitAPI.UI;

public sealed class UI_FitRotateWindow : Window
{
	private sealed class BoundaryConnectionRecord
	{
		internal BoundaryConnectionRecord(
			ElementId movingOwnerId,
			ElementId stationaryOwnerId,
			XYZ pivot,
			XYZ movingDirection,
			XYZ stationaryDirection,
			Domain domain)
		{
			MovingOwnerId = movingOwnerId;
			StationaryOwnerId = stationaryOwnerId;
			Pivot = pivot;
			MovingDirection = movingDirection;
			StationaryDirection = stationaryDirection;
			Domain = domain;
		}

		internal ElementId MovingOwnerId { get; }

		internal ElementId StationaryOwnerId { get; }

		internal XYZ Pivot { get; }

		internal XYZ MovingDirection { get; }

		internal XYZ StationaryDirection { get; }

		internal Domain Domain { get; }
	}

	private sealed class FitRotateFailurePreprocessor : IFailuresPreprocessor
	{
		private readonly List<string> _messages = new List<string>();

		private readonly HashSet<ElementId> _failingElementIds = new HashSet<ElementId>();

		public IReadOnlyList<string> Messages => _messages;

		public IReadOnlyCollection<ElementId> FailingElementIds => _failingElementIds;

		public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
		{
			bool hasError = false;
			foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
			{
				if (failure.GetSeverity() == FailureSeverity.Warning)
				{
					failuresAccessor.DeleteWarning(failure);
					continue;
				}

				hasError = true;
				string description = failure.GetDescriptionText();
				if (!string.IsNullOrWhiteSpace(description) && !_messages.Contains(description))
				{
					_messages.Add(description);
				}

				foreach (ElementId id in failure.GetFailingElementIds())
				{
					_failingElementIds.Add(id);
				}
			}

			return hasError
				? FailureProcessingResult.ProceedWithRollBack
				: FailureProcessingResult.Continue;
		}
	}

	private readonly Document _document;

	private readonly Line _axisLine;

	private readonly IReadOnlyList<ElementId> _fitIds;

	private readonly WpfTextBox _angleTextBox;

	private readonly Button _rotateLeftButton;

	private readonly Button _rotateRightButton;

	private readonly TextBlock _statusText;

	private bool _isRotating;

	private const double ConnectionTolerance = 0.0001;

	private const double DirectionDotTolerance = 0.999;

	public UI_FitRotateWindow(Document document, Line axisLine, ICollection<ElementId> fitIds)
	{
		_document = document ?? throw new ArgumentNullException(nameof(document));
		_axisLine = axisLine ?? throw new ArgumentNullException(nameof(axisLine));
		if (fitIds == null)
		{
			throw new ArgumentNullException(nameof(fitIds));
		}
		_fitIds = fitIds.Distinct().ToList();
		if (_fitIds.Count == 0)
		{
			throw new ArgumentException("Không có phần tử hợp lệ để xoay.", nameof(fitIds));
		}

		Title = "TungMEP2 - FitRotate";
		Width = 470.0;
		Height = 310.0;
		MinWidth = 470.0;
		MinHeight = 310.0;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;
		Background = new SolidColorBrush(MediaColor.FromRgb(246, 248, 251));
		FontFamily = new FontFamily("Segoe UI");

		WpfGrid root = new WpfGrid
		{
			Margin = new Thickness(16.0)
		};
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		Content = root;

		TextBlock title = new TextBlock
		{
			Text = "Xoay fitting quanh trục kết nối",
			FontSize = 17.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = new SolidColorBrush(MediaColor.FromRgb(30, 42, 60))
		};
		root.Children.Add(title);

		TextBlock hint = new TextBlock
		{
			Text = $"Đang xoay {_fitIds.Count} phần tử quanh đoạn ống/trục cố định đã chọn.",
			Margin = new Thickness(0.0, 5.0, 0.0, 14.0),
			Foreground = new SolidColorBrush(MediaColor.FromRgb(84, 96, 114)),
			TextWrapping = TextWrapping.Wrap
		};
		WpfGrid.SetRow(hint, 1);
		root.Children.Add(hint);

		Border settingsCard = new Border
		{
			Background = Brushes.White,
			BorderBrush = new SolidColorBrush(MediaColor.FromRgb(218, 223, 231)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0)
		};
		WpfGrid.SetRow(settingsCard, 2);
		root.Children.Add(settingsCard);

		WpfGrid settings = new WpfGrid();
		settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		settings.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105.0) });
		settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		settingsCard.Child = settings;

		TextBlock angleLabel = new TextBlock
		{
			Text = "Góc xoay (độ)",
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			FontWeight = FontWeights.SemiBold
		};
		settings.Children.Add(angleLabel);

		_angleTextBox = new WpfTextBox
		{
			Text = "45",
			Height = 30.0,
			Padding = new Thickness(7.0, 3.0, 7.0, 3.0),
			VerticalContentAlignment = VerticalAlignment.Center,
			HorizontalContentAlignment = HorizontalAlignment.Right
		};
		_angleTextBox.PreviewKeyDown += OnAngleKeyDown;
		WpfGrid.SetColumn(_angleTextBox, 1);
		settings.Children.Add(_angleTextBox);

		TextBlock usageHint = new TextBlock
		{
			Text = "Cách dùng: chọn fitting + toàn bộ nhánh cần di chuyển → bấm FitRotate → chọn đoạn ống thẳng đang nối trực tiếp và phải đứng yên. Sau đó chỉ dùng hai nút Xoay bên dưới.",
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
			Foreground = new SolidColorBrush(MediaColor.FromRgb(84, 96, 114)),
			TextWrapping = TextWrapping.Wrap
		};
		WpfGrid.SetRow(usageHint, 1);
		WpfGrid.SetColumnSpan(usageHint, 3);
		settings.Children.Add(usageHint);

		_statusText = new TextBlock
		{
			Text = "Sẵn sàng.",
			Margin = new Thickness(2.0, 10.0, 0.0, 0.0),
			Foreground = new SolidColorBrush(MediaColor.FromRgb(65, 83, 105)),
			TextWrapping = TextWrapping.Wrap
		};
		WpfGrid.SetRow(_statusText, 3);
		root.Children.Add(_statusText);

		StackPanel actions = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		_rotateLeftButton = CreateActionButton("↶  Xoay trái", true);
		_rotateLeftButton.Click += OnRotateLeft;
		_rotateRightButton = CreateActionButton("Xoay phải  ↷", true);
		_rotateRightButton.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		_rotateRightButton.Click += OnRotateRight;
		Button close = CreateActionButton("Đóng", false);
		close.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		close.Click += delegate { Close(); };
		actions.Children.Add(_rotateLeftButton);
		actions.Children.Add(_rotateRightButton);
		actions.Children.Add(close);
		WpfGrid.SetRow(actions, 4);
		root.Children.Add(actions);

		Loaded += delegate
		{
			_angleTextBox.Focus();
			_angleTextBox.SelectAll();
		};
	}

	private static Button CreateActionButton(string text, bool primary)
	{
		return new Button
		{
			Content = text,
			MinWidth = primary ? 105.0 : 75.0,
			Height = 32.0,
			Padding = new Thickness(10.0, 3.0, 10.0, 3.0),
			Background = primary
				? new SolidColorBrush(MediaColor.FromRgb(18, 100, 171))
				: Brushes.White,
			Foreground = primary
				? Brushes.White
				: new SolidColorBrush(MediaColor.FromRgb(35, 47, 64)),
			BorderBrush = new SolidColorBrush(MediaColor.FromRgb(175, 184, 198))
		};
	}

	private void OnAngleKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			Rotate(1.0);
			e.Handled = true;
		}
	}

	private void OnRotateLeft(object sender, RoutedEventArgs e)
	{
		Rotate(1.0);
	}

	private void OnRotateRight(object sender, RoutedEventArgs e)
	{
		Rotate(-1.0);
	}

	private void Rotate(double sign)
	{
		if (_isRotating)
		{
			return;
		}

		if (!TryGetAngleRadians(out double angleRadians))
		{
			TaskDialog.Show("FitRotate", "Góc xoay không hợp lệ. Hãy nhập một số khác 0.");
			_angleTextBox.Focus();
			_angleTextBox.SelectAll();
			return;
		}

		List<ElementId> existingIds = _fitIds
			.Where(id => _document.GetElement(id) != null)
			.ToList();
		if (existingIds.Count != _fitIds.Count)
		{
			ShowRotateFailure(
				"Một số phần tử đã bị xóa hoặc không còn tồn tại. Hãy đóng FitRotate, chọn lại cụm rồi chạy lại lệnh.",
				null);
			return;
		}

		_isRotating = true;
		_rotateLeftButton.IsEnabled = false;
		_rotateRightButton.IsEnabled = false;
		_statusText.Text = "Đang kiểm tra kết nối và xoay...";
		_statusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(65, 83, 105));

		try
		{
			using Transaction transaction = new Transaction(_document, "FitRotate");
			FitRotateFailurePreprocessor failurePreprocessor = new FitRotateFailurePreprocessor();
			if (transaction.Start() != TransactionStatus.Started)
			{
				ShowRotateFailure("Không thể bắt đầu transaction FitRotate.", null);
				return;
			}

			FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
			failureOptions.SetFailuresPreprocessor(failurePreprocessor);
			failureOptions.SetClearAfterRollback(true);
			transaction.SetFailureHandlingOptions(failureOptions);

			try
			{
				if (!TryCaptureBoundaryConnections(existingIds, out List<BoundaryConnectionRecord> boundaryConnections, out string boundaryError))
				{
					throw new InvalidOperationException(boundaryError);
				}

				DisconnectBoundaryConnections(boundaryConnections);
				_document.Regenerate();

				ElementTransformUtils.RotateElements(
					_document,
					existingIds,
					_axisLine,
					sign * angleRadians);
				_document.Regenerate();

				ReconnectBoundaryConnections(boundaryConnections);
				_document.Regenerate();

				TransactionStatus commitStatus = transaction.Commit();
				if (commitStatus != TransactionStatus.Committed)
				{
					ShowRotateFailure(BuildFailureMessage(failurePreprocessor), failurePreprocessor);
					return;
				}

				double angleDegrees = UnitUtils.ConvertFromInternalUnits(angleRadians, UnitTypeId.Degrees);
				_statusText.Text = $"Đã xoay {existingIds.Count} phần tử {angleDegrees:0.###}°. Có thể bấm tiếp để xoay thêm.";
				_statusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(25, 125, 75));
			}
			catch (Exception exception)
			{
				if (transaction.GetStatus() == TransactionStatus.Started)
				{
					transaction.RollBack();
				}
				ShowRotateFailure(exception.Message, failurePreprocessor);
			}
		}
		catch (Exception exception)
		{
			ShowRotateFailure(exception.Message, null);
		}
		finally
		{
			_isRotating = false;
			_rotateLeftButton.IsEnabled = true;
			_rotateRightButton.IsEnabled = true;
		}
	}

	private bool TryCaptureBoundaryConnections(
		IReadOnlyCollection<ElementId> movingIds,
		out List<BoundaryConnectionRecord> records,
		out string error)
	{
		records = new List<BoundaryConnectionRecord>();
		error = string.Empty;
		HashSet<ElementId> movingIdSet = movingIds.ToHashSet();
		XYZ axisStart = _axisLine.GetEndPoint(0);
		XYZ axisEnd = _axisLine.GetEndPoint(1);

		foreach (ElementId movingId in movingIds)
		{
			Element movingElement = _document.GetElement(movingId);
			foreach (Connector movingConnector in GetPhysicalConnectors(movingElement))
			{
				foreach (Connector stationaryConnector in movingConnector.AllRefs.Cast<Connector>())
				{
					Element stationaryOwner = stationaryConnector?.Owner;
					if (stationaryOwner == null ||
						movingIdSet.Contains(stationaryOwner.Id) ||
						stationaryOwner.Id == movingElement.Id ||
						stationaryConnector.ConnectorType == ConnectorType.Logical ||
						stationaryConnector.ConnectorType == ConnectorType.Surface ||
						!movingConnector.IsConnectedTo(stationaryConnector))
					{
						continue;
					}

					XYZ movingDirection = GetConnectorDirection(movingConnector);
					XYZ stationaryDirection = GetConnectorDirection(stationaryConnector);
					if (!FitRotateRotationGeometry.CanRotateConnectedBranch(
							axisStart,
							axisEnd,
							movingConnector.Origin,
							movingDirection,
							ConnectionTolerance))
					{
						error =
							"Có connector nối ra ngoài cụm nhưng tâm connector không nằm trên trục xoay 3D. " +
							"Hãy chọn thêm phần tử đó vào cụm hoặc chọn lại đúng đoạn trục đang nối trực tiếp.";
						return false;
					}

					XYZ axisDirection = (axisEnd - axisStart).Normalize();
					if (Math.Abs(movingDirection.Normalize().DotProduct(axisDirection)) < DirectionDotTolerance ||
						Math.Abs(stationaryDirection.Normalize().DotProduct(axisDirection)) < DirectionDotTolerance)
					{
						error =
							"Connector biên không cùng phương với trục xoay nên không thể ngắt và nối lại an toàn. " +
							"Lưu ý: nhánh phía sau connector vẫn được phép nghiêng góc bất kỳ; chỉ connector gắn vào trục phải đồng trục.";
						return false;
					}

					bool duplicate = records.Any(record =>
						record.MovingOwnerId == movingElement.Id &&
						record.StationaryOwnerId == stationaryOwner.Id &&
						record.Pivot.DistanceTo(movingConnector.Origin) <= ConnectionTolerance);
					if (!duplicate)
					{
						records.Add(new BoundaryConnectionRecord(
							movingElement.Id,
							stationaryOwner.Id,
							movingConnector.Origin,
							movingDirection,
							stationaryDirection,
							movingConnector.Domain));
					}
				}
			}
		}

		return true;
	}

	private void DisconnectBoundaryConnections(IEnumerable<BoundaryConnectionRecord> records)
	{
		foreach (BoundaryConnectionRecord record in records)
		{
			Connector movingConnector = FindMatchingConnector(
				record.MovingOwnerId,
				record.Pivot,
				record.MovingDirection,
				record.Domain);
			Connector stationaryConnector = FindMatchingConnector(
				record.StationaryOwnerId,
				record.Pivot,
				record.StationaryDirection,
				record.Domain);

			if (!movingConnector.IsConnectedTo(stationaryConnector))
			{
				throw new InvalidOperationException("Không tìm thấy đúng cặp connector biên đang nối trước khi xoay.");
			}

			movingConnector.DisconnectFrom(stationaryConnector);
		}
	}

	private void ReconnectBoundaryConnections(IEnumerable<BoundaryConnectionRecord> records)
	{
		foreach (BoundaryConnectionRecord record in records)
		{
			Connector movingConnector = FindMatchingConnector(
				record.MovingOwnerId,
				record.Pivot,
				record.MovingDirection,
				record.Domain);
			Connector stationaryConnector = FindMatchingConnector(
				record.StationaryOwnerId,
				record.Pivot,
				record.StationaryDirection,
				record.Domain);

			if (movingConnector.Origin.DistanceTo(stationaryConnector.Origin) > ConnectionTolerance)
			{
				throw new InvalidOperationException(
					"Sau khi xoay, connector biên không còn trùng tâm nên lệnh đã được rollback để không làm hỏng hệ ống.");
			}

			if (!movingConnector.IsConnectedTo(stationaryConnector))
			{
				movingConnector.ConnectTo(stationaryConnector);
			}

			if (!movingConnector.IsConnectedTo(stationaryConnector))
			{
				throw new InvalidOperationException(
					"Không thể nối lại connector biên sau khi xoay; toàn bộ thay đổi đã được rollback.");
			}
		}
	}

	private Connector FindMatchingConnector(
		ElementId ownerId,
		XYZ origin,
		XYZ expectedDirection,
		Domain domain)
	{
		Element owner = _document.GetElement(ownerId);
		Connector connector = GetPhysicalConnectors(owner)
			.Where(candidate => candidate.Domain == domain)
			.Where(candidate => candidate.Origin.DistanceTo(origin) <= ConnectionTolerance)
			.OrderByDescending(candidate =>
				Math.Abs(GetConnectorDirection(candidate)
					.Normalize()
					.DotProduct(expectedDirection.Normalize())))
			.FirstOrDefault();

		if (connector == null)
		{
			throw new InvalidOperationException(
				$"Không tìm thấy lại connector tại điểm nối trên phần tử ID {ownerId.IntegerValue}.");
		}

		return connector;
	}

	private static XYZ GetConnectorDirection(Connector connector)
	{
		XYZ direction = connector?.CoordinateSystem?.BasisZ;
		if (direction == null || direction.GetLength() <= 1E-09)
		{
			throw new InvalidOperationException("Connector không có phương 3D hợp lệ để FitRotate.");
		}

		return direction.Normalize();
	}

	private static List<Connector> GetPhysicalConnectors(Element element)
	{
		ConnectorSet connectorSet = element?.GetConnectors();
		if (connectorSet == null)
		{
			return new List<Connector>();
		}

		List<Connector> connectors = new List<Connector>();
		foreach (Connector connector in connectorSet)
		{
			if (connector.ConnectorType != ConnectorType.Surface &&
				connector.ConnectorType != ConnectorType.Logical)
			{
				connectors.Add(connector);
			}
		}

		return connectors;
	}

	private string BuildFailureMessage(FitRotateFailurePreprocessor failurePreprocessor)
	{
		if (failurePreprocessor.Messages.Count > 0)
		{
			return string.Join(Environment.NewLine, failurePreprocessor.Messages);
		}

		return "Revit không cho phép xoay cụm ở trạng thái kết nối hiện tại.";
	}

	private void ShowRotateFailure(string message, FitRotateFailurePreprocessor failurePreprocessor)
	{
		_statusText.Text = "Không thể xoay — toàn bộ thay đổi đã được rollback.";
		_statusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(180, 45, 45));

		List<string> failingElements = new List<string>();
		if (failurePreprocessor != null)
		{
			foreach (ElementId id in failurePreprocessor.FailingElementIds)
			{
				Element element = _document.GetElement(id);
				string category = element?.Category?.Name ?? "Không rõ category";
				string name = element?.Name ?? "Không rõ tên";
				failingElements.Add($"{category} — {name} — ID {id.IntegerValue}");
			}
		}

		string expandedContent =
			"Không có phần tử nào bị xóa hoặc giữ lại ở trạng thái xoay dở.\n\n" +
			"Kiểm tra lại:\n" +
			"• Chọn fitting cùng toàn bộ ống/fitting của nhánh cần di chuyển.\n" +
			"• Chọn đoạn ống thẳng nối trực tiếp với cụm làm trục cố định.\n" +
			"• Bỏ Pin và các dimension/constraint đang khóa hình học.";
		if (failingElements.Count > 0)
		{
			expandedContent += "\n\nPhần tử Revit báo lỗi:\n" + string.Join("\n", failingElements);
		}

		TaskDialog dialog = new TaskDialog("TungMEP2 - FitRotate")
		{
			MainInstruction = "Không thể xoay cụm đã chọn",
			MainContent = string.IsNullOrWhiteSpace(message)
				? "Revit đang giữ một kết nối hoặc ràng buộc không cho phép xoay."
				: message,
			ExpandedContent = expandedContent,
			CommonButtons = TaskDialogCommonButtons.Close
		};
		dialog.Show();
	}

	private bool TryGetAngleRadians(out double angleRadians)
	{
		angleRadians = 0.0;
		string text = _angleTextBox.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double angleDegrees) &&
			!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out angleDegrees))
		{
			return false;
		}
		if (double.IsNaN(angleDegrees) ||
			double.IsInfinity(angleDegrees) ||
			Math.Abs(angleDegrees) < 1E-09)
		{
			return false;
		}
		angleRadians = UnitUtils.ConvertToInternalUnits(
			Math.Abs(angleDegrees),
			UnitTypeId.Degrees);
		return true;
	}
}
