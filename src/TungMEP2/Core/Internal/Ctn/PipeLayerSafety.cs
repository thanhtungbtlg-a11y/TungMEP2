using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.Internal.Ctn.FitSpace;

namespace VP_RevitAPI.Internal.Ctn;

// Explicit Z moves stay on selected Pipes; non-reference selected Pipes may resize
// along their original 3D axis while retaining the measurement station and datum.
// The user permits Revit to adjust related
// unselected Pipes/fittings/accessories, including length/slope, while preserving
// the fixed reference, protected elements and identity. Physical connections
// remain mandatory by default; explicit allow-open choice permits lost existing
// edges/port geometry changes only, not new joins or structural damage.
internal sealed class PipeLayerSafety
{
    private sealed class ElementState
    {
        internal FitSpaceElementSnapshot Element;
        internal PipeLayerSegment? Pipe;
        internal bool IsLocked;
        internal Transform OriginalTransform;
        internal Transform FixedTransform;
        internal long[] HostIds;
        internal IReadOnlyList<FitSpaceConnectorKey> Connectors;
        internal Dictionary<int, string[]> Peers;
    }

    private sealed class BindingState
    {
        internal FitSpaceConnectorBinding Binding;
        internal XYZ Offset;
        internal double AxisDot;
    }

    private readonly List<ElementState> elements = new List<ElementState>();
    private readonly List<BindingState> bindings = new List<BindingState>();
    private readonly List<KeyValuePair<ElementId, ElementId>> dependentFamilies = new List<KeyValuePair<ElementId, ElementId>>();

    internal static bool TryCapture(Document document, IReadOnlyList<Pipe> seeds, out PipeLayerSafety baseline, out string error)
    {
        baseline = new PipeLayerSafety();
        error = string.Empty;
        try
        {
            var queue = new Queue<Element>(seeds);
            var visited = new HashSet<long>();
            var bindingKeys = new HashSet<string>(StringComparer.Ordinal);
            var hostedFamilyIds = new HashSet<long>();
            var ancestorFamilyIds = new HashSet<long>();
            Dictionary<long, List<FamilyInstance>> hostedFamilies = BuildHostedFamilyIndex(document);
            while (queue.Count > 0)
            {
                Element element = queue.Dequeue();
                if (!visited.Add(element.Id.Value)) continue;
                if (visited.Count > 10000)
                    throw new InvalidOperationException("Mạng nối vượt 10.000 phần tử; không thể kiểm tra an toàn toàn bộ trong lệnh này.");
                if (!(element is Pipe) && !(element is FamilyInstance))
                    throw new InvalidOperationException($"Phần tử nối {element.Id.Value} ({element.GetType().Name}) chưa được bảo vệ hình học; chưa hỗ trợ cụm có FlexPipe/FabricationPart.");
                EnsureEndOnlyConnections(element);
                // A piping-connected shared/nested child can carry its parent.
                // Enroll family ancestors as boundaries even when they have no
                // piping ports; being an ancestor alone does not permit movement.
                if (element is FamilyInstance relatedFamily)
                    foreach (long parentId in GetHostIds(relatedFamily))
                        if (document.GetElement(new ElementId(parentId)) is FamilyInstance parent)
                        {
                            ancestorFamilyIds.Add(parent.Id.Value);
                            queue.Enqueue(parent);
                        }
                if (hostedFamilies.TryGetValue(element.Id.Value, out List<FamilyInstance> hosted))
                    foreach (FamilyInstance child in hosted)
                    {
                        hostedFamilyIds.Add(child.Id.Value);
                        queue.Enqueue(child);
                    }
                // Covers logical dependents whose family Host/HostFace is absent,
                // without treating annotations or insulation as independent MEP devices.
                foreach (ElementId childId in element.GetDependentElements(new ElementClassFilter(typeof(FamilyInstance))))
                {
                    if (childId == element.Id || !(document.GetElement(childId) is FamilyInstance child)) continue;
                    hostedFamilyIds.Add(child.Id.Value);
                    queue.Enqueue(child);
                    baseline.dependentFamilies.Add(new KeyValuePair<ElementId, ElementId>(element.Id, child.Id));
                }
                IReadOnlyList<Connector> connectors = FitSpaceConnectorService.GetPhysicalPipingConnectors(element);
                if (connectors.Count == 0 && !(element is FamilyInstance &&
                    (hostedFamilyIds.Contains(element.Id.Value) || ancestorFamilyIds.Contains(element.Id.Value))))
                    throw new InvalidOperationException($"Không đọc được cổng nối vật lý của phần tử {element.Id.Value}.");
                var state = new ElementState
                {
                    Element = new FitSpaceElementSnapshot(element),
                    IsLocked = IsLocked(document, element),
                    Connectors = connectors.Select(FitSpaceConnectorService.CreateKey).ToArray(),
                    Peers = new Dictionary<int, string[]>()
                };
                if (element is Pipe pipe)
                {
                    if (!PipeLayerRunner.TryReadSegment(pipe, out PipeLayerSegment segment, out string pipeError))
                        throw new InvalidOperationException($"Pipe {pipe.Id.Value}: {pipeError}");
                    state.Pipe = segment;
                }
                else if (element is FamilyInstance family)
                {
                    state.OriginalTransform = family.GetTransform();
                    state.HostIds = GetHostRoleIds(family);
                }

                foreach (Connector connector in connectors)
                {
                    IReadOnlyList<Connector> peers = FitSpaceConnectorService.GetConnectedPhysicalReferences(connector);
                    state.Peers[connector.Id] = peers.Select(Key).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                    foreach (Connector peer in peers)
                    {
                        queue.Enqueue(peer.Owner);
                        string firstKey = Key(connector);
                        string secondKey = Key(peer);
                        string bindingKey = string.CompareOrdinal(firstKey, secondKey) < 0 ? firstKey + "|" + secondKey : secondKey + "|" + firstKey;
                        if (bindingKeys.Add(bindingKey))
                            baseline.bindings.Add(new BindingState
                            {
                                Binding = new FitSpaceConnectorBinding(FitSpaceConnectorService.CreateKey(connector), FitSpaceConnectorService.CreateKey(peer)),
                                Offset = connector.Origin.Subtract(peer.Origin),
                                AxisDot = AxisDot(connector, peer)
                            });
                    }
                }
                baseline.elements.Add(state);
            }
            // Decide only after traversal: a hosted family may also have been
            // reached through a physical port before its host was visited.
            foreach (ElementState state in baseline.elements.Where(item => item.OriginalTransform != null))
                if (state.IsLocked || (!IsMovablePipingFamily(document.GetElement(state.Element.Id)) &&
                                       !hostedFamilyIds.Contains(state.Element.Id.Value)))
                    state.FixedTransform = state.OriginalTransform;
            return true;
        }
        catch (Exception exception)
        {
            error = "Không chụp được trạng thái kết nối ban đầu: " + exception.Message;
            baseline = null;
            return false;
        }
    }

    internal bool Validate(Document document, IReadOnlyDictionary<long, double> shifts, double tolerance, out string error,
        ElementId referenceId = null, IReadOnlyDictionary<long, double> stations = null, bool allowDisconnected = false)
    {
        error = string.Empty;
        foreach (ElementState state in elements)
        {
            Element current = document.GetElement(state.Element.Id);
            if (current == null || current.UniqueId != state.Element.UniqueId || current.GetTypeId() != state.Element.TypeId ||
                current.Category?.Id != state.Element.CategoryId ||
                (current.DesignOption?.Id ?? ElementId.InvalidElementId) != state.Element.DesignOptionId ||
                current.Pinned != state.Element.IsPinned || current.GroupId != state.Element.GroupId)
                return Reject($"Phần tử {state.Element.Id.Value} bị xóa, thay loại hoặc đổi ràng buộc.", out error);
            if (state.HostIds != null && (!(current is FamilyInstance family) || !GetHostRoleIds(family).SequenceEqual(state.HostIds)))
                return Reject($"Quan hệ host của phần tử {current.Id.Value} bị thay đổi.", out error);
            try { EnsureEndOnlyConnections(current); }
            catch (InvalidOperationException exception) { return Reject(exception.Message, out error); }
            var currentProperties = new FitSpaceElementSnapshot(current);
            bool selected = shifts.TryGetValue(current.Id.Value, out double expectedShift);
            if (currentProperties.SystemTypeId != state.Element.SystemTypeId ||
                !SameDouble(currentProperties.Diameter, state.Element.Diameter) ||
                ((selected || state.IsLocked || state.FixedTransform != null) &&
                 !SameDouble(currentProperties.Slope, state.Element.Slope)))
                return Reject($"Phần tử {current.Id.Value} bị đổi hệ, đường kính hoặc độ dốc.", out error);

            if (state.Pipe.HasValue)
            {
                if (!(current is Pipe pipe) || !PipeLayerRunner.TryReadSegment(pipe, out PipeLayerSegment actual, out string _))
                    return Reject($"Không đọc lại được Pipe {current.Id.Value}.", out error);
                PipeLayerSegment original = state.Pipe.Value;
                if (Math.Abs(actual.OutsideDiameter - original.OutsideDiameter) > tolerance)
                    return Reject($"Pipe {current.Id.Value} bị đổi đường kính ngoài.", out error);
                bool reference = referenceId != null && current.Id == referenceId;
                if (reference && !PipeLayerMath.EndpointsMatchTranslation(original.Start, original.End, actual.Start, actual.End, 0, tolerance))
                    return Reject($"Ống gốc {current.Id.Value} bị dịch/co giãn; ống gốc phải giữ nguyên hai đầu.", out error);
                if (selected && !reference && referenceId != null && !state.IsLocked)
                {
                    if (stations == null || !stations.TryGetValue(current.Id.Value, out double station))
                        return Reject($"Ống cần căn {current.Id.Value}: thiếu vị trí đo đã chụp trước thao tác.", out error);
                    if (!PipeLayerMath.AxialResizeMatchesTarget(original, actual, expectedShift, station, tolerance, out string reason))
                        return Reject($"Ống cần căn {current.Id.Value}: {reason}", out error);
                }
                else if (!reference && (selected || state.IsLocked) &&
                    !PipeLayerMath.EndpointsMatchTranslation(original.Start, original.End, actual.Start, actual.End,
                        selected ? expectedShift : 0.0, tolerance))
                    return Reject(selected ? $"Pipe đã chọn/được bảo vệ {current.Id.Value} bị đổi hình học ngoài phép dịch Z." :
                        $"Pipe liên quan {current.Id.Value} bị Pin, nằm trong Group hoặc do người khác sở hữu nhưng bị kéo theo.", out error);
            }
            if (state.FixedTransform != null && current is FamilyInstance fixedFamily)
            {
                Transform actual = fixedFamily.GetTransform();
                if (actual.Origin.DistanceTo(state.FixedTransform.Origin) > tolerance ||
                    !actual.BasisX.IsAlmostEqualTo(state.FixedTransform.BasisX) ||
                    !actual.BasisY.IsAlmostEqualTo(state.FixedTransform.BasisY) ||
                    !actual.BasisZ.IsAlmostEqualTo(state.FixedTransform.BasisZ))
                    return Reject($"Thiết bị ngoài vùng chọn {current.Id.Value} bị dịch/xoay theo.", out error);
            }

            IReadOnlyList<Connector> connectors = FitSpaceConnectorService.GetPhysicalPipingConnectors(current);
            if (connectors.Count != state.Connectors.Count)
                return Reject($"Số cổng của phần tử {current.Id.Value} thay đổi.", out error);
            foreach (FitSpaceConnectorKey oldConnector in state.Connectors)
            {
                Connector connector = connectors.FirstOrDefault(item => item.Id == oldConnector.ConnectorId);
                if (connector == null || connector.Shape != oldConnector.OriginalShape ||
                    (connector.Shape == ConnectorProfileType.Round && Math.Abs(connector.Radius - oldConnector.OriginalRadius) > tolerance) ||
                    ((connector.Shape == ConnectorProfileType.Rectangular || connector.Shape == ConnectorProfileType.Oval) &&
                     (Math.Abs(connector.Width - oldConnector.OriginalWidth) > tolerance || Math.Abs(connector.Height - oldConnector.OriginalHeight) > tolerance)))
                    return Reject($"Cổng {current.Id.Value}:{oldConnector.ConnectorId} bị thay đổi hình dạng/kích thước.", out error);
                if (state.FixedTransform != null &&
                    (connector.Origin.DistanceTo(oldConnector.OriginalOrigin) > tolerance ||
                     !connector.CoordinateSystem.BasisZ.IsAlmostEqualTo(oldConnector.OriginalBasisZ)))
                    return Reject($"Cổng trên phần tử cố định {current.Id.Value}:{connector.Id} bị dịch/đổi hướng.", out error);
                string[] peers = FitSpaceConnectorService.GetConnectedPhysicalReferences(connector)
                    .Select(Key).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                // Allow loss of existing edges only, never new/replaced joins.
                if (allowDisconnected ? peers.Except(state.Peers[connector.Id], StringComparer.Ordinal).Any() :
                    !peers.SequenceEqual(state.Peers[connector.Id], StringComparer.Ordinal))
                    return Reject($"Kết nối tại cổng {current.Id.Value}:{connector.Id} bị mất hoặc thay đổi.", out error);
            }
        }
        foreach (KeyValuePair<ElementId, ElementId> relation in dependentFamilies)
        {
            Element parent = document.GetElement(relation.Key);
            if (parent == null || !parent.GetDependentElements(new ElementClassFilter(typeof(FamilyInstance))).Contains(relation.Value))
                return Reject($"Phần tử gắn host/phụ thuộc {relation.Value.Value} mất quan hệ với {relation.Key.Value}.", out error);
        }
        foreach (BindingState state in bindings.Where(_ => !allowDisconnected))
        {
            Connector first = FitSpaceConnectorService.FindByKey(document, state.Binding.First);
            Connector second = FitSpaceConnectorService.FindByKey(document, state.Binding.Second);
            if (first == null || second == null || !FitSpaceConnectorService.IsConnected(first, second) ||
                !PipeLayerMath.BindingOffsetPreserved(ToPoint(state.Offset), ToPoint(first.Origin.Subtract(second.Origin)),
                    state.AxisDot, AxisDot(first, second), tolerance))
                return Reject($"Cặp cổng {state.Binding.First.OwnerId.Value}:{state.Binding.First.ConnectorId} ↔ " +
                    $"{state.Binding.Second.OwnerId.Value}:{state.Binding.Second.ConnectorId} bị hở hoặc lệch trục thêm sau khi dịch.", out error);
        }
        return true;
    }

    internal IReadOnlyList<string> GetConnectionIssues(Document document, double tolerance)
    {
        var issues = new List<string>();
        foreach (BindingState state in bindings)
        {
            Connector first = FitSpaceConnectorService.FindByKey(document, state.Binding.First);
            Connector second = FitSpaceConnectorService.FindByKey(document, state.Binding.Second);
            string pair = $"{state.Binding.First.OwnerId.Value}:{state.Binding.First.ConnectorId} ↔ " +
                          $"{state.Binding.Second.OwnerId.Value}:{state.Binding.Second.ConnectorId}";
            if (first == null || second == null)
            { issues.Add(pair + ": không tìm được cổng."); continue; }
            bool connected = FitSpaceConnectorService.IsConnected(first, second);
            XYZ offset = first.Origin.Subtract(second.Origin);
            double dot = AxisDot(first, second);
            if (connected && PipeLayerMath.BindingOffsetPreserved(ToPoint(state.Offset), ToPoint(offset), state.AxisDot, dot, tolerance)) continue;
            double oldAngle = Math.Acos(Math.Max(-1, Math.Min(1, -state.AxisDot))) * 180 / Math.PI;
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, -dot))) * 180 / Math.PI;
            issues.Add(pair + (connected ? ": còn nối nhưng hình học cổng thay đổi" : ": mất kết nối") +
                $"; khe hở {state.Offset.GetLength() * 304.8:F3} → {offset.GetLength() * 304.8:F3} mm" +
                $"; lệch hướng {oldAngle:F3} → {angle:F3}°" +
                $"; đổi vector lệch {offset.DistanceTo(state.Offset) * 304.8:F3} mm.");
        }
        return issues;
    }

    private static bool IsMovablePipingFamily(Element element) =>
        element.Category?.Id.Value == (long)BuiltInCategory.OST_PipeFitting ||
        element.Category?.Id.Value == (long)BuiltInCategory.OST_PipeAccessory;

    internal string DescribeRelatedChanges(Document document, IReadOnlyDictionary<long, double> shifts, double tolerance)
    {
        var changedIds = new List<long>();
        int pipeCount = 0, familyCount = 0;
        foreach (ElementState state in elements.Where(item => !shifts.ContainsKey(item.Element.Id.Value)))
        {
            Element current = document.GetElement(state.Element.Id);
            bool changed = false;
            if (state.Pipe.HasValue && current is Pipe pipe &&
                PipeLayerRunner.TryReadSegment(pipe, out PipeLayerSegment actual, out string _))
            {
                PipeLayerSegment original = state.Pipe.Value;
                changed = !PipeLayerMath.EndpointsMatchTranslation(original.Start, original.End, actual.Start, actual.End, 0, tolerance);
                if (changed) pipeCount++;
            }
            else if (state.OriginalTransform != null && current is FamilyInstance family)
            {
                Transform placement = family.GetTransform();
                changed = placement.Origin.DistanceTo(state.OriginalTransform.Origin) > tolerance ||
                    !placement.BasisX.IsAlmostEqualTo(state.OriginalTransform.BasisX) ||
                    !placement.BasisY.IsAlmostEqualTo(state.OriginalTransform.BasisY) ||
                    !placement.BasisZ.IsAlmostEqualTo(state.OriginalTransform.BasisZ) ||
                    state.Connectors.Any(old =>
                    {
                        Connector port = FitSpaceConnectorService.FindByKey(document, old);
                        return port.Origin.DistanceTo(old.OriginalOrigin) > tolerance ||
                            !port.CoordinateSystem.BasisZ.IsAlmostEqualTo(old.OriginalBasisZ);
                    });
                if (changed) familyCount++;
            }
            if (changed) changedIds.Add(state.Element.Id.Value);
        }
        if (changedIds.Count == 0) return string.Empty;
        string ids = string.Join(", ", changedIds.OrderBy(id => id).Take(8));
        return $"\nRevit đã điều chỉnh thêm {pipeCount} Pipe và {familyCount} fitting/phụ kiện liên quan ngoài vùng chọn." +
            $"\nID: {ids}" + (changedIds.Count > 8 ? ", ..." : string.Empty);
    }
    private static bool IsLocked(Document document, Element element) => element.Pinned ||
        element.GroupId != ElementId.InvalidElementId ||
        (document.IsWorkshared && WorksharingUtils.GetCheckoutStatus(document, element.Id) == CheckoutStatus.OwnedByOtherUser);
    // Preserve relation roles for validation; flatten only for graph traversal.
    // Face identity within one host is not frozen: native face reindexing during
    // allowed Pipe stretching still needs model-copy verification.
    private static long[] GetHostRoleIds(FamilyInstance family) => new[]
        {
            family.Host?.Id ?? ElementId.InvalidElementId,
            family.HostFace?.ElementId ?? ElementId.InvalidElementId,
            family.SuperComponent?.Id ?? ElementId.InvalidElementId
        }.Select(id => id.Value).ToArray();
    private static long[] GetHostIds(FamilyInstance family) => GetHostRoleIds(family)
        .Where(id => id != ElementId.InvalidElementId.Value && id != family.Id.Value)
        .Distinct().OrderBy(id => id).ToArray();
    private static Dictionary<long, List<FamilyInstance>> BuildHostedFamilyIndex(Document document)
    {
        var result = new Dictionary<long, List<FamilyInstance>>();
        using var collector = new FilteredElementCollector(document);
        foreach (FamilyInstance family in collector.OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
            foreach (long hostId in GetHostIds(family))
            {
                if (!result.TryGetValue(hostId, out List<FamilyInstance> children))
                    result[hostId] = children = new List<FamilyInstance>();
                children.Add(family);
            }
        return result;
    }
    private static PipeLayerPoint ToPoint(XYZ point) => new PipeLayerPoint(point.X, point.Y, point.Z);

    private static void EnsureEndOnlyConnections(Element element)
    {
        ConnectorSet set = element is MEPCurve curve ? curve.ConnectorManager?.Connectors :
            (element as FamilyInstance)?.MEPModel?.ConnectorManager?.Connectors;
        IEnumerable<Connector> all = set?.Cast<Connector>() ?? Enumerable.Empty<Connector>();
        foreach (Connector connector in all.Where(IsPhysicalPiping))
        {
            if (connector.ConnectorType != ConnectorType.End || connector.AllRefs.Cast<Connector>()
                .Any(peer => peer.Owner.Id != element.Id && IsPhysicalPiping(peer) && peer.ConnectorType != ConnectorType.End))
                throw new InvalidOperationException($"Phần tử {element.Id.Value} có cổng tap/Curve/Physical chưa được hỗ trợ. Không dịch cụm này để tránh bỏ sót nhánh nối.");
        }
    }

    private static bool IsPhysicalPiping(Connector connector) => connector.Domain == Domain.DomainPiping &&
        (connector.ConnectorType == ConnectorType.End || connector.ConnectorType == ConnectorType.Curve ||
         connector.ConnectorType == ConnectorType.Physical);
    private static string Key(Connector connector) => connector.Owner.UniqueId + ":" + connector.Id;
    private static double AxisDot(Connector first, Connector second) => first.CoordinateSystem.BasisZ.Normalize()
        .DotProduct(second.CoordinateSystem.BasisZ.Normalize());
    private static bool SameDouble(double? first, double? second) => first.HasValue == second.HasValue &&
        (!first.HasValue || Math.Abs(first.Value - second.Value) <= 1E-8);
    private static bool Reject(string message, out string error) { error = message; return false; }

    internal sealed class FailurePreprocessor : IFailuresPreprocessor
    {
        private readonly bool allowWarnings;
        internal FailurePreprocessor(bool allowWarnings = false) { this.allowWarnings = allowWarnings; }
        internal string Error { get; private set; } = string.Empty;
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            IList<FailureMessageAccessor> failures = accessor.GetFailureMessages();
            if (failures.Count == 0) return FailureProcessingResult.Continue;
            Error = string.Join("; ", failures.Select(item => item.GetDescriptionText()));
            // Do not delete warnings: native Revit retains control of its warning
            // dialog. Errors still rollback, and post-commit structural validation
            // rejects element deletion, new joins or incorrect selected geometry.
            if (allowWarnings && failures.All(item => item.GetSeverity() == FailureSeverity.Warning))
                return FailureProcessingResult.Continue;
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
