using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Ctn;

internal static class PipeLayerRunner
{
	private const string Title = "TungMEP2 - PipeLayer";
	private static readonly double PointTolerance = UnitUtils.ConvertToInternalUnits(0.05, UnitTypeId.Millimeters);

	internal static void Run(UIDocument uiDocument, Document document)
	{
		if (document == null || document.IsReadOnly)
		{
			TaskDialog.Show(Title, "Document hiện tại không thể chỉnh sửa.");
			return;
		}
		ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
		List<Pipe> selectedPipes = selectedIds.Select(document.GetElement).OfType<Pipe>().ToList();
		if (selectedPipes.Count < 2)
		{
			TaskDialog.Show(Title, "Vui lòng chọn 2 ống trở lên trước khi chạy PipeLayer.");
			return;
		}
		Reference pickRef = uiDocument.Selection.PickObject(ObjectType.Element, new Isf.Pipe(), "Chọn ống gốc tại vị trí cần căn cao độ");
		Pipe targetPipe = (Pipe)document.GetElement(pickRef);
		if (selectedPipes.All((Pipe pipe2) => pipe2.Id != targetPipe.Id))
		{
			TaskDialog.Show(Title, "Ống gốc phải nằm trong danh sách đã chọn.");
			return;
		}
		if (!PipeLayerModeDialog.TryChooseMode(out PipeLayerMode mode)) return;
		if (pickRef.GlobalPoint == null)
		{
			TaskDialog.Show(Title, $"Không đọc được điểm bấm trên ống gốc {targetPipe.Id.Value}.");
			return;
		}
		if (!TryReadSegment(targetPipe, out PipeLayerSegment reference, out string error))
		{
			TaskDialog.Show(Title, $"Không thay đổi mô hình. Pipe gốc {targetPipe.Id.Value}: {error}");
			return;
		}

		var shifts = new Dictionary<long, double> { [targetPipe.Id.Value] = 0.0 };
		var originals = new Dictionary<long, PipeLayerSegment> { [targetPipe.Id.Value] = reference };
		var stations = new Dictionary<long, double>();
		var click = ToPoint(pickRef.GlobalPoint);
		foreach (Pipe pipe in selectedPipes.Where(item => item.Id != targetPipe.Id))
		{
			if (!TryReadSegment(pipe, out PipeLayerSegment segment, out error) ||
			    !PipeLayerMath.TryCompute(reference, segment, click, mode, out PipeLayerAlignment alignment, out error))
			{
				TaskDialog.Show(Title, $"Không thay đổi mô hình. Pipe {pipe.Id.Value}: {error}");
				return;
			}
			if (Math.Abs(alignment.DeltaZ) > PointTolerance &&
			    (pipe.Pinned || pipe.GroupId != ElementId.InvalidElementId ||
			     (document.IsWorkshared && WorksharingUtils.GetCheckoutStatus(document, pipe.Id) == CheckoutStatus.OwnedByOtherUser)))
			{
				TaskDialog.Show(Title, $"Không thay đổi mô hình. Pipe {pipe.Id.Value} bị Pin, nằm trong Group hoặc do người khác sở hữu.");
				return;
			}
			shifts[pipe.Id.Value] = Math.Abs(alignment.DeltaZ) <= PointTolerance ? 0.0 : alignment.DeltaZ;
			originals[pipe.Id.Value] = segment;
			stations[pipe.Id.Value] = alignment.CandidateParameter;
		}
		int moved = shifts.Count(item => Math.Abs(item.Value) > PointTolerance);
		if (moved == 0)
		{
			TaskDialog.Show(Title, $"Các ống đã đúng cao độ {ModeName(mode)} tại vị trí đo (sai số ≤ 0,05 mm).");
			return;
		}
		if (!PipeLayerModeDialog.TryChooseConnectionPolicy(out bool allowDisconnected)) return;
		if (!TryApply(document, selectedPipes, shifts, originals, targetPipe.Id, stations, allowDisconnected, out string relatedChanges, out error))
		{
			TaskDialog.Show(Title, "Không thay đổi mô hình. PipeLayer không áp dụng được: " + error);
			return;
		}
		TaskDialog.Show(Title, $"Đã căn {ModeName(mode)} cho {moved} ống.\nỐng gốc giữ nguyên; ống cần căn được co/giãn dọc trục, giữ hướng/độ dốc và vị trí đo." + relatedChanges);
	}

	private static bool TryApply(Document document, IReadOnlyList<Pipe> pipes,
		IReadOnlyDictionary<long, double> shifts, IReadOnlyDictionary<long, PipeLayerSegment> originals,
		ElementId referenceId, IReadOnlyDictionary<long, double> stations,
		bool allowDisconnected,
		out string relatedChanges, out string error)
	{
		relatedChanges = string.Empty;
		error = string.Empty;
		if (!PipeLayerSafety.TryCapture(document, pipes, out PipeLayerSafety baseline, out error)) return false;
		using var group = new TransactionGroup(document, "TungMEP2 - PipeLayer");
		using var transaction = new Transaction(document, "TungMEP2 - PipeLayer BOP/COP/TOP");
		var failures = new PipeLayerSafety.FailurePreprocessor(allowDisconnected);
		IReadOnlyList<string> beforeCommitIssues = null;
		IReadOnlyList<string> finalIssues = null;
		try
		{
			if (group.Start() != TransactionStatus.Started || transaction.Start() != TransactionStatus.Started)
				throw new InvalidOperationException("Không bắt đầu được transaction.");
			FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
			options.SetFailuresPreprocessor(failures);
			options.SetClearAfterRollback(true);
			options.SetForcedModalHandling(true);
			transaction.SetFailureHandlingOptions(options);

			foreach (Pipe originalPipe in pipes.Where(item => Math.Abs(shifts[item.Id.Value]) > PointTolerance))
			{
				Pipe pipe = document.GetElement(originalPipe.Id) as Pipe;
				if (pipe == null || !TryReadSegment(pipe, out PipeLayerSegment current, out string readError))
					throw new InvalidOperationException("Không đọc lại được Pipe trước khi dịch: " + originalPipe.Id.Value);
				PipeLayerSegment original = originals[pipe.Id.Value];
				if (!PipeLayerMath.TryReadAxialResizeShift(original, current, PointTolerance, out double currentShift, out string geometryError))
					throw new InvalidOperationException($"Ống cần căn {pipe.Id.Value}, trước khi dịch: {geometryError}");
				// Connected selected Pipes may already have moved: apply only the remaining Z delta.
				double remaining = shifts[pipe.Id.Value] - currentShift;
				if (Math.Abs(remaining) > PointTolerance)
					ElementTransformUtils.MoveElement(document, pipe.Id, XYZ.BasisZ.Multiply(remaining));
				document.Regenerate();
			}
			if (!baseline.Validate(document, shifts, PointTolerance, out string validationError, referenceId, stations, allowDisconnected))
				throw new InvalidOperationException("Sau khi dịch: " + validationError);
			beforeCommitIssues = baseline.GetConnectionIssues(document, PointTolerance);
			if (transaction.Commit() != TransactionStatus.Committed)
				throw new InvalidOperationException(string.IsNullOrEmpty(failures.Error) ? "Revit không commit được thay đổi." : failures.Error);
			// Revit may solve connected fittings at Commit; validate again before retaining the group.
			if (!baseline.Validate(document, shifts, PointTolerance, out validationError, referenceId, stations, allowDisconnected))
				throw new InvalidOperationException("Sau khi Revit commit: " + validationError);
			relatedChanges = baseline.DescribeRelatedChanges(document, shifts, PointTolerance);
			if (allowDisconnected)
			{
				finalIssues = baseline.GetConnectionIssues(document, PointTolerance);
				relatedChanges += "\nChế độ: ưu tiên căn cao độ — cho phép đầu hở.";
				if (finalIssues.Count == 0) relatedChanges += "\nKhông phát hiện mất kết nối hoặc lệch cổng mới.";
			}
			if (group.Assimilate() != TransactionStatus.Committed)
				throw new InvalidOperationException("Không lưu được nhóm thao tác PipeLayer.");
			if (finalIssues?.Count > 0)
			{
				string log = TryWriteConnectionLog(baseline, document, "Đã căn — cho phép đầu hở", finalIssues);
				// If persistence fails, do not hide repair IDs behind the UI limit.
				int displayCount = string.IsNullOrEmpty(log) ? finalIssues.Count : 8;
				if (string.IsNullOrEmpty(log)) relatedChanges += "\nKhông ghi được log; hiển thị đầy đủ các cặp cổng bên dưới.";
				relatedChanges += $"\nCẢNH BÁO: {finalIssues.Count} cặp cổng cần kiểm tra/nối lại thủ công.\n" +
				                  string.Join("\n", finalIssues.Take(displayCount)) +
				                  (finalIssues.Count > displayCount ? "\n..." : string.Empty) + log;
			}
			return true;
		}
		catch (Exception exception)
		{
			// Capture observed connector geometry before rollback removes the evidence.
			bool alreadyRolledBack = transaction.GetStatus() == TransactionStatus.RolledBack;
			bool groupAlreadyRolledBack = group.GetStatus() == TransactionStatus.RolledBack;
			string log = TryWriteConnectionLog(baseline, document, exception.Message +
				(groupAlreadyRolledBack ? "\nSố đo là snapshot TRƯỚC lưu nhóm; Revit đã tự hoàn tác nhóm." :
				 alreadyRolledBack ? "\nSố đo dưới đây là snapshot TRƯỚC commit; Revit đã tự hoàn tác lúc commit." : string.Empty),
				groupAlreadyRolledBack ? finalIssues ?? beforeCommitIssues : alreadyRolledBack ? beforeCommitIssues : null);
			relatedChanges = string.Empty;
			if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
			if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
			error = "Đã rollback toàn bộ: " + exception.Message + log;
			return false;
		}
	}

	private static string TryWriteConnectionLog(PipeLayerSafety baseline, Document document, string result,
		IReadOnlyList<string> issues = null)
	{
		try
		{
			issues ??= baseline.GetConnectionIssues(document, PointTolerance);
			string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TungMEP2", "Logs");
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, "PipeLayer-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log");
			File.WriteAllText(path, result + Environment.NewLine + string.Join(Environment.NewLine, issues), new UTF8Encoding(false));
			return "\nLog kết nối: " + path;
		}
		catch (Exception) { return string.Empty; } // Diagnostics must not change transaction outcome.
	}

	internal static bool TryReadSegment(Pipe pipe, out PipeLayerSegment segment, out string error)
	{
		segment = default;
		error = string.Empty;
		if (!(pipe?.Location is LocationCurve { Curve: Line line }))
		{
			error = "Pipe không có trục thẳng; chưa hỗ trợ Pipe cong.";
			return false;
		}
		Parameter outside = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
		if (outside == null || outside.StorageType != StorageType.Double || outside.AsDouble() <= 0)
		{
			error = "Không đọc được đường kính ngoài của Pipe.";
			return false;
		}
		segment = new PipeLayerSegment(ToPoint(line.GetEndPoint(0)), ToPoint(line.GetEndPoint(1)), outside.AsDouble());
		return true;
	}

	private static PipeLayerPoint ToPoint(XYZ point) => new PipeLayerPoint(point.X, point.Y, point.Z);
	private static string ModeName(PipeLayerMode mode) => mode == PipeLayerMode.Bop ? "BOP" : mode == PipeLayerMode.Cop ? "COP" : "TOP";
}
