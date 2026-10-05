using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Internal.Ctn.DirectY;

namespace VP_RevitAPI.Internal.Ctn;

internal static class Auto90Runner
{
    private const string Title = "TungMEP2 - Auto90";
    private sealed class PipeFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => element is Pipe;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    internal static void Run(UIDocument ui, Document document)
    {
        if (ui == null || document == null || document.IsFamilyDocument || document.IsReadOnly)
        { TaskDialog.Show(Title, "Lệnh chỉ chạy trong project có thể chỉnh sửa."); return; }
        Pipe main = null, source = null;
        List<Element> selected = ui.Selection.GetElementIds().Select(document.GetElement).Where(element => element != null).ToList();
        if (selected.Count == 1 && selected[0] is Pipe selectedMain) main = selectedMain;
        else if (selected.Count == 2 && selected.All(element => element is Pipe))
        {
            var open = selected.OfType<Pipe>().Where(pipe => DirectYTopology.Ports(pipe).Count(port => !port.IsConnected) == 1).ToArray();
            if (open.Length == 1) { source = open[0]; main = (Pipe)selected.Single(element => element.Id != source.Id); }
        }
        if (main == null)
            main = document.GetElement(ui.Selection.PickObject(ObjectType.Element, new PipeFilter(),
                "1/2: Chọn Pipe tuyến chính để tạo Tee 90°; tuyến này giữ nguyên tim/cao độ")) as Pipe;
        if (source == null)
            source = document.GetElement(ui.Selection.PickObject(ObjectType.Element, new PipeFilter(),
                "2/2: Chọn Pipe cuối nhánh có đầu hở; tự xoay qua elbow/con thu/union, không nhập góc")) as Pipe;
        if (main == null || source == null || main.Id == source.Id)
        { TaskDialog.Show(Title, "Chọn hai Pipe khác nhau: tuyến chính và đầu hở của nhánh."); return; }
        if (DirectYAutoYRunner.TryExecute(document, main, source, out double height, out string error, out bool pending, true))
            TaskDialog.Show(Title, "Đã tự xoay, căn tim 3D và tạo Tee 90° để nối nhánh.\n" +
                $"Chỉnh cao độ bổ sung: {height * 304.8:0.##} mm; ưu tiên xoay trước, bước 0,5 mm.\n" +
                "Tuyến chính và các đầu ngoài phạm vi được giữ nguyên.");
        else TaskDialog.Show(Title, (pending ? "Chưa xác nhận trạng thái transaction; kiểm tra Revit trước khi chạy lại. " : "Không thay đổi mô hình. ") + error);
    }
}
