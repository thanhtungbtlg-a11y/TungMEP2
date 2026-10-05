using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Internal.Ctn.FitSpace;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

internal static class DirectYRunner
{
    private const double MillimetresPerFoot = 304.8;
    private const double PositionTolerance = 0.5 / MillimetresPerFoot;
    private const double UnchangedTolerance = 0.1 / MillimetresPerFoot;
    private const double RadiusTolerance = 0.1 / MillimetresPerFoot;
    private const double MaximumAdjustment = 5000.0 / MillimetresPerFoot;
    private const double DirectionToleranceRadians = 2.0 * Math.PI / 180.0;
    internal static IReadOnlyList<double> HeightSteps => DirectYInsertionGeometry.ElevationSteps(0.5 / MillimetresPerFoot, 100.0 / MillimetresPerFoot);

    private sealed class PipeSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => element is Pipe;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    internal sealed class DirectYPlan
    {
        internal ElementId YId;
        internal ElementId SourceId;
        internal ElementId AnchorId;
        internal FitSpaceConnectorKey YOpen;
        internal FitSpaceConnectorKey SourceOpen;
        internal FitSpaceConnectorKey MovingBoundary;
        internal FitSpaceConnectorKey AnchorNear;
        internal FitSpaceConnectorKey FixedFar;
        internal bool FixedFarWasOpen;
        internal List<ElementId> InnerIds;
        internal List<ElementId> OuterIds;
        internal ElementId UpstreamId;
        internal FitSpaceConnectorKey UpstreamNear;
        internal FitSpaceConnectorKey OuterBoundary;
        internal XYZ UpstreamPivot;
        internal XYZ UpstreamAxis;
        internal DirectYArticulationSolution Articulation;
        internal List<(ElementId Id, string UniqueId, ElementId TypeId)> Elements;
        internal List<FitSpaceConnectorKey> LocalPorts;
        internal List<(FitSpaceConnectorKey First, FitSpaceConnectorKey Second, DirectYRigidSignature Signature)> RigidPairs;
        internal List<FitSpaceConnectorBinding> PreservedBindings;
        internal List<FitSpaceConnectorBinding> InternalBindings;
        internal List<FitSpaceConnectorBinding> ExistingOffsetBindings;
        internal DirectYInnerClusterBaseline InnerBaseline;
        internal List<FitSpaceConnectorKey> UnchangedPorts;
        internal XYZ AxisDirection;
        internal XYZ Pivot;
        internal double InnerRotation;
        internal double AnchorSlide;
        internal double MinimumPipeLength;
        internal bool PerpendicularTee;
        internal bool HasOuter => UpstreamId != null;
    }

    internal sealed class RollBackOnAnyFailure : IFailuresPreprocessor
    {
        private readonly DirectYFailurePolicy policy = new DirectYFailurePolicy();
        internal string LastFailure => policy.LastFailure;
        internal IReadOnlyList<string> AllowedWarnings => policy.AllowedWarnings;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                FailureSeverity severity = failure.GetSeverity();
                FailureDefinitionId definition = failure.GetFailureDefinitionId();
                bool isTypeWarning = definition == BuiltInFailures.AutoRouteFailures.MismatchedSystemTypeWarning;
                bool isClassificationWarning =
                    definition == BuiltInFailures.AutoRouteFailures.MismatchedSystemClassificationNoSystemWarning ||
                    definition == BuiltInFailures.AutoRouteFailures.MismatchedSystemClassificationHasSystemWarning;
                string description = failure.GetDescriptionText();
                if (isClassificationWarning)
                    description = "Khác System Classification; không bảo đảm tính toán/flow analysis giữa hai hệ. " + description;
                else if (isTypeWarning)
                    description = "Khác System Type. " + description;
                string diagnostic = $"{severity} [{definition?.Guid}]: {description}";
                if (policy.Record(severity == FailureSeverity.Warning, isTypeWarning, diagnostic))
                    accessor.DeleteWarning(failure);
            }
            return policy.LastFailure.Length > 0
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }
    }

    internal static void Run(UIDocument uiDocument, Document document)
    {
        if (uiDocument == null || document == null || document.IsFamilyDocument || document.IsReadOnly)
        {
            TaskDialog.Show("TungMEP2 - DirectY", "Lệnh chỉ chạy trong project Revit có thể chỉnh sửa.");
            return;
        }

        Pipe main = null;
        Pipe source = null;
        List<Element> selected = uiDocument.Selection.GetElementIds()
            .Select(document.GetElement)
            .Where(element => element != null)
            .ToList();
        if (selected.Count == 1 && selected[0] is Pipe selectedMain)
        {
            main = selectedMain;
        }
        else if (selected.Count == 2 && selected.All(element => element is Pipe))
        {
            var openEnded = selected.OfType<Pipe>().Where(pipe =>
                DirectYTopology.Ports(pipe).Count(port => !port.IsConnected) == 1).ToArray();
            if (openEnded.Length == 1)
            {
                source = openEnded[0]; main = (Pipe)selected.Single(element => element.Id != source.Id);
            }
        }
        if (main == null)
        {
            Reference reference = uiDocument.Selection.PickObject(ObjectType.Element, new PipeSelectionFilter(),
                "1/2: Chọn Pipe tuyến chính; dùng Y hở phù hợp hoặc tự tạo Y mới trên Pipe này");
            main = document.GetElement(reference) as Pipe;
        }
        if (source == null)
        {
            Reference reference = uiDocument.Selection.PickObject(ObjectType.Element, new PipeSelectionFilter(),
                "2/2: Chọn Pipe nhánh có đầu hở; cho phép con thu/union và Pipe trung gian trước elbow");
            source = document.GetElement(reference) as Pipe;
        }
        if (!DirectYTopology.TryFindY(main, source, out FamilyInstance y, out string error, out bool mayCreate))
        {
            if (mayCreate)
            {
                if (DirectYAutoYRunner.TryExecute(document, main, source, out double height, out error, out bool autoPending))
                    TaskDialog.Show("TungMEP2 - DirectY", $"Đã tạo Y và nối trực tiếp nhánh; giữ các fitting hiện có. Bước cao độ đã dùng: {height * MillimetresPerFoot:0.##} mm (xoay trước, bước 0,5 mm).");
                else TaskDialog.Show("TungMEP2 - DirectY", (autoPending ? "Chưa xác nhận trạng thái transaction. " : "Không thay đổi mô hình. ") + error);
                return;
            }
            TaskDialog.Show("TungMEP2 - DirectY", error);
            return;
        }
        ElementId yId = y.Id, sourceId = source.Id;
        if (!DirectYInnerClusterBaseline.TryCapture(document, source, out var innerBaseline, out error))
        {
            TaskDialog.Show("TungMEP2 - DirectY", "Không thay đổi mô hình. " + error);
            return;
        }
        foreach (double height in HeightSteps)
        {
            if (!TryCreatePlan(document, document.GetElement(yId) as FamilyInstance, document.GetElement(sourceId) as Pipe,
                out DirectYPlan plan, out error, height, null, innerBaseline)) continue;
            if (TryExecute(document, plan, out error, out bool pending))
            {
                TaskDialog.Show("TungMEP2 - DirectY", $"Đã nối trực tiếp Pipe nhánh vào Y; giữ các elbow/con thu. Bước cao độ đã dùng: {height * MillimetresPerFoot:0.##} mm (xoay trước, bước 0,5 mm).");
                return;
            }
            if (pending) { TaskDialog.Show("TungMEP2 - DirectY", "Chưa xác nhận rollback/trạng thái transaction; dừng lệnh để kiểm tra Revit. " + error); return; }
        }
        TaskDialog.Show("TungMEP2 - DirectY", "Không thay đổi mô hình. Đã thử xoay trước và các bước cao độ 0,5 mm trong ±100 mm. " + error);
    }

    internal static bool TryConnectInOpenTransaction(Document document, FamilyInstance y, Pipe source, double height, out DirectYPlan plan, out string error,
        DirectYDiagnostics diagnostics, DirectYInnerClusterBaseline innerBaseline, bool perpendicularTee = false)
    {
        plan = null;
        if (!document.IsModifiable) { error = "Cần transaction đang mở để nối Y."; return false; }
        return TryCreatePlan(document, y, source, out plan, out error, height, diagnostics, innerBaseline, perpendicularTee) && TryApplyInOpenTransaction(document, plan, out error);
    }
    internal static bool ValidateApplied(Document document, DirectYPlan plan, out string error) => ValidatePostconditions(document, plan, out error);

    private static bool TryCreatePlan(
        Document document,
        FamilyInstance y,
        Pipe source,
        out DirectYPlan plan,
        out string error,
        double height = 0,
        DirectYDiagnostics diagnostics = null,
        DirectYInnerClusterBaseline innerBaseline = null,
        bool perpendicularTee = false)
    {
        plan = null;
        error = string.Empty;
        if (innerBaseline == null)
        { error = "Thiếu bản ghi cụm nhánh trước transaction; không thể xác nhận an toàn."; return false; }
        if (!IsYFamily(y) || source == null)
        {
            error = "Cần chọn đúng một fitting Y và một Pipe nhánh.";
            return false;
        }
        if (!IsEditable(document, y, out error) || !IsEditable(document, source, out error))
        {
            return false;
        }

        IReadOnlyList<Connector> yPorts = FitSpaceConnectorService.GetPhysicalPipingConnectors(y);
        if (yPorts.Any(port => !HasOnlySupportedEndReferences(port)))
        {
            error = "Y có kết nối Curve/Physical hoặc loại connector ngoài phạm vi DirectY.";
            return false;
        }
        if (yPorts.Count != 3 || yPorts.Count(port => !port.IsConnected) != 1)
        {
            error = "Y phải có đúng 3 connector ống, trong đó đúng 1 cổng nhánh đang hở.";
            return false;
        }
        if (yPorts.Any(port => !IsValidDirection(port.CoordinateSystem?.BasisZ)))
        {
            error = "Không đọc được hướng các connector của Y.";
            return false;
        }
        Connector yOpen = yPorts.Single(port => !port.IsConnected);
        int openIndex = Enumerable.Range(0, yPorts.Count)
            .Single(index => yPorts[index].Id == yOpen.Id);
        if (!DirectYGeometry.TryClassifyOpenBranch(
                ToVector(yPorts[0].CoordinateSystem.BasisZ),
                ToVector(yPorts[1].CoordinateSystem.BasisZ),
                ToVector(yPorts[2].CoordinateSystem.BasisZ),
                openIndex,
                2.0 * Math.PI / 180.0,
                out DirectYBranchClassification branchClassification,
                out string classificationError))
        {
            error = "Không xác định được cổng nhánh Y đang hở: " + classificationError;
            return false;
        }
        Connector[] yMain =
        {
            yPorts[branchClassification.MainFirstIndex],
            yPorts[branchClassification.MainSecondIndex]
        };
        if (yMain.Any(port => port.Id == yOpen.Id) ||
            AxisDot(yMain[0], yMain[1]) > -0.90)
        {
            error = "Cổng hở không phải nhánh của Y có tuyến chính gần thẳng.";
            return false;
        }
        if (yMain.Any(port => FitSpaceConnectorService.GetConnectedPhysicalReferences(port).Count != 1))
        {
            error = "Hai cổng tuyến chính của Y phải đang có đúng một kết nối vật lý mỗi cổng.";
            return false;
        }
        XYZ mainAxis = yMain[0].CoordinateSystem?.BasisZ;
        XYZ otherMainAxis = yMain[1].CoordinateSystem?.BasisZ;
        XYZ branchAxis = yOpen.CoordinateSystem?.BasisZ;
        if (!IsValidDirection(mainAxis) || !IsValidDirection(otherMainAxis) || !IsValidDirection(branchAxis) ||
            mainAxis.Normalize().DotProduct(otherMainAxis.Normalize()) > -0.90)
        {
            error = "Không xác định được cặp cổng tuyến chính đối hướng của Y.";
            return false;
        }
        double branchDot = Math.Abs(branchAxis.Normalize().DotProduct(mainAxis.Normalize()));
        if (!DirectYGuards.SupportsBranchAngle(branchDot, perpendicularTee))
        {
            error = perpendicularTee ? "Cổng Tee không vuông 90° với tuyến chính trong 3D." :
                "Cổng hở không có góc nhánh Y rõ ràng; kiểm tra lại family hoặc Part Type.";
            return false;
        }

        IReadOnlyList<Connector> sourcePorts = FitSpaceConnectorService.GetPhysicalPipingConnectors(source);
        if (sourcePorts.Any(port => !HasOnlySupportedEndReferences(port)))
        {
            error = "Pipe nhánh có kết nối không phải cặp End thông thường.";
            return false;
        }
        if (sourcePorts.Count != 2 || sourcePorts.Count(port => !port.IsConnected) != 1)
        {
            error = "Pipe nhánh phải có đúng một đầu hở và một đầu nối với chuỗi Pipe/fitting phía trước.";
            return false;
        }
        Connector sourceOpen = sourcePorts.Single(port => !port.IsConnected);
        Connector sourceNear = sourcePorts.Single(port => port.Id != sourceOpen.Id);
        if (!DirectYTopology.TryCollectTail(document, source, sourceNear, out DirectYTail tail, out error)) return false;
        if (!innerBaseline.ValidateUnmoved(document, tail.MovingIds, tail.Bindings,
            FitSpaceConnectorService.CreateKey(sourceOpen), out error)) return false;
        Pipe anchor = tail.Anchor;
        Connector anchorNear = tail.AnchorNear;
        IReadOnlyList<Connector> anchorPorts = FitSpaceConnectorService.GetPhysicalPipingConnectors(anchor);
        if (anchorPorts.Any(port => !HasOnlySupportedEndReferences(port)))
        {
            error = "Pipe làm trục có kết nối Curve/Physical hoặc loại ngoài phạm vi DirectY.";
            return false;
        }
        if (anchorPorts.Count != 2 || anchorPorts.Count(port => port.Id == anchorNear.Id) != 1 ||
            FitSpaceConnectorService.GetConnectedPhysicalReferences(anchorNear).Count != 1)
        {
            error = "Không xác định được đầu Pipe trục đang nối với elbow cuối.";
            return false;
        }
        Connector anchorFar = anchorPorts.Single(port => port.Id != anchorNear.Id);
        if (FitSpaceConnectorService.GetConnectedPhysicalReferences(anchorFar).Count > 1)
        {
            error = "Đầu xa của Pipe trục có nhiều kết nối; không thể giữ cố định an toàn.";
            return false;
        }
        if (!TryGetPipeLine(source, out _) || !TryGetPipeLine(anchor, out _))
        {
            error = "DirectY chỉ xử lý các Pipe thẳng có LocationCurve dạng Line.";
            return false;
        }
        if (sourceOpen.Shape != ConnectorProfileType.Round || yOpen.Shape != ConnectorProfileType.Round ||
            Math.Abs(sourceOpen.Radius - yOpen.Radius) > RadiusTolerance)
        {
            error = "Đường kính/cấu dạng connector của Pipe nhánh và cổng Y không trùng; DirectY không tự tạo transition.";
            return false;
        }

        XYZ pivot = anchorNear.Origin;
        XYZ axisDirection = pivot - anchorFar.Origin;
        XYZ sourceDirection = sourceOpen.Origin - sourceNear.Origin;
        if (!IsValidDirection(axisDirection) || !IsValidDirection(sourceDirection) ||
            !IsValidDirection(sourceOpen.CoordinateSystem?.BasisZ) ||
            sourceDirection.Normalize().DotProduct(sourceOpen.CoordinateSystem.BasisZ.Normalize()) < 0.99)
        {
            error = "Trục Pipe nhánh hoặc hướng connector đầu hở không hợp lệ.";
            return false;
        }
        axisDirection = axisDirection.Normalize();
        double minimumLength = Math.Max(document.Application.ShortCurveTolerance * 2.0, 10.0 / MillimetresPerFoot);
        double anchorLength = anchorFar.Origin.DistanceTo(anchorNear.Origin);
        double sourceLength = sourceNear.Origin.DistanceTo(sourceOpen.Origin);
        plan = new DirectYPlan
        {
            YId = y.Id,
            SourceId = source.Id,
            AnchorId = anchor.Id,
            YOpen = FitSpaceConnectorService.CreateKey(yOpen),
            SourceOpen = FitSpaceConnectorService.CreateKey(sourceOpen),
            MovingBoundary = FitSpaceConnectorService.CreateKey(tail.MovingBoundary),
            AnchorNear = FitSpaceConnectorService.CreateKey(anchorNear),
            InnerIds = tail.MovingIds,
            PreservedBindings = tail.Bindings.Concat(new[] { Bind(anchorNear, tail.MovingBoundary) }).ToList(),
            InternalBindings = tail.Bindings.ToList(),
            ExistingOffsetBindings = new List<FitSpaceConnectorBinding>(),
            InnerBaseline = innerBaseline,
            AxisDirection = axisDirection,
            Pivot = pivot,
            MinimumPipeLength = minimumLength,
            PerpendicularTee = perpendicularTee
        };

        // Prefer the least-invasive one-joint solution; add a second swivel only when needed.
        var input = new DirectYGeometryInput(ToVector(pivot), ToVector(axisDirection), ToVector(sourceOpen.Origin),
            ToVector(sourceDirection), ToVector(yOpen.Origin), ToVector(branchAxis), DirectionToleranceRadians, PositionTolerance);
        bool singleSolved = DirectYGeometry.TrySolve(input, out var single, out string geometryError) &&
            anchorLength + single.AxialSlide > minimumLength && sourceLength + single.SourceExtension > minimumLength &&
            Math.Abs(single.AxialSlide) <= MaximumAdjustment && Math.Abs(single.SourceExtension) <= MaximumAdjustment &&
            Math.Abs(axisDirection.Z * single.AxialSlide - height) <= UnchangedTolerance;
        Connector fixedFar = anchorFar;
        if (singleSolved)
        {
            plan.InnerRotation = single.RotationRadians; plan.AnchorSlide = single.AxialSlide;
        }
        else
        {
            if (!TryGetSingleExternal(anchorFar, out Connector outerToAnchor) ||
                !IsTwoPortElbow(outerToAnchor.Owner, out FamilyInstance outerElbow))
            {
                error = "Cần thêm khớp elbow phía sau Pipe trục để phối hợp xoay/chỉnh cao độ. " + geometryError;
                return false;
            }
            Connector outerBoundary = DirectYTopology.Ports(outerElbow).Single(port => port.Id != outerToAnchor.Id);
            if (!TryGetSingleExternal(outerBoundary, out Connector upstreamNear) ||
                upstreamNear.Owner is not Pipe upstream || !TryGetPipeLine(upstream, out _))
            {
                error = "Khớp elbow thứ hai phải nối với Pipe thẳng phía trước để giữ đầu xa và chỉnh cao độ.";
                return false;
            }
            if (tail.MovingIds.Any(id => id == upstream.Id || id == outerElbow.Id) || upstream.Id == anchor.Id)
            { error = "Cụm xoay tạo vòng; không thể giới hạn phạm vi an toàn."; return false; }
            var upstreamPorts = DirectYTopology.Ports(upstream);
            if (upstreamPorts.Count != 2) { error = "Pipe phía trước phải có đúng hai cổng End."; return false; }
            fixedFar = upstreamPorts.Single(port => port.Id != upstreamNear.Id);
            XYZ upstreamAxis = (upstreamNear.Origin - fixedFar.Origin).Normalize();
            var articulatedInput = new DirectYArticulationInput
            {
                UpstreamPivot = ToVector(upstreamNear.Origin),
                UpstreamAxis = ToVector(upstreamAxis),
                AnchorPivot = ToVector(pivot),
                AnchorAxis = ToVector(axisDirection),
                SourceOpen = ToVector(sourceOpen.Origin),
                SourceDirection = ToVector(sourceDirection),
                TargetPoint = ToVector(yOpen.Origin),
                TargetOpenDirection = ToVector(branchAxis),
                UpstreamLength = upstreamNear.Origin.DistanceTo(fixedFar.Origin),
                AnchorLength = anchorLength,
                SourceLength = sourceLength,
                MinimumLength = minimumLength,
                MaximumAdjustment = MaximumAdjustment,
                PositionTolerance = PositionTolerance
            };
            var heightSolutions = DirectYArticulation.HeightSolutions(articulatedInput, height).ToArray();
            if (heightSolutions.Length == 0) { error = "Không khớp được hai đầu ở bước cao độ này bằng xoay và chỉnh chiều dài hợp lệ."; return false; }
            var articulation = heightSolutions.OrderBy(value => value.OuterRotation * value.OuterRotation + value.InnerRotation * value.InnerRotation).First();
            plan.UpstreamId = upstream.Id; plan.UpstreamNear = FitSpaceConnectorService.CreateKey(upstreamNear);
            plan.OuterBoundary = FitSpaceConnectorService.CreateKey(outerBoundary);
            plan.UpstreamAxis = upstreamAxis; plan.UpstreamPivot = upstreamNear.Origin;
            plan.OuterIds = tail.MovingIds.Concat(new[] { anchor.Id, outerElbow.Id }).ToList();
            plan.Articulation = articulation; plan.InnerRotation = articulation.InnerRotation; plan.AnchorSlide = articulation.AnchorSlide;
            plan.PreservedBindings.Add(Bind(anchorFar, outerToAnchor));
            plan.PreservedBindings.Add(Bind(upstreamNear, outerBoundary));
        }
        plan.FixedFar = FitSpaceConnectorService.CreateKey(fixedFar);
        plan.FixedFarWasOpen = !fixedFar.IsConnected;
        List<ElementId> localIds = (plan.HasOuter ? plan.OuterIds.Concat(new[] { plan.UpstreamId }) :
            plan.InnerIds.Concat(new[] { anchor.Id })).Concat(new[] { y.Id }).Distinct().ToList();
        if (localIds.Count != (plan.HasOuter ? plan.OuterIds.Count + 2 : plan.InnerIds.Count + 2))
        { error = "Y/tuyến chính trùng với cụm nhánh di chuyển; không thực thi."; return false; }
        plan.Elements = new List<(ElementId, string, ElementId)>();
        plan.LocalPorts = new List<FitSpaceConnectorKey>();
        plan.RigidPairs = new List<(FitSpaceConnectorKey, FitSpaceConnectorKey, DirectYRigidSignature)>();
        foreach (ElementId id in localIds)
        {
            Element element = document.GetElement(id);
            if (!IsEditable(document, element, out error)) return false;
            var ports = DirectYTopology.Ports(element);
            if (ports.Any(port => !HasOnlySupportedEndReferences(port) ||
                FitSpaceConnectorService.GetConnectedPhysicalReferences(port).Count > 1) || !HasOnlyEndPorts(element))
            { error = "Cụm có tap, connector phụ hoặc nhiều kết nối; không thể giữ phạm vi cục bộ."; return false; }
            plan.Elements.Add((id, element.UniqueId, element.GetTypeId()));
            plan.LocalPorts.AddRange(ports.Select(FitSpaceConnectorService.CreateKey));
            if (element is not Pipe || (id != plan.SourceId && id != plan.AnchorId && id != plan.UpstreamId))
            {
                for (int i = 0; i < ports.Count; i++)
                    for (int j = i + 1; j < ports.Count; j++)
                        plan.RigidPairs.Add((FitSpaceConnectorService.CreateKey(ports[i]),
                            FitSpaceConnectorService.CreateKey(ports[j]), CaptureRigidPair(ports[i], ports[j])));
            }
        }
        // Everything beyond the one length-adjusted boundary remains fixed.
        plan.UnchangedPorts = yPorts.Select(FitSpaceConnectorService.CreateKey).Concat(new[] { plan.FixedFar }).ToList();
        foreach (Connector port in yMain.Concat(new[] { fixedFar }))
        {
            var refs = FitSpaceConnectorService.GetConnectedPhysicalReferences(port);
            if (refs.Count == 0 && port.Id == fixedFar.Id && port.Owner.Id == fixedFar.Owner.Id) continue;
            if (refs.Count != 1 || localIds.Any(id => id == refs[0].Owner.Id))
            { error = "Tuyến ngoài phạm vi không có một kết nối riêng biệt an toàn."; return false; }
            plan.PreservedBindings.Add(Bind(port, refs[0]));
            plan.UnchangedPorts.AddRange(DirectYTopology.Ports(refs[0].Owner).Select(FitSpaceConnectorService.CreateKey));
        }
        plan.UnchangedPorts = plan.UnchangedPorts.GroupBy(key => (key.OwnerId.Value, key.ConnectorId)).Select(group => group.First()).ToList();
        foreach (var binding in plan.PreservedBindings)
        {
            diagnostics?.Binding("connection/preflight-binding", document, binding, PositionTolerance);
            bool originalOffset = innerBaseline.AllowsExistingOffset(document, binding);
            if (!ValidatePreservedBinding(document, binding, originalOffset, out error))
            {
                diagnostics?.Binding("connection/invalid-binding", document, binding, PositionTolerance);
                error = "Kết nối sẵn có chưa hợp lệ: " + error +
                    $" Cặp ID {binding.First.OwnerId.Value}:{binding.First.ConnectorId} ↔ {binding.Second.OwnerId.Value}:{binding.Second.ConnectorId}.";
                return false;
            }
            if (originalOffset)
            {
                plan.ExistingOffsetBindings.Add(binding);
                diagnostics?.Binding("connection/preserved-original-internal-offset", document, binding, PositionTolerance);
            }
        }
        return true;
    }

    private static bool TryExecute(Document document, DirectYPlan plan, out string error, out bool pending)
    {
        error = string.Empty;
        pending = false;
        using var group = new TransactionGroup(document, "TungMEP2 - DirectY kiểm tra sau commit");
        if (group.Start() != TransactionStatus.Started) { error = "Không thể mở TransactionGroup."; return false; }
        using Transaction transaction = new Transaction(document, "TungMEP2 - DirectY");
        if (transaction.Start() != TransactionStatus.Started)
        {
            error = "Không thể bắt đầu transaction.";
            return false;
        }
        FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
        var failureHandler = new RollBackOnAnyFailure();
        options.SetFailuresPreprocessor(failureHandler);
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);
        try
        {
            if (!TryApplyInOpenTransaction(document, plan, out error))
            {
                pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
                error += " " + rollbackError;
                return false;
            }
            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
                error = pending ? "Chưa xác nhận rollback; kiểm tra trạng thái Revit trước khi chạy lại. " + rollbackError
                    : "Revit từ chối commit; transaction đã rollback.";
                if (failureHandler.LastFailure.Length > 0) error += " " + failureHandler.LastFailure;
                return false;
            }
            if (!ValidatePostconditions(document, plan, out error))
            {
                pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
                error += " " + rollbackError; return false;
            }
            if (group.Assimilate() == TransactionStatus.Committed) return true;
            pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string groupRollbackError);
            error = "Không xác nhận được kết quả TransactionGroup. " + groupRollbackError;
            return false;
        }
        catch (Exception exception)
        {
            pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
            error = exception.Message + " " + rollbackError;
            if (failureHandler.LastFailure.Length > 0) error += " " + failureHandler.LastFailure;
            return false;
        }
    }

    private static bool TryApplyInOpenTransaction(Document document, DirectYPlan plan, out string error)
    {
        if (!ValidatePreconditions(document, plan, out error)) return false;
        if (plan.HasOuter)
        {
            Disconnect(document, plan.UpstreamNear, plan.OuterBoundary);
            TransformCluster(document, plan.OuterIds, plan.UpstreamPivot, plan.UpstreamAxis,
                plan.Articulation.OuterRotation, plan.Articulation.UpstreamSlide);
            if (!TryReplacePipeEndpoint(document.GetElement(plan.UpstreamId) as Pipe, Find(document, plan.UpstreamNear).Origin,
                plan.UpstreamPivot + plan.UpstreamAxis.Multiply(plan.Articulation.UpstreamSlide), plan.MinimumPipeLength, out error)) return false;
            document.Regenerate();
        }
        Disconnect(document, plan.AnchorNear, plan.MovingBoundary);
        XYZ pivot = plan.HasOuter ? ToXYZ(plan.Articulation.TransformedAnchorPivot) : plan.Pivot;
        XYZ axis = plan.HasOuter ? ToXYZ(plan.Articulation.TransformedAnchorAxis) : plan.AxisDirection;
        TransformCluster(document, plan.InnerIds, pivot, axis, plan.InnerRotation, plan.AnchorSlide);
        if (!TryReplacePipeEndpoint(document.GetElement(plan.AnchorId) as Pipe, Find(document, plan.AnchorNear).Origin,
            pivot + axis.Multiply(plan.AnchorSlide), plan.MinimumPipeLength, out error)) return false;
        document.Regenerate();
        if (!TryReplacePipeEndpoint(document.GetElement(plan.SourceId) as Pipe, Find(document, plan.SourceOpen).Origin,
            Find(document, plan.YOpen).Origin, plan.MinimumPipeLength, out error)) return false;
        document.Regenerate();
        if (!Reconnect(document, plan.AnchorNear, plan.MovingBoundary, out error) ||
            (plan.HasOuter && !Reconnect(document, plan.UpstreamNear, plan.OuterBoundary, out error))) return false;
        Connector sourceOpen = Find(document, plan.SourceOpen), yOpen = Find(document, plan.YOpen);
        if (sourceOpen == null || yOpen == null || sourceOpen.Origin.DistanceTo(yOpen.Origin) > PositionTolerance ||
            !AreOpposed(sourceOpen, yOpen, DirectionToleranceRadians))
        { error = "Hai đầu Pipe/Y chưa trùng vị trí và đối hướng sau khi chỉnh."; return false; }
        if (!FitSpaceConnectorService.IsConnected(sourceOpen, yOpen)) { sourceOpen.ConnectTo(yOpen); document.Regenerate(); }
        return ValidatePostconditions(document, plan, out error);
    }

    private static bool ValidatePreconditions(Document document, DirectYPlan plan, out string error)
    {
        error = string.Empty;
        if (!plan.InnerBaseline.ValidateUnmoved(document, plan.InnerIds, plan.InternalBindings, plan.SourceOpen, out error)) return false;
        foreach (var snapshot in plan.Elements)
        {
            Element element = document.GetElement(snapshot.Id);
            if (!IsEditable(document, element, out error) || element.UniqueId != snapshot.UniqueId || element.GetTypeId() != snapshot.TypeId)
            {
                return false;
            }
        }
        if (Find(document, plan.YOpen)?.IsConnected != false ||
            Find(document, plan.SourceOpen)?.IsConnected != false ||
            FitSpaceConnectorService.GetConnectedPhysicalReferences(Find(document, plan.FixedFar)).Count !=
                (plan.FixedFarWasOpen ? 0 : 1) ||
            !plan.PreservedBindings.All(binding => DirectYInnerClusterBaseline.IsReciprocalUnique(
                Find(document, binding.First), Find(document, binding.Second))) ||
            !PortsUnchanged(document, plan.UnchangedPorts, UnchangedTolerance))
        {
            error = "Đầu hở, kết nối hoặc hình học đã thay đổi sau bước phân tích.";
            return false;
        }
        foreach (var binding in plan.PreservedBindings)
            if (!ValidatePreservedBinding(document, binding, plan.ExistingOffsetBindings.Contains(binding), out error)) return false;
        return true;
    }

    private static bool ValidatePostconditions(Document document, DirectYPlan plan, out string error)
    {
        error = string.Empty;
        if (plan.Elements.Any(snapshot => document.GetElement(snapshot.Id) is not Element element ||
            element.UniqueId != snapshot.UniqueId || element.GetTypeId() != snapshot.TypeId))
        {
            error = "ID hoặc loại của phần tử trong cụm đã thay đổi.";
            return false;
        }
        if (!PortsUnchanged(document, plan.UnchangedPorts, UnchangedTolerance))
        {
            error = "Y hoặc tuyến ngoài phạm vi đã di chuyển; rollback để bảo vệ mô hình.";
            return false;
        }
        if (plan.PerpendicularTee)
        {
            var teePorts = DirectYTopology.Ports(document.GetElement(plan.YId));
            Connector branch = teePorts.SingleOrDefault(port => port.Id == plan.YOpen.ConnectorId);
            Connector connectedSource = Find(document, plan.SourceOpen);
            if (branch == null || teePorts.Count != 3 || teePorts.Where(port => port.Id != branch.Id).Any(port =>
                connectedSource == null || !DirectYGuards.SupportsBranchAngle(Math.Abs(AxisDot(branch, port)), true) ||
                !DirectYGuards.SupportsBranchAngle(Math.Abs(AxisDot(connectedSource, port)), true)))
            { error = "Tee không giữ góc nhánh 90° sau khi nối; rollback."; return false; }
        }
        if (FitSpaceConnectorService.GetConnectedPhysicalReferences(Find(document, plan.FixedFar)).Count !=
            (plan.FixedFarWasOpen ? 0 : 1))
        {
            error = "Trạng thái kết nối đầu xa của Pipe trục đã thay đổi; rollback.";
            return false;
        }
        foreach (FitSpaceConnectorKey key in plan.LocalPorts)
        {
            Connector port = Find(document, key);
            if (port == null || !HasOnlySupportedEndReferences(port) ||
                port.Shape != key.OriginalShape ||
                (port.Shape == ConnectorProfileType.Round &&
                 Math.Abs(port.Radius - key.OriginalRadius) > RadiusTolerance))
            {
                error = "Cấu dạng hoặc DN của connector đã thay đổi; rollback.";
                return false;
            }
        }
        foreach (var pair in plan.RigidPairs)
        {
            Connector first = Find(document, pair.First), second = Find(document, pair.Second);
            if (first == null || second == null || !pair.Signature.IsPreservedBy(CaptureRigidPair(first, second),
                UnchangedTolerance, 0.5 * Math.PI / 180.0))
            { error = "Góc/takeout fitting hoặc chiều dài Pipe trung gian đã thay đổi; rollback."; return false; }
        }
        foreach (FitSpaceConnectorBinding binding in plan.PreservedBindings)
        {
            if (!ValidatePreservedBinding(document, binding,
                plan.InnerBaseline.IsOriginalInternal(binding) && plan.ExistingOffsetBindings.Contains(binding), out error))
            {
                error = "Một kết nối cũ bị mất hoặc lệch trục; rollback. " + error;
                return false;
            }
        }
        DirectYRigidStep? outer = plan.HasOuter ? new DirectYRigidStep(ToVector(plan.UpstreamPivot),
            ToVector(plan.UpstreamAxis), plan.Articulation.OuterRotation, plan.Articulation.UpstreamSlide) : (DirectYRigidStep?)null;
        var inner = new DirectYRigidStep(plan.HasOuter ? plan.Articulation.TransformedAnchorPivot : ToVector(plan.Pivot),
            plan.HasOuter ? plan.Articulation.TransformedAnchorAxis : ToVector(plan.AxisDirection), plan.InnerRotation, plan.AnchorSlide);
        if (!plan.InnerBaseline.ValidateTransformed(document, outer, inner, out error)) return false;
        Connector sourceOpen = Find(document, plan.SourceOpen);
        Connector yOpen = Find(document, plan.YOpen);
        if (!DirectYInnerClusterBaseline.IsReciprocalUnique(sourceOpen, yOpen) ||
            sourceOpen.Origin.DistanceTo(yOpen.Origin) > PositionTolerance ||
            !AreOpposed(sourceOpen, yOpen, DirectionToleranceRadians))
        {
            error = "Pipe chưa nối trực tiếp với Y hoặc Revit đã chèn fitting trung gian; rollback.";
            return false;
        }
        if (plan.Elements.Select(snapshot => document.GetElement(snapshot.Id)).OfType<Pipe>().Any(pipe =>
            !TryGetPipeLine(pipe, out Line line) || line.Length <= plan.MinimumPipeLength))
        {
            error = "Pipe sau chỉnh bị cong hoặc quá ngắn; rollback.";
            return false;
        }
        return true;
    }

    private static bool ValidatePreservedBinding(Document document, FitSpaceConnectorBinding binding,
        bool preservesOriginalInternalOffset, out string error)
    {
        Connector first = Find(document, binding.First), second = Find(document, binding.Second);
        if (!DirectYInnerClusterBaseline.IsReciprocalUnique(first, second))
        { error = "Không còn cặp connector vật lý hai chiều duy nhất."; return false; }
        if (!preservesOriginalInternalOffset)
            return FitSpaceConnectorService.ValidateBindingGeometry(document, binding, PositionTolerance, out error);
        if (first.Shape != ConnectorProfileType.Round || second.Shape != ConnectorProfileType.Round ||
            Math.Abs(first.Radius - second.Radius) > RadiusTolerance || !AreOpposed(first, second, 5.0 * Math.PI / 180.0))
        { error = "Kết nối bên trong cụm không giữ đúng DN/cấu dạng hoặc đối hướng."; return false; }
        // Exact transformed origins and the signed offset are separately checked against the immutable baseline.
        error = string.Empty; return true;
    }

    private static void TransformCluster(Document document, ICollection<ElementId> ids, XYZ pivot, XYZ axis,
        double angle, double slide)
    {
        if (Math.Abs(angle) > 1e-9)
        {
            ElementTransformUtils.RotateElements(document, ids, Line.CreateBound(pivot, pivot + axis), angle);
            document.Regenerate();
        }
        if (Math.Abs(slide) > 1e-8)
        {
            ElementTransformUtils.MoveElements(document, ids, axis.Multiply(slide));
            document.Regenerate();
        }
    }

    private static void Disconnect(Document document, FitSpaceConnectorKey firstKey, FitSpaceConnectorKey secondKey)
    {
        Connector first = Find(document, firstKey), second = Find(document, secondKey);
        if (!FitSpaceConnectorService.IsConnected(first, second)) throw new InvalidOperationException("Mất kết nối tại ranh giới cụm.");
        first.DisconnectFrom(second);
        document.Regenerate();
        if (FitSpaceConnectorService.IsConnected(Find(document, firstKey), Find(document, secondKey)))
            throw new InvalidOperationException("Revit không ngắt được ranh giới cụm.");
    }

    private static bool Reconnect(Document document, FitSpaceConnectorKey firstKey, FitSpaceConnectorKey secondKey, out string error)
    {
        error = string.Empty;
        Connector first = Find(document, firstKey), second = Find(document, secondKey);
        if (first == null || second == null || first.Origin.DistanceTo(second.Origin) > PositionTolerance ||
            !AreOpposed(first, second, 5.0 * Math.PI / 180.0))
        { error = "Không thể nối lại ranh giới cụm đúng vị trí và trục."; return false; }
        if (!FitSpaceConnectorService.IsConnected(first, second)) first.ConnectTo(second);
        document.Regenerate(); return true;
    }

    private static bool TryReplacePipeEndpoint(Pipe pipe, XYZ oldEnd, XYZ newEnd, double minimumLength, out string error)
    {
        error = string.Empty;
        if (!TryGetPipeLine(pipe, out Line line) || oldEnd == null || newEnd == null)
        {
            error = "Không đọc được đoạn Pipe cần chỉnh chiều dài.";
            return false;
        }
        double d0 = line.GetEndPoint(0).DistanceTo(oldEnd);
        double d1 = line.GetEndPoint(1).DistanceTo(oldEnd);
        if (Math.Min(d0, d1) > PositionTolerance)
        {
            error = "Đầu connector không còn nằm ở endpoint của Pipe.";
            return false;
        }
        XYZ other = d0 <= d1 ? line.GetEndPoint(1) : line.GetEndPoint(0);
        if (other.DistanceTo(newEnd) <= minimumLength)
        {
            error = "Pipe sẽ quá ngắn sau khi nối.";
            return false;
        }
        Line replacement = d0 <= d1 ? Line.CreateBound(newEnd, other) : Line.CreateBound(other, newEnd);
        ((LocationCurve)pipe.Location).Curve = replacement;
        return true;
    }

    private static bool IsYFamily(Element element)
    {
        if (element is not FamilyInstance fitting ||
            fitting.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeFitting ||
            fitting.MEPModel is not MechanicalFitting mechanical)
        {
            return false;
        }
        return mechanical.PartType == PartType.Wye ||
            mechanical.PartType == PartType.LateralTee ||
            mechanical.PartType == PartType.Tee;
    }

    private static bool IsTwoPortElbow(Element element, out FamilyInstance elbow)
    {
        elbow = element as FamilyInstance;
        return elbow != null &&
            elbow.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting &&
            elbow.MEPModel is MechanicalFitting mechanical &&
            mechanical.PartType == PartType.Elbow &&
            FitSpaceConnectorService.GetPhysicalPipingConnectors(elbow).Count == 2;
    }

    internal static bool IsEditable(Document document, Element element, out string error)
    {
        error = string.Empty;
        if (element == null || element.Pinned || element.GroupId != ElementId.InvalidElementId)
        {
            error = "Phần tử trong cụm bị thiếu, Pin hoặc thuộc Group.";
            return false;
        }
        if (document.IsWorkshared &&
            WorksharingUtils.GetCheckoutStatus(document, element.Id) == CheckoutStatus.OwnedByOtherUser)
        {
            error = "Một phần tử trong cụm đang do người khác sở hữu.";
            return false;
        }
        ElementId optionId = element.DesignOption?.Id ?? ElementId.InvalidElementId;
        if (optionId != ElementId.InvalidElementId && optionId != DesignOption.GetActiveDesignOptionId(document))
        {
            error = "Một phần tử trong cụm thuộc design option không hoạt động.";
            return false;
        }
        return true;
    }

    private static bool TryGetSingleExternal(Connector connector, out Connector external)
    {
        IReadOnlyList<Connector> references = FitSpaceConnectorService.GetConnectedPhysicalReferences(connector);
        external = references.Count == 1 ? references[0] : null;
        return external != null;
    }

    internal static bool HasOnlySupportedEndReferences(Connector connector)
    {
        if (connector == null)
        {
            return false;
        }
        int externalPhysicalCount = connector.AllRefs.Cast<Connector>().Count(reference =>
            reference.Owner.Id != connector.Owner.Id &&
            reference.Domain == Domain.DomainPiping &&
            (reference.ConnectorType == ConnectorType.End ||
             reference.ConnectorType == ConnectorType.Curve ||
             reference.ConnectorType == ConnectorType.Physical));
        int supportedCount = FitSpaceConnectorService.GetConnectedPhysicalReferences(connector).Count;
        return externalPhysicalCount == supportedCount &&
            (!connector.IsConnected || supportedCount > 0);
    }

    internal static bool HasOnlyEndPorts(Element element)
    {
        ConnectorSet ports = element is Pipe pipe ? pipe.ConnectorManager.Connectors :
            (element as FamilyInstance)?.MEPModel?.ConnectorManager?.Connectors;
        return ports != null && !ports.Cast<Connector>().Any(port => port.Domain == Domain.DomainPiping &&
            (port.ConnectorType == ConnectorType.Curve || port.ConnectorType == ConnectorType.Physical));
    }

    private static bool TryGetPipeLine(Pipe pipe, out Line line)
    {
        line = (pipe?.Location as LocationCurve)?.Curve as Line;
        return line != null && line.IsBound;
    }

    private static bool IsValidDirection(XYZ direction)
    {
        return direction != null && direction.GetLength() > 1e-9;
    }

    private static double AxisDot(Connector first, Connector second)
    {
        return first.CoordinateSystem.BasisZ.Normalize()
            .DotProduct(second.CoordinateSystem.BasisZ.Normalize());
    }

    private static FitSpaceConnectorBinding Bind(Connector first, Connector second)
    {
        return new FitSpaceConnectorBinding(
            FitSpaceConnectorService.CreateKey(first),
            FitSpaceConnectorService.CreateKey(second));
    }

    private static Connector Find(Document document, FitSpaceConnectorKey key)
    {
        return FitSpaceConnectorService.FindByKey(document, key);
    }

    private static bool AreOpposed(Connector first, Connector second, double toleranceRadians)
    {
        XYZ firstAxis = first?.CoordinateSystem?.BasisZ;
        XYZ secondAxis = second?.CoordinateSystem?.BasisZ;
        return IsValidDirection(firstAxis) && IsValidDirection(secondAxis) &&
            firstAxis.Normalize().DotProduct(secondAxis.Normalize()) <= -Math.Cos(toleranceRadians);
    }

    internal static bool PortsUnchanged(Document document, IEnumerable<FitSpaceConnectorKey> keys, double tolerance)
    {
        return keys.All(key =>
        {
            Connector port = Find(document, key);
            if (port == null || port.Origin.DistanceTo(key.OriginalOrigin) > tolerance ||
                port.Shape != key.OriginalShape ||
                !SameAxis(port.CoordinateSystem?.BasisX, key.OriginalBasisX) ||
                !SameAxis(port.CoordinateSystem?.BasisY, key.OriginalBasisY) ||
                !SameAxis(port.CoordinateSystem?.BasisZ, key.OriginalBasisZ))
            {
                return false;
            }
            return port.Shape switch
            {
                ConnectorProfileType.Round => Math.Abs(port.Radius - key.OriginalRadius) <= RadiusTolerance,
                ConnectorProfileType.Rectangular or ConnectorProfileType.Oval =>
                    Math.Abs(port.Width - key.OriginalWidth) <= RadiusTolerance &&
                    Math.Abs(port.Height - key.OriginalHeight) <= RadiusTolerance,
                _ => true
            };
        });
    }

    private static bool SameAxis(XYZ current, XYZ original)
    {
        if (current == null || original == null)
        {
            return current == null && original == null;
        }
        return IsValidDirection(current) && IsValidDirection(original) &&
            current.Normalize().DotProduct(original.Normalize()) >= Math.Cos(0.5 * Math.PI / 180.0);
    }

    private static Vector3d ToVector(XYZ value)
    {
        return new Vector3d(value.X, value.Y, value.Z);
    }

    private static XYZ ToXYZ(Vector3d value) => new XYZ(value.X, value.Y, value.Z);
    private static DirectYRigidSignature CaptureRigidPair(Connector first, Connector second) => DirectYRigidSignature.Create(
        ToVector(first.Origin), ToVector(second.Origin), ToVector(first.CoordinateSystem.BasisZ), ToVector(second.CoordinateSystem.BasisZ));
}
