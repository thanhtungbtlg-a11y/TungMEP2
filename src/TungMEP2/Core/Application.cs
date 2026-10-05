using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using Autodesk.Revit.UI;
using Nice3point.Revit.Extensions.UI;
using Nice3point.Revit.Toolkit.External;
using VP_RevitAPI.Commands.COMBINE;
using VP_RevitAPI.Commands.CTN;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Commands.DWG;
using VP_RevitAPI.Commands.Electrical;
using VP_RevitAPI.Commands.FF;
using VP_RevitAPI.Commands.HVAC;
using VP_RevitAPI.Commands.LAYOUT;
using VP_RevitAPI.UI;

namespace VP_RevitAPI;

public class Application : ExternalApplication
{
	private static readonly Dictionary<string, string> ButtonTooltips = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["Align3D"] = "Căn chỉnh các phần tử theo không gian 3D.",
		["Align2D"] = "Căn chỉnh các phần tử theo mặt phẳng hiện tại.",
		["AConnect"] = "Kết nối nhanh các đầu nối MEP theo cặp.",
		["MoveConn"] = "Dịch chuyển Pipe/MEP element thứ hai để cặp connector trống gần nhất đã đối hướng gặp nhau và kết nối.",
		["MC-Align"] = "Căn và di chuyển toàn bộ cụm MEP. Ưu tiên nối hai đầu hở; nếu Pipe mốc kín hai đầu và Pipe thứ hai còn đầu hở, tool chèn Tee/Y vào thân Pipe.",
		["AlignMEP3D"] = "Căn thẳng trục connector trong 3D và di chuyển toàn bộ cụm MEP liên thông; không tạo kết nối mới.",
		["AlignBranch"] = "Dịch chuyển nhánh để tim nhánh giao tim chính trong 3D, giữ nguyên phương và độ dốc hiện tại.",
		["AlignBranch+"] = "Xoay nhánh vuông góc với tim chính theo mặt bằng, lấy cùng độ dốc rồi đưa hai tim về giao nhau.",
		["Extend"] = "Kéo dài fitting hoặc ống theo hướng nối.",
		["SplitGap"] = "Tách phần tử và chèn khoảng hở theo giá trị đặt trước.",
		["FitRotate"] = "Xoay fitting để khớp hướng kết nối.",
		["Up45"] = "Tạo nhánh/đổi hướng đi lên 45 độ.",
		["Left"] = "Tạo nhánh/đổi hướng sang trái.",
		["Dw45"] = "Tạo nhánh/đổi hướng đi xuống 45 độ.",
		["Up90"] = "Tạo nhánh/đổi hướng đi lên 90 độ.",
		["Right"] = "Tạo nhánh/đổi hướng sang phải.",
		["Dw90"] = "Tạo nhánh/đổi hướng đi xuống 90 độ.",
		["E90to45"] = "Chuyển cấu hình co từ 90 độ sang 45 độ.",
		["E45to90"] = "Chuyển cấu hình co từ 45 độ sang 90 độ.",
		["FitSpace"] = "Chuẩn hóa khoảng cách cụm hai elbow tại chỗ; giữ nguyên ID, loại, DN, system và hai đầu Pipe ngoài.",
		["PipeLayer"] = "Căn BOP/COP/TOP cho ống khác phương; giữ ống gốc, hướng/độ dốc và điểm đo. Chọn Giữ kết nối hoặc Ưu tiên căn cao độ — cho phép đầu hở, báo cặp cổng cần xử lý, không tự nối lại. Chọn các ống trước, rồi bấm ống gốc tại vị trí cần căn.",
		["PipeSpacing"] = "Dàn các Pipe song song theo khoảng hở thực; giữ Pipe biên lớn hơn và kéo theo cụm fitting/nhánh tới ranh giới Tee, Wye, thiết bị.",
		["FixPipe"] = "Sửa lỗi hình học và kết nối ống thường gặp.",
		["QRPipe3"] = "Chạy quy trình xử lý nhanh đường ống phiên bản 3.",
		["QRPipe2"] = "Chạy quy trình xử lý nhanh đường ống phiên bản 2.",
		["1E1Y"] = "Tạo cụm 1 co + 1 nhánh Y cho tuyến ống.",
		["Auto90"] = "Tự xoay và căn tim nhánh trong 3D, tạo Tee 90° rồi nối; không nhập góc. Ưu tiên xoay trước, chỉ chỉnh cao độ nhỏ nhất theo bước 0,5 mm; giữ tuyến chính.",
		["DirectY"] = "Chọn Pipe tuyến chính, rồi Pipe nhánh có đầu hở. Dùng Y hở phù hợp hoặc tự tạo Y 45° theo Routing Preferences. Không bắt buộc cùng System Type; Revit quyết định nối hệ tương thích. Giữ elbow/con thu; ưu tiên xoay, sau đó thử cao độ từng bước 5 mm trong ±100 mm. Dọn phần tử tạm có kiểm tra ID; lỗi có log. Rollback nếu không giữ được kết nối.",
		["2E1Y"] = "Tạo cụm 2 co + 1 nhánh Y cho tuyến ống.",
		["2E1Yv1"] = "Tạo cụm 2E1Y theo thuật toán phiên bản 1.",
		["2E1Yv2"] = "Tạo cụm 2E1Y theo thuật toán phiên bản 2.",
		["Branch"] = "Tạo nhánh rẽ từ tuyến ống chính.",
		["DisVent"] = "Ngắt kết nối vent khỏi hệ hiện tại.",
		["Vent"] = "Tạo hoặc nối vent cho tuyến ống.",
		["Ven90"] = "Xử lý nhánh vent với góc 90 độ.",
		["CapEnd"] = "Bịt đầu cuối ống bằng phụ kiện phù hợp.",
		["RenTrong"] = "Xử lý ren trong cho đầu nối ống.",
		["Đặt DN SK"] = "Đặt đường kính danh nghĩa theo SK đã chọn.",
		["AutoTagPipe"] = "Tự động đặt tag cho các ống trong vùng chọn.",
		["SKPipe1"] = "Lọc và xử lý nhóm ống SKPipe1.",
		["ASKPipe1"] = "Lọc và xử lý nhóm ống ASKPipe1.",
		["ASKPipe2"] = "Lọc và xử lý nhóm ống ASKPipe2.",
		["GridPlace"] = "Rải phần tử theo lưới dựa trên hai điểm chuẩn.",
		["GridPlace Settings"] = "Thiết lập thông số rải phần tử theo lưới.",
		["Nối tap"] = "Nối tap vào tuyến ống gió theo điểm chọn.",
		["TemPlane"] = "Tạo mặt phẳng tạm phục vụ thao tác dựng hình.",
		["SaveSync"] = "Lưu trạng thái đồng bộ view hiện tại.",
		["ViewSync"] = "Đồng bộ view hiện tại theo trạng thái đã lưu.",
		["SectionBoxSlide"] = "Dịch chuyển section box theo bước cố định.",
		["3DSync"] = "Đồng bộ nhanh góc nhìn 3D giữa các view.",
		["SetWorkset"] = "Gán workset cho phần tử theo lựa chọn.",
		["Reroute"] = "Đi lại tuyến để tránh va chạm một phần tử.",
		["RerouteM"] = "Đi lại tuyến để tránh va chạm cho nhiều phần tử.",
		["CopyCir"] = "Sao chép mạch điện giữa các thiết bị tương ứng.",
		["MergeCir"] = "Gộp nhiều mạch điện thành một mạch.",
		["RemoveCir"] = "Xóa mạch điện khỏi phần tử đã chọn.",
		["Similar"] = "Chọn parameter E_Area, E_LoadName hoặc E_Wire để lọc các phần tử cùng giá trị trong view hiện tại.",
		["CopyData"] = "Sao chép E_Area, E_Wire, E_LoadName từ phần tử nguồn sang phần tử đích.",
		["ObjectDetection"] = "Tìm phần tử thiếu E_Area, E_Wire, E_LoadName và chọn theo danh sách.",
		["FindReplaceP"] = "Tìm và thay thế text trong các parameter dạng chuỗi của element đang chọn.",
		["ADTags-H"] = "Căn chỉnh mũi tên tag theo phương ngang.",
		["ADTags-V"] = "Căn chỉnh mũi tên tag theo phương dọc.",
		["DWG"] = "Mở và nạp lại liên kết DWG trong dự án.",
		["CropViewPort"] = "Thiết lập crop region cho viewport nhanh.",
		["CropLineWeight"] = "Điều chỉnh lineweight của đường crop view.",
		["DimCache"] = "Tạo cache dữ liệu để chạy auto dim.",
		["AutoDimCache"] = "Tạo dim tự động từ dữ liệu cache.",
		["DimFPoint"] = "Dim tới điểm tham chiếu của family instance.",
		["DimViewFloor"] = "Dim lưới theo biên trên mặt bằng.",
		["CopyGridViewFloor"] = "Sao chép lưới giữa các view mặt bằng.",
		["CoverGrid2D"] = "Phủ lưới 2D cho view theo thiết lập hiện tại.",
		["ExcelLegendSync"] = "Đồng bộ dữ liệu legend giữa Revit và Excel.",
		["DuplicateSheets"] = "Nhân bản sheet hàng loạt theo cấu hình.",
		["AlignSheets"] = "Căn chỉnh vị trí viewport giữa các sheet.",
		["PutViewsInSheet"] = "Đặt nhiều view vào sheet tự động.",
		["CopyCropView"] = "Sao chép crop từ source view sang target view.",
		["CopyVG"] = "Sao chép thiết lập VG từ source view sang target view.",
		["OpenViewInSheet"] = "Mở nhanh view tương ứng từ sheet.",
		["SetSTT"] = "Đánh số thứ tự cho phần tử hoặc sheet.",
		["IncrementSN"] = "Tăng nhanh số thứ tự đang chọn.",
		["DecrementSN"] = "Giảm nhanh số thứ tự đang chọn.",
		["CTitleBlockType"] = "Đổi loại title block cho nhiều sheet.",
		["CheckListTitleBlocks"] = "Kiểm tra danh sách title block trong dự án.",
		["CreatePrintSet"] = "Tạo tập in từ các sheet được chọn.",
		["PrintSheets"] = "In hàng loạt sheet theo tập in.",
		["TransTemp"] = "Chuyển đổi dữ liệu tạm theo quy tắc nội bộ.",
		["SelectByLevel"] = "Lọc phần tử theo level, chọn theo category hoặc đổi level theo phạm vi chọn.",
		["SelectByIDs"] = "Chọn nhanh phần tử theo danh sách ElementId dán từ clipboard."
	};

	private static readonly Dictionary<string, string> GroupTooltips = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["GridPlace"] = "Nhóm công cụ rải phần tử theo lưới.",
		["ViewSync"] = "Nhóm công cụ lưu và đồng bộ view.",
		["DimCache"] = "Nhóm công cụ tạo và dùng dữ liệu cache dim.",
		["ViewFloorTools"] = "Nhóm công cụ xử lý lưới trên view mặt bằng.",
		["KHÁC"] = "Các lệnh ít dùng thuộc HVAC, Electrical và Checker."
	};

	public override void OnStartup()
	{
		InitializeWpfInfrastructure();
		CreateRibbon();
		base.Application.Idling += RevitPipeSlopeRibbonState.OnIdling;
	}

	public override void OnShutdown()
	{
		base.Application.Idling -= RevitPipeSlopeRibbonState.OnIdling;
	}

	private static void InitializeWpfInfrastructure()
	{
		UI_HandyControlBootstrapper.EnsureInitialized();
	}

	private void CreateRibbon()
	{
		List<RibbonPanel> createdPanels = new List<RibbonPanel>();
		string assemblyPath = Assembly.GetExecutingAssembly().Location;

		// PLUMBING is the primary workspace. Every existing Common, Draw 3D and
		// Plumbing command stays directly visible; this panel has no dropdown.
		RibbonPanel plumbingRibbonPanel = base.Application.CreatePanel("PLUMBING", "TungMEP2");
		createdPanels.Add(plumbingRibbonPanel);
		IRibbonStackPanel ribbonStackPanel_Plumbing = plumbingRibbonPanel.AddStackPanel();
		ribbonStackPanel_Plumbing.AddPushButton<Command_Align3D>("Align3D").SetImage("/TungMEP2;component/Resources/Icons/Align3D16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Align3D32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_AlignCurve>("Align2D").SetImage("/TungMEP2;component/Resources/Icons/Align3D16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Align3D32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_ConnectMaster>("AConnect").SetImage("/TungMEP2;component/Resources/Icons/ConnectTo16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/ConnectTo32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_MoveConnect>("MoveConn").SetImage("/TungMEP2;component/Resources/Icons/ConnectTo16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/ConnectTo32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_MoveConnectAlign>("MC-Align").SetImage("/TungMEP2;component/Resources/Icons/Align3D16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Align3D32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_AlignMep3D>("AlignMEP3D").SetImage("/TungMEP2;component/Resources/Icons/Align3D16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Align3D32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_AlignBranch>("AlignBranch").SetImage("/TungMEP2;component/Resources/Icons/LayerPipe_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LayerPipe_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_AlignBranchPlus>("AlignBranch+").SetImage("/TungMEP2;component/Resources/Icons/E9045_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/E9045_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_ExtendFitting>("Extend").SetImage("/TungMEP2;component/Resources/Icons/ExtendFitting16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/ExtendFitting32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_SplitWithGap>("SplitGap").SetImage("/TungMEP2;component/Resources/Icons/AvoidMEP_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/AvoidMEP_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_FitRotate>("FitRotate").SetImage("/TungMEP2;component/Resources/Icons/FittingRotate_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/FittingRotate_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_ElbowUp45>("Up45").SetImage("/TungMEP2;component/Resources/Icons/Up45arrow_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Up45arrow_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_ElbowUp>("Up90").SetImage("/TungMEP2;component/Resources/Icons/Up90arrow_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Up90arrow_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_ElbowLeft>("Left").SetImage("/TungMEP2;component/Resources/Icons/Left45arrow_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Left45arrow_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_ElbowRight>("Right").SetImage("/TungMEP2;component/Resources/Icons/Right45arrow_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Right45arrow_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_ElbowDown45>("Dw45").SetImage("/TungMEP2;component/Resources/Icons/Down45arrow_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Down45arrow_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_ElbowDown>("Dw90").SetImage("/TungMEP2;component/Resources/Icons/Down90arrow_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Down90arrow_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Elbow90To45>("E90to45").SetImage("/TungMEP2;component/Resources/Icons/E9045_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/E9045_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Elbow45To90>("E45to90").SetImage("/TungMEP2;component/Resources/Icons/E9045_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/E9045_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_FitSpace>("FitSpace").SetImage("/TungMEP2;component/Resources/Icons/E9045_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/E9045_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_PipeLayer>("PipeLayer").SetImage("/TungMEP2;component/Resources/Icons/LayerPipe_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LayerPipe_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_PipeSpacing>("PipeSpacing").SetImage("/TungMEP2;component/Resources/Icons/LayerPipe_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LayerPipe_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_FixPipe>("FixPipe").SetImage("/TungMEP2;component/Resources/Icons/FixPipe_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/FixPipe_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_QRPipe3>("QRPipe3").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_QRPipe2>("QRPipe2").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Gom1E1Y>("1E1Y").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_DirectY>("DirectY").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Auto90>("Auto90").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Gom2E1Y>("2E1Y").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Gom2E1Yv1>("2E1Yv1").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Gom2E1Yv2>("2E1Yv2").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Branch>("Branch").SetImage("/TungMEP2;component/Resources/Icons/EY_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/EY_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_DisconnectVent>("DisVent").SetImage("/TungMEP2;component/Resources/Icons/DisVent_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/DisVent_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Vent>("Vent").SetImage("/TungMEP2;component/Resources/Icons/Vent_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Vent_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Ven90>("Ven90").SetImage("/TungMEP2;component/Resources/Icons/Vent_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Vent_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_Thongtac>("CapEnd").SetImage("/TungMEP2;component/Resources/Icons/DisVent_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/DisVent_32.png");
		ribbonStackPanel_Plumbing.AddPushButton<Command_RenTrong>("RenTrong").SetImage("/TungMEP2;component/Resources/Icons/E9045_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/E9045_32.png");

		// PCCC also remains fully expanded. GridPlace run and settings are
		// separate visible buttons so no frequently used fire command is hidden.
		RibbonPanel fireFightingRibbonPanel = base.Application.CreatePanel("PCCC", "TungMEP2");
		createdPanels.Add(fireFightingRibbonPanel);
		IRibbonStackPanel ribbonStackPanel_Fire = fireFightingRibbonPanel.AddStackPanel();
		ribbonStackPanel_Fire.AddPushButton<Command_SetDnBySkSelect>("Đặt DN SK").SetImage("/TungMEP2;component/Resources/Icons/LayerPipe_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LayerPipe_32.png");
		ribbonStackPanel_Fire.AddPushButton<Command_AutoTagPipe>("AutoTagPipe").SetImage("/TungMEP2;component/Resources/Icons/LayerPipe_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LayerPipe_32.png");
		ribbonStackPanel_Fire.AddPushButton<Command_GridPlaceElement_Run>("GridPlace").SetImage("/TungMEP2;component/Resources/Icons/LineWeight_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LineWeight_32.png");
		ribbonStackPanel_Fire.AddPushButton<Command_GridPlaceElement_Settings>("GridPlace Settings").SetImage("/TungMEP2;component/Resources/Icons/SetWorkSet_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/SetWorkSet_32.png");
		ribbonStackPanel_Fire.AddPushButton<Command_SKPipe1>("SKPipe1").SetImage("/TungMEP2;component/Resources/Icons/ConnectTo16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/ConnectTo32.png");
		ribbonStackPanel_Fire.AddPushButton<Command_ASKPipe1>("ASKPipe1").SetImage("/TungMEP2;component/Resources/Icons/ConnectTo16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/ConnectTo32.png");
		ribbonStackPanel_Fire.AddPushButton<Command_ASKPipe2>("ASKPipe2").SetImage("/TungMEP2;component/Resources/Icons/ConnectTo16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/ConnectTo32.png");

		RibbonPanel combineRibbonPanel = base.Application.CreatePanel("VIEW & COORD", "TungMEP2");
		createdPanels.Add(combineRibbonPanel);
		PushButtonData temPlaneButtonData = new PushButtonData("TemPlaneButton", "TemPlane", assemblyPath, typeof(Command_TemporaryPlane).FullName);
		SplitButtonData viewSyncSplitData = new SplitButtonData("ViewSyncSplitButton", "ViewSync");
		IList<RibbonItem> viewSyncItems = combineRibbonPanel.AddStackedItems(temPlaneButtonData, viewSyncSplitData);
		if (viewSyncItems.Count >= 1 && viewSyncItems[0] is PushButton temPlaneButton)
		{
			temPlaneButton.SetImage("/TungMEP2;component/Resources/Icons/TemporaryPLane_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/TemporaryPLane_32.png");
		}
		if (viewSyncItems.Count >= 2 && viewSyncItems[1] is SplitButton viewSyncSplitButton)
		{
			PushButtonData saveSyncButtonData = new PushButtonData("SaveSyncButton", "SaveSync", assemblyPath, typeof(Command_SaveSyncView).FullName);
			PushButtonData viewSyncButtonData = new PushButtonData("ViewSyncButton", "ViewSync", assemblyPath, typeof(Command_SyncView).FullName);
			viewSyncSplitButton.AddPushButton(saveSyncButtonData)?.SetImage("/TungMEP2;component/Resources/Icons/savesyncview_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/savesyncview_32.png");
			viewSyncSplitButton.AddPushButton(viewSyncButtonData)?.SetImage("/TungMEP2;component/Resources/Icons/syncview_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/syncview_32.png");
		}
		IRibbonStackPanel ribbonStackPanel_Combine = combineRibbonPanel.AddStackPanel();
		ribbonStackPanel_Combine.AddPushButton<Command_SectionBoxSlide>("SectionBoxSlide").SetImage("/TungMEP2;component/Resources/Icons/syncview_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/syncview_32.png");
		ribbonStackPanel_Combine.AddPushButton<Command_3DSync>("3DSync").SetImage("/TungMEP2;component/Resources/Icons/syncview_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/syncview_32.png");
		ribbonStackPanel_Combine.AddPushButton<Command_SetWorkset>("SetWorkset").SetImage("/TungMEP2;component/Resources/Icons/SetWorkSet_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/SetWorkSet_32.png");
		ribbonStackPanel_Combine.AddPushButton<Command_RerouteUnderOver>("Reroute").SetImage("/TungMEP2;component/Resources/Icons/AvoidMEP_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/AvoidMEP_32.png");
		ribbonStackPanel_Combine.AddPushButton<Command_RerouteUnderOverMultiple>("RerouteM").SetImage("/TungMEP2;component/Resources/Icons/AvoidMEP_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/AvoidMEP_32.png");

		RibbonPanel layoutRibbonPanel = base.Application.CreatePanel("LAYOUT", "TungMEP2");
		createdPanels.Add(layoutRibbonPanel);
		IRibbonStackPanel ribbonStackPanel_Layout = layoutRibbonPanel.AddStackPanel();
		ribbonStackPanel_Layout.AddPushButton<Command_AutoAdjustTagArrowHorizontal>("ADTags-H").SetImage("/TungMEP2;component/Resources/Icons/AutoAdjustTags_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/AutoAdjustTags_32.png");
		ribbonStackPanel_Layout.AddPushButton<Command_AutoAdjustTagArrowVertical>("ADTags-V").SetImage("/TungMEP2;component/Resources/Icons/AutoAdjustTags_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/AutoAdjustTags_32.png");
		ribbonStackPanel_Layout.AddPushButton<Command_DwgOpenReload>("DWG").SetImage("/TungMEP2;component/Resources/Icons/dwg_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/dwg_32.png");
		ribbonStackPanel_Layout.AddPushButton<Command_SetCropView>("CropViewPort").SetImage("/TungMEP2;component/Resources/Icons/CropViewPort_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/CropViewPort_32.png");
		ribbonStackPanel_Layout.AddPushButton<Command_CropLineWeight>("CropLineWeight").SetImage("/TungMEP2;component/Resources/Icons/LineWeight_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LineWeight_32.png");
		ribbonStackPanel_Layout.AddPushButton<Command_CopyCropView>("CopyCropView").SetImage("/TungMEP2;component/Resources/Icons/CropViewPort_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/CropViewPort_32.png");
		ribbonStackPanel_Layout.AddPushButton<Command_CopyVG>("CopyVG").SetImage("/TungMEP2;component/Resources/Icons/CropViewPort_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/CropViewPort_32.png");
		SplitButtonData dimCacheSplitData = new SplitButtonData("DimCacheSplitButton", "DimCache");
		SplitButtonData viewFloorSplitData = new SplitButtonData("ViewFloorSplitButton", "ViewFloorTools");
		PushButtonData dimFamilyPointButtonData = new PushButtonData("DimFamilyPointButton", "DimFPoint", assemblyPath, typeof(Command_DimFamilyInstancePoint).FullName);
		IList<RibbonItem> dimCacheItems = layoutRibbonPanel.AddStackedItems(dimCacheSplitData, dimFamilyPointButtonData, viewFloorSplitData);
		if (dimCacheItems.Count >= 1 && dimCacheItems[0] is SplitButton dimCacheSplitButton)
		{
			PushButtonData buildCacheButtonData = new PushButtonData("DimCacheBuildButton", "DimCache", assemblyPath, typeof(Command_AutoDimPointBuildCache).FullName);
			PushButtonData autoDimCacheButtonData = new PushButtonData("DimCacheRunButton", "AutoDimCache", assemblyPath, typeof(Command_AutoDimPointFromCache).FullName);
			dimCacheSplitButton.AddPushButton(buildCacheButtonData)?.SetImage("/TungMEP2;component/Resources/Icons/LineWeight_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LineWeight_32.png");
			dimCacheSplitButton.AddPushButton(autoDimCacheButtonData)?.SetImage("/TungMEP2;component/Resources/Icons/LineWeight_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LineWeight_32.png");
		}
		if (dimCacheItems.Count >= 2 && dimCacheItems[1] is PushButton dimFamilyPointButton)
		{
			dimFamilyPointButton.SetImage("/TungMEP2;component/Resources/Icons/LineWeight_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LineWeight_32.png");
		}
		if (dimCacheItems.Count >= 3 && dimCacheItems[2] is SplitButton viewFloorSplitButton)
		{
			PushButtonData dimViewFloorButtonData = new PushButtonData("DimViewFloorButton", "DimViewFloor", assemblyPath, typeof(Command_DimGridByBoundary).FullName);
			PushButtonData copyGridViewFloorButtonData = new PushButtonData("CopyGridViewFloorButton", "CopyGridViewFloor", assemblyPath, typeof(Command_CopyGridViewFloor).FullName);
			PushButtonData coverGrid2DButtonData = new PushButtonData("CoverGrid2DButton", "CoverGrid2D", assemblyPath, typeof(Command_CoverGrid2D).FullName);
			viewFloorSplitButton.AddPushButton(dimViewFloorButtonData)?.SetImage("/TungMEP2;component/Resources/Icons/LineWeight_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LineWeight_32.png");
			viewFloorSplitButton.AddPushButton(copyGridViewFloorButtonData)?.SetImage("/TungMEP2;component/Resources/Icons/LineWeight_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LineWeight_32.png");
			viewFloorSplitButton.AddPushButton(coverGrid2DButtonData)?.SetImage("/TungMEP2;component/Resources/Icons/LineWeight_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/LineWeight_32.png");
		}

		RibbonPanel sheetsRibbonPanel = base.Application.CreatePanel("SHEETS & PRINT", "TungMEP2");
		createdPanels.Add(sheetsRibbonPanel);
		PushButtonData printSheetsButtonData = new PushButtonData("PrintSheetsPrimaryButton", "PrintSheets", assemblyPath, typeof(Command_PrintSheets).FullName);
		if (sheetsRibbonPanel.AddItem(printSheetsButtonData) is PushButton printSheetsButton)
		{
			printSheetsButton.SetImage("/TungMEP2;component/Resources/Icons/Printer_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Printer_32.png");
		}
		IRibbonStackPanel ribbonStackPanel_Sheets = sheetsRibbonPanel.AddStackPanel();
		ribbonStackPanel_Sheets.AddPushButton<Command_CreateSelectPrintSet>("CreatePrintSet").SetImage("/TungMEP2;component/Resources/Icons/CreatePrintSet_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/CreatePrintSet_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_ExcelLegendLinkSync>("ExcelLegendSync").SetImage("/TungMEP2;component/Resources/Icons/Excel_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Excel_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_DuplicateSheets>("DuplicateSheets").SetImage("/TungMEP2;component/Resources/Icons/DSheets_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/DSheets_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_AlignViewports>("AlignSheets").SetImage("/TungMEP2;component/Resources/Icons/ASheets_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/ASheets_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_PutViewsInSheet>("PutViewsInSheet").SetImage("/TungMEP2;component/Resources/Icons/PutViewsInSheet_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/PutViewsInSheet_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_OpenViewInSheet>("OpenViewInSheet").SetImage("/TungMEP2;component/Resources/Icons/PutViewsInSheet_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/PutViewsInSheet_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_SetStt>("SetSTT").SetImage("/TungMEP2;component/Resources/Icons/SetSTT_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/SetSTT_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_IncrementSN>("IncrementSN").SetImage("/TungMEP2;component/Resources/Icons/PlusS_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/PlusS_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_DecrementSN>("DecrementSN").SetImage("/TungMEP2;component/Resources/Icons/MinusS_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/MinusS_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_ChangeSheetTitleBlockType>("CTitleBlockType").SetImage("/TungMEP2;component/Resources/Icons/Titlecheck_16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/Titlecheck_32.png");
		ribbonStackPanel_Sheets.AddPushButton<Command_TurnOffCloude>("TurnOffCloude").SetImage("/TungMEP2;component/Resources/Icons/RibbonIcon16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/RibbonIcon32.png");

		RibbonPanel manageRibbonPanel = base.Application.CreatePanel("MANAGE", "TungMEP2");
		createdPanels.Add(manageRibbonPanel);
		IRibbonStackPanel ribbonStackPanel_Manage = manageRibbonPanel.AddStackPanel();
		ribbonStackPanel_Manage.AddPushButton<Command_TransTemp>("TransTemp").SetImage("/TungMEP2;component/Resources/Icons/RibbonIcon16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/RibbonIcon32.png");
		ribbonStackPanel_Manage.AddPushButton<Command_SelectElementsByLevel>("SelectByLevel").SetImage("/TungMEP2;component/Resources/Icons/RibbonIcon16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/RibbonIcon32.png");
		ribbonStackPanel_Manage.AddPushButton<Command_SelectByIds>("SelectByIDs").SetImage("/TungMEP2;component/Resources/Icons/RibbonIcon16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/RibbonIcon32.png");

		// Low-frequency HVAC, Electrical and Checker commands share one compact menu.
		RibbonPanel otherRibbonPanel = base.Application.CreatePanel("KHÁC", "TungMEP2");
		createdPanels.Add(otherRibbonPanel);
		PulldownButtonData otherToolsData = new PulldownButtonData("OtherToolsPulldown", "KHÁC");
		if (otherRibbonPanel.AddItem(otherToolsData) is PulldownButton otherToolsButton)
		{
			otherToolsButton.SetImage("/TungMEP2;component/Resources/Icons/RibbonIcon16.png").SetLargeImage("/TungMEP2;component/Resources/Icons/RibbonIcon32.png");

			AddPulldownCommand<Command_ConnectWithTap>(otherToolsButton, "OtherHvacConnectTap", "Nối tap", "/TungMEP2;component/Resources/Icons/ConnectTo16.png", "/TungMEP2;component/Resources/Icons/ConnectTo32.png");
			otherToolsButton.AddSeparator();

			AddPulldownCommand<Command_CopyCircuits>(otherToolsButton, "OtherElectricalCopyCir", "CopyCir", "/TungMEP2;component/Resources/Icons/ConnectTo16.png", "/TungMEP2;component/Resources/Icons/ConnectTo32.png");
			AddPulldownCommand<Command_AddCir>(otherToolsButton, "OtherElectricalAddCir", "AddCir", "/TungMEP2;component/Resources/Icons/ConnectTo16.png", "/TungMEP2;component/Resources/Icons/ConnectTo32.png");
			AddPulldownCommand<Command_MergeCircuits>(otherToolsButton, "OtherElectricalMergeCir", "MergeCir", "/TungMEP2;component/Resources/Icons/SetWorkSet_16.png", "/TungMEP2;component/Resources/Icons/SetWorkSet_32.png");
			AddPulldownCommand<Command_RemoveCircuits>(otherToolsButton, "OtherElectricalRemoveCir", "RemoveCir", "/TungMEP2;component/Resources/Icons/DisVent_16.png", "/TungMEP2;component/Resources/Icons/DisVent_32.png");
			AddPulldownCommand<Command_RewireCircuitRaster>(otherToolsButton, "OtherElectricalWireRaster", "WireRaster", "/TungMEP2;component/Resources/Icons/ConnectTo16.png", "/TungMEP2;component/Resources/Icons/ConnectTo32.png");
			AddPulldownCommand<Command_Similar>(otherToolsButton, "OtherElectricalSimilar", "Similar", "/TungMEP2;component/Resources/Icons/ConnectTo16.png", "/TungMEP2;component/Resources/Icons/ConnectTo32.png");
			AddPulldownCommand<Command_CopyData>(otherToolsButton, "OtherElectricalCopyData", "CopyData", "/TungMEP2;component/Resources/Icons/ConnectTo16.png", "/TungMEP2;component/Resources/Icons/ConnectTo32.png");
			AddPulldownCommand<Command_ObjectDetection>(otherToolsButton, "OtherElectricalObjectDetection", "ObjectDetection", "/TungMEP2;component/Resources/Icons/ConnectTo16.png", "/TungMEP2;component/Resources/Icons/ConnectTo32.png");
			AddPulldownCommand<Command_FindReplaceParameter>(otherToolsButton, "OtherElectricalFindReplace", "FindReplaceP", "/TungMEP2;component/Resources/Icons/ConnectTo16.png", "/TungMEP2;component/Resources/Icons/ConnectTo32.png");
			otherToolsButton.AddSeparator();

			AddPulldownCommand<Command_CheckListTitleBlocks>(otherToolsButton, "OtherCheckerTitleBlocks", "CheckListTitleBlocks", "/TungMEP2;component/Resources/Icons/Titlecheck_16.png", "/TungMEP2;component/Resources/Icons/Titlecheck_32.png");
		}

		ApplyButtonTooltips(createdPanels);
	}

	private static PushButton? AddPulldownCommand<TCommand>(PulldownButton menu, string internalName, string label, string image16, string image32)
	{
		PushButtonData buttonData = new PushButtonData(internalName, label, Assembly.GetExecutingAssembly().Location, typeof(TCommand).FullName);
		PushButton? button = menu.AddPushButton(buttonData);
		button?.SetImage(image16).SetLargeImage(image32);
		return button;
	}

	private static void ApplyButtonTooltips(IEnumerable<RibbonPanel> panels)
	{
		foreach (RibbonPanel panel in panels)
		{
			foreach (RibbonItem item in panel.GetItems())
			{
				ApplyButtonTooltip(item);
			}
		}
	}

	private static void ApplyButtonTooltip(RibbonItem item)
	{
		if (item is PushButton pushButton)
		{
			if (string.IsNullOrWhiteSpace(pushButton.ToolTip))
			{
				string tooltip = ResolveButtonTooltip(pushButton);
				if (!string.IsNullOrWhiteSpace(tooltip))
				{
					pushButton.ToolTip = tooltip;
				}
			}
		}
		else
		{
			if (!(item is PulldownButton pulldownButton))
			{
				return;
			}
			if (string.IsNullOrWhiteSpace(pulldownButton.ToolTip))
			{
				string tooltip2 = ResolveGroupTooltip(pulldownButton);
				if (!string.IsNullOrWhiteSpace(tooltip2))
				{
					pulldownButton.ToolTip = tooltip2;
				}
			}
			foreach (PushButton childItem in pulldownButton.GetItems())
			{
				ApplyButtonTooltip(childItem);
			}
		}
	}

	private static string? ResolveButtonTooltip(RibbonItem item)
	{
		string label = GetItemLabel(item);
		if (ButtonTooltips.TryGetValue(label, out string tooltip))
		{
			return tooltip;
		}
		return null;
	}

	private static string? ResolveGroupTooltip(RibbonItem item)
	{
		string label = GetItemLabel(item);
		if (GroupTooltips.TryGetValue(label, out string tooltip))
		{
			return tooltip;
		}
		return null;
	}

	private static string GetItemLabel(RibbonItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.ItemText))
		{
			return item.ItemText;
		}
		if (!string.IsNullOrWhiteSpace(item.Name))
		{
			return item.Name;
		}
		return "này";
	}
}
