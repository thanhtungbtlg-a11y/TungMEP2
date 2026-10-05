using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper.SectionBox;

namespace VP_RevitAPI.UI;

public sealed class UI_ExternalEventHandler : IExternalEventHandler
{
	private readonly UI_ExternalEventBridge _bridge;

	public UI_ExternalEventHandler(UI_ExternalEventBridge bridge)
	{
		_bridge = bridge;
	}

	public void Execute(UIApplication app)
	{
		if (_bridge.TryDequeue(out UI_Request request))
		{
			switch (request.Kind)
			{
			case UI_RequestKind.Hello:
				TaskDialog.Show("UI_Template", string.IsNullOrWhiteSpace(request.Message) ? "Xin chào từ giao diện mẫu" : request.Message);
				break;
			case UI_RequestKind.SectionBoxSlide:
				HandleSectionBoxSlide(app, request);
				break;
			}
		}
	}

	private static void HandleSectionBoxSlide(UIApplication app, UI_Request request)
	{
		if (!request.SectionBoxFace.HasValue)
		{
			TaskDialog.Show("Trượt Section Box", "Không xác định được mặt cần trượt.");
			return;
		}
		UIDocument uiDocument = app.ActiveUIDocument;
		if (uiDocument == null)
		{
			return;
		}
		if (!(uiDocument.ActiveView is View3D view3D))
		{
			TaskDialog.Show("Trượt Section Box", "Chỉ hỗ trợ View3D.");
			return;
		}
		Document document = uiDocument.Document;
		if (!view3D.IsSectionBoxActive)
		{
			using (Transaction activateTransaction = new Transaction(document, "Bật Section Box"))
			{
				activateTransaction.Start();
				view3D.IsSectionBoxActive = true;
				activateTransaction.Commit();
				TaskDialog.Show("Trượt Section Box", "Đã bật Section Box. Hãy bấm lại nút mặt để chọn điểm.");
				return;
			}
		}
		try
		{
			XYZ pickedPoint = PickPointOnFace(uiDocument, document);
			BoundingBoxXYZ sectionBox = view3D.GetSectionBox();
			if (!SectionBoxSlideMath.TrySlideFace(request.SectionBoxFace.Value, sectionBox, pickedPoint, out BoundingBoxXYZ updatedSectionBox, out string error))
			{
				TaskDialog.Show("Trượt Section Box", string.IsNullOrWhiteSpace(error) ? "Không thể trượt mặt đã chọn." : error);
				return;
			}
			using Transaction updateTransaction = new Transaction(document, "Trượt mặt Section Box");
			updateTransaction.Start();
			view3D.SetSectionBox(updatedSectionBox);
			updateTransaction.Commit();
			UI_SectionBoxSlideWindow.CloseCurrentWindow();
		}
		catch (Autodesk.Revit.Exceptions.OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			TaskDialog.Show("Trượt Section Box", ex2.Message);
		}
	}

	private static XYZ PickPointOnFace(UIDocument uiDocument, Document hostDocument)
	{
		Reference reference = uiDocument.Selection.PickObject(ObjectType.Face, "Chọn mặt để lấy điểm trượt");
		if (reference.LinkedElementId != ElementId.InvalidElementId)
		{
			return EvaluateLinkedFacePoint(reference, hostDocument);
		}
		Element hostElement = hostDocument.GetElement(reference.ElementId) ?? throw new System.InvalidOperationException("Không lấy được phần tử đã chọn.");
		Face hostFace = (hostElement.GetGeometryObjectFromReference(reference) as Face) ?? throw new System.InvalidOperationException("Tham chiếu không phải là mặt hợp lệ.");
		UV uvPoint = reference.UVPoint ?? throw new System.InvalidOperationException("Không lấy được tọa độ UV của mặt đã chọn.");
		return hostFace.Evaluate(uvPoint);
	}

	private static XYZ EvaluateLinkedFacePoint(Reference hostReference, Document hostDocument)
	{
		RevitLinkInstance linkInstance = (hostDocument.GetElement(hostReference.ElementId) as RevitLinkInstance) ?? throw new System.InvalidOperationException("Không lấy được Revit Link Instance.");
		Document linkDocument = linkInstance.GetLinkDocument() ?? throw new System.InvalidOperationException("Không truy cập được tài liệu của link.");
		Element linkedElement = linkDocument.GetElement(hostReference.LinkedElementId) ?? throw new System.InvalidOperationException("Không lấy được phần tử trong link.");
		Reference linkReference = hostReference.CreateReferenceInLink();
		Face linkedFace = (linkedElement.GetGeometryObjectFromReference(linkReference) as Face) ?? throw new System.InvalidOperationException("Tham chiếu trong link không phải là mặt hợp lệ.");
		UV uvPoint = linkReference.UVPoint ?? hostReference.UVPoint ?? throw new System.InvalidOperationException("Không lấy được tọa độ UV của mặt trong link.");
		XYZ pointInLinkCoordinates = linkedFace.Evaluate(uvPoint);
		return linkInstance.GetTotalTransform().OfPoint(pointInLinkCoordinates);
	}

	public string GetName()
	{
		return "UI_ExternalEventHandler";
	}
}
