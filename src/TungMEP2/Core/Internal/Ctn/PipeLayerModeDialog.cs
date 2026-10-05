using Autodesk.Revit.UI;

namespace VP_RevitAPI.Internal.Ctn;

// Kept separate from Revit model operations so the UI initialization contract
// and mode/cancellation mapping can be exercised without a live document.
internal static class PipeLayerModeDialog
{
	internal static bool TryChooseConnectionPolicy(out bool allowDisconnected)
	{
		allowDisconnected = false;
		var dialog = new TaskDialog("PipeLayer — Kết nối")
		{
			MainInstruction = "Chọn cách xử lý kết nối",
			MainContent = "Cả hai chế độ đều giữ nguyên ống gốc, hướng/độ dốc và cao độ đích của ống cần căn.\n" +
			              "Cho phép đầu hở: giữ kết quả căn nếu Revit làm mất kết nối hoặc cổng bị lệch.\n" +
			              "Lệnh báo các cặp cổng cần kiểm tra; không chủ động tháo nối và không tự nối lại.\n" +
			              "Cảnh báo của Revit vẫn có thể xuất hiện để bạn xem xét.",
			CommonButtons = TaskDialogCommonButtons.Cancel
		};
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Giữ kết nối — Không đạt thì hoàn tác (mặc định)");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Ưu tiên căn cao độ — Cho phép đầu hở");
		dialog.DefaultButton = TaskDialogResult.CommandLink1;
		TaskDialogResult result = dialog.Show();
		allowDisconnected = result == TaskDialogResult.CommandLink2;
		return result == TaskDialogResult.CommandLink1 || result == TaskDialogResult.CommandLink2;
	}

	internal static bool TryChooseMode(out PipeLayerMode mode)
	{
		mode = PipeLayerMode.Bop;
		var dialog = new TaskDialog("TungMEP2 - PipeLayer")
		{
			MainInstruction = "Chọn kiểu căn cao độ",
			MainContent = "Giữ cố định ống gốc, chỉ nâng/hạ các ống khác.\n" +
			              "Ống cần căn được co/giãn dọc trục, giữ hướng/độ dốc và điểm đo.\n" +
			              "Ống ngoài vùng chọn/fitting/phụ kiện liên quan có thể được Revit điều chỉnh để giữ kết nối.\n" +
			              "Điểm đo là điểm gần nhất theo mặt bằng, trong phạm vi đoạn ống thật.\n" +
			              "BOP/TOP dùng đường kính ngoài ống, không bao gồm bảo ôn.",
			CommonButtons = TaskDialogCommonButtons.Cancel
		};
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "BOP — Căn đáy ngoài ống (mặc định)");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "COP — Căn tim ống");
		dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "TOP — Căn đỉnh ngoài ống");
		// Revit 2024 validates the default immediately; register its link first.
		dialog.DefaultButton = TaskDialogResult.CommandLink1;
		TaskDialogResult result = dialog.Show();
		if (result == TaskDialogResult.CommandLink2) mode = PipeLayerMode.Cop;
		else if (result == TaskDialogResult.CommandLink3) mode = PipeLayerMode.Top;
		return result == TaskDialogResult.CommandLink1 || result == TaskDialogResult.CommandLink2 ||
		       result == TaskDialogResult.CommandLink3;
	}
}
