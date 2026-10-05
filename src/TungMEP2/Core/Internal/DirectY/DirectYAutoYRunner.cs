using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using VP_RevitAPI.Internal.Ctn.FitSpace;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

// All probes and the final split/connection belong to one rollback-safe transaction.
internal static class DirectYAutoYRunner
{
    private const double Mm = 304.8;
    private const double Tolerance = 0.5 / Mm;
    private const double FixedTolerance = 0.1 / Mm;
    private const double MaximumAdjustment = 5000 / Mm;

    private sealed class MainSnapshot
    {
        internal ElementId Id, Type, SystemType, Level;
        internal string UniqueId;
        internal XYZ Start, End, Axis;
        internal double Diameter, MinimumLength, Margin;
        internal List<(FitSpaceConnectorKey End, FitSpaceConnectorKey Peer)> Ends;
        internal List<FitSpaceConnectorKey> FixedNeighbors;
    }

    private sealed class BranchGeometry
    {
        internal XYZ Pivot, Axis, Open, Direction;
        internal double AnchorLength, SourceLength;
        internal DirectYArticulationInput Outer;
        internal DirectYInnerClusterBaseline InnerBaseline;
    }

    private sealed class Prototype
    {
        internal XYZ InputDirection, Offset, OpenDirection;
        internal double MinimumCenter, MaximumCenter;
    }

    internal static bool TryExecute(Document document, Pipe main, Pipe source, out double usedHeight,
        out string error, out bool pending, bool perpendicularTee = false)
    {
        usedHeight = 0; pending = false;
        var log = new DirectYDiagnostics();
        log.Write("input/mode", perpendicularTee ? "Auto90: perpendicular Tee, rotate before 0.5mm height steps" : "DirectY: oblique Y");
        if (!TryCaptureMain(document, main, out MainSnapshot snapshot, out error, perpendicularTee))
        {
            if (perpendicularTee) { log.Write("input/main-rejected", error); error = log.WithLog(error); }
            return false;
        }
        if (!TryCaptureBranch(document, source, main.Id, out BranchGeometry branch, out error, log)) return false;
        ElementId sourceId = source.Id;
        var baseline = new DirectYModelBaseline(document);
        log.Element("input/main", main, "SystemType=" + snapshot.SystemType?.Value);
        log.Element("input/source", source, "SystemType=" + SystemType(source)?.Value);
        double length = snapshot.Start.DistanceTo(snapshot.End);
        if (perpendicularTee) log.Write("input/main-window", $"main={main.Id.Value}; lengthMm={length * Mm:F3}; diameterMm={snapshot.Diameter * Mm:F3}; minimumPipeMm={snapshot.MinimumLength * Mm:F3}; provisionalMarginMm={snapshot.Margin * Mm:F3}; actual Tee takeouts not yet measured");
        double probeDistance = length / 2;
        XYZ probeCenter = snapshot.Start + snapshot.Axis.Multiply(probeDistance);
        var directions = DirectYInsertionGeometry.BranchDirections(V(snapshot.Axis), V(branch.Open - probeCenter), V(branch.Axis), V(branch.Direction), perpendicularTee);
        var prototypes = new List<Prototype>();
        double? measuredRequiredLength = null;
        using var group = new TransactionGroup(document, perpendicularTee ? "TungMEP2 - Auto90" : "TungMEP2 - DirectY kiểm tra Y sau commit");
        if (group.Start() != TransactionStatus.Started) { error = "Không thể mở TransactionGroup tạo Y."; return false; }
        using var transaction = new Transaction(document, perpendicularTee ? "Auto90 xoay, căn 3D và tạo Tee" : "TungMEP2 - DirectY tạo Y và nối nhánh");
        if (transaction.Start() != TransactionStatus.Started) { error = "Không thể mở transaction tạo Y."; return false; }
        var options = transaction.GetFailureHandlingOptions();
        var failureHandler = new DirectYRunner.RollBackOnAnyFailure();
        options.SetFailuresPreprocessor(failureHandler);
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);
        try
        {
            // Probe actual family takeouts once per orientation, without keeping any model changes.
            foreach (Vector3d direction in directions)
            {
                var probeDistances = new[] { 0.5, 0.25, 0.75, 0.1, 0.9 }
                    .Select(fraction => Math.Max(snapshot.Margin, Math.Min(length - snapshot.Margin, length * fraction))).ToList();
                for (int index = 0; index < probeDistances.Count; index++)
                {
                    XYZ testCenter = snapshot.Start + snapshot.Axis.Multiply(probeDistances[index]);
                    int countBefore = prototypes.Count;
                    using var probe = new SubTransaction(document);
                    if (probe.Start() != TransactionStatus.Started) throw new InvalidOperationException("Không thể mở lần thử Y.");
                    try
                    {
                        if (TryInsert(document, baseline, log, snapshot.Id, sourceId, testCenter, X(direction), snapshot.MinimumLength,
                            out FamilyInstance y, out _, out error, perpendicularTee, perpendicularTee ? ObserveWindow : null))
                        {
                            Connector open = DirectYTopology.Ports(y).Single(port => !port.IsConnected);
                            var runOffsets = DirectYTopology.Ports(y).Where(port => port.IsConnected)
                                .Select(port => (port.Origin - testCenter).DotProduct(snapshot.Axis)).ToArray();
                            double low = Math.Max(snapshot.Margin, snapshot.MinimumLength - runOffsets.Min());
                            double high = Math.Min(length - snapshot.Margin, length - snapshot.MinimumLength - runOffsets.Max());
                            if (perpendicularTee && !DirectYInsertionGeometry.TryMeasuredRunBounds(length, snapshot.MinimumLength,
                                runOffsets.Min(), runOffsets.Max(), snapshot.Margin, out low, out high, out _))
                                throw new InvalidOperationException("Tee thử không còn khoảng chèn hợp lệ theo takeout thực tế.");
                            prototypes.Add(new Prototype
                            {
                                InputDirection = X(direction),
                                Offset = open.Origin - testCenter,
                                OpenDirection = open.CoordinateSystem.BasisZ.Normalize(),
                                MinimumCenter = low,
                                MaximumCenter = high
                            });
                        }
                    }
                    catch (Exception exception) when (CanRetry(exception)) { error = log.Stage + ": " + exception.Message; log.Write("probe/error", error); }
                    finally
                    {
                        if (probe.GetStatus() == TransactionStatus.Started && probe.RollBack() != TransactionStatus.RolledBack)
                            throw new DirectYAbortException("Không xác nhận được rollback lần thử Y.");
                    }
                    if (prototypes.Count > countBefore) break;
                }
                void ObserveWindow(double low, double high, double required)
                {
                    if (!measuredRequiredLength.HasValue || required < measuredRequiredLength.Value) measuredRequiredLength = required;
                    if (low > high) return;
                    DirectYInsertionGeometry.AppendMeasuredRunProbes(probeDistances, low, high);
                }
            }
            if (prototypes.Count == 0)
            {
                pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
                string size = perpendicularTee ? $"Chiều dài đoạn chính: {length * Mm:F3} mm. " +
                    (measuredRequiredLength.HasValue ? $"Chiều dài tối thiểu theo Tee đã đo (tốt nhất trong các lần thử): {measuredRequiredLength.Value * Mm:F3} mm. " :
                     "Chưa có số đo Tee hợp lệ từ lần thử native; xem lỗi bước cuối. Không dùng 4D làm yêu cầu chiều dài. ") : string.Empty;
                error = log.WithLog((perpendicularTee ? "Không tạo được Tee 90° thử an toàn; kiểm tra family/size trong Routing Preferences. " : "Không tạo được Y thử an toàn. ") + size + "Lỗi bước cuối: " + error + " " + rollbackError);
                return false;
            }
            // Height is the outer loop: exhaust rotation candidates before each next 0.5 mm step.
            foreach (double height in DirectYRunner.HeightSteps)
                foreach (Prototype prototype in CandidatesAtHeight(prototypes, branch, snapshot, probeCenter, probeDistance, height, perpendicularTee))
                {
                    foreach (double shift in InsertionShifts(branch, prototype, probeCenter, snapshot, probeDistance, height))
                    {
                        XYZ center = probeCenter + snapshot.Axis.Multiply(shift);
                        using var attempt = new SubTransaction(document);
                        if (attempt.Start() != TransactionStatus.Started) throw new InvalidOperationException("Không thể mở lần thử kết nối.");
                        bool accepted = false;
                        DirectYRunner.DirectYPlan appliedPlan = null;
                        ElementId createdYId = null, createdSplitId = null;
                        try
                        {
                            log.Write("connection/attempt", $"heightMm={height * Mm:F3}; center={center}; direction={prototype.InputDirection}");
                            if (TryInsert(document, baseline, log, snapshot.Id, sourceId, center, prototype.InputDirection, snapshot.MinimumLength,
                                out FamilyInstance y, out ElementId splitId, out error, perpendicularTee))
                            {
                                createdYId = y.Id; createdSplitId = splitId;
                                Connector open = DirectYTopology.Ports(y).Single(port => !port.IsConnected);
                                if (open.Origin.DistanceTo(center + prototype.Offset) > FixedTolerance ||
                                    open.CoordinateSystem.BasisZ.Normalize().DotProduct(prototype.OpenDirection) <= Math.Cos(Math.PI / 360))
                                    error = "connection/prototype: Y thực tế không giữ đúng vị trí/hướng connector đã đo trong lần thử.";
                                else if (ValidateMain(document, snapshot, splitId, y.Id, null, out error))
                                {
                                    log.Write("connection/rotate-align", $"source={sourceId.Value}; Y={y.Id.Value}; heightMm={height * Mm:F3}");
                                    if (DirectYRunner.TryConnectInOpenTransaction(document, y, document.GetElement(sourceId) as Pipe, height, out appliedPlan, out error, log, branch.InnerBaseline, perpendicularTee) &&
                                        ValidateMain(document, snapshot, splitId, y.Id, sourceId, out error))
                                    {
                                        accepted = attempt.Commit() == TransactionStatus.Committed;
                                        if (!accepted) error = "connection/attempt-commit: Không xác nhận được commit phương án kết nối.";
                                    }
                                }
                            }
                        }
                        catch (Exception exception) when (CanRetry(exception)) { error = log.Stage + ": " + exception.Message; log.Write("connection/error", error); }
                        finally
                        {
                            if (attempt.GetStatus() == TransactionStatus.Started && attempt.RollBack() != TransactionStatus.RolledBack)
                                throw new DirectYAbortException("Không xác nhận được rollback kết nối thử.");
                        }
                        if (!accepted) { log.Write("connection/rejected", error); continue; }
                        TransactionStatus status = transaction.Commit();
                        pending = status == TransactionStatus.Pending;
                        foreach (string warning in failureHandler.AllowedWarnings) log.Write("commit/allowed-system-type-warning", warning);
                        if (status != TransactionStatus.Committed)
                        {
                            pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
                            error = log.WithLog((pending ? "Chưa xác nhận rollback. " : "Revit từ chối commit và đã rollback. ") + failureHandler.LastFailure + " " + rollbackError);
                            return false;
                        }
                        if (!ValidateMain(document, snapshot, createdSplitId, createdYId, sourceId, out error) ||
                            !DirectYRunner.ValidateApplied(document, appliedPlan, out error) ||
                            !baseline.ElementsSurvive(document, out error))
                        {
                            pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
                            error = log.WithLog(error + " " + rollbackError); return false;
                        }
                        log.Element("result/main", document.GetElement(snapshot.Id), "SystemType=" + SystemType(document.GetElement(snapshot.Id) as Pipe)?.Value);
                        log.Element("result/source", document.GetElement(sourceId), "SystemType=" + SystemType(document.GetElement(sourceId) as Pipe)?.Value);
                        if (group.Assimilate() != TransactionStatus.Committed)
                        {
                            pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
                            error = log.WithLog("Không xác nhận được kết quả TransactionGroup. " + rollbackError); return false;
                        }
                        log.Write("result/success", $"Y={createdYId.Value}; split={createdSplitId.Value}; heightMm={height * Mm:F3}");
                        usedHeight = height; return true;
                    }
                }
            pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string exhaustedRollbackError);
            error = log.WithLog("Không tìm được kết nối trực tiếp trong đoạn Pipe đã chọn sau khi thử xoay và bước cao độ 0,5 mm (±100 mm). " + error + " " + exhaustedRollbackError);
            return false;
        }
        catch (Exception exception)
        {
            pending = !DirectYTransactions.TryRollbackOwned(transaction, group, out string rollbackError);
            error = log.WithLog(exception.Message + " " + failureHandler.LastFailure + " " + rollbackError); return false;
        }
    }

    private static IEnumerable<Prototype> CandidatesAtHeight(List<Prototype> prototypes, BranchGeometry branch, MainSnapshot main,
        XYZ center, double centerDistance, double height, bool perpendicularTee)
    {
        foreach (var prototype in prototypes) yield return prototype;
        if (branch.Outer == null && !perpendicularTee) yield break;
        // Rolling a round-pipe Y about its run leaves run takeouts unchanged. Validate the actual fitting later.
        foreach (var prototype in prototypes.GroupBy(value => Math.Sign(value.OpenDirection.DotProduct(main.Axis))).Select(group => group.First()))
        {
            double low = prototype.MinimumCenter - centerDistance, high = prototype.MaximumCenter - centerDistance;
            var rolls = new List<double>();
            if (perpendicularTee && (Math.Abs(branch.Axis.Z) > 1e-6 || Math.Abs(height) < FixedTolerance))
                rolls.AddRange(DirectYInsertionGeometry.SinglePositionRolls(V(branch.Pivot), V(branch.Axis), V(branch.Open),
                    V(branch.Direction), V(main.Axis), V(center), V(prototype.OpenDirection), V(prototype.Offset), low, high,
                    branch.AnchorLength, branch.SourceLength, main.MinimumLength, MaximumAdjustment, Tolerance,
                    Math.Abs(branch.Axis.Z) > 1e-6 ? height / branch.Axis.Z : (double?)null));
            if (branch.Outer != null)
            {
                branch.Outer.TargetSlideAxis = V(main.Axis);
                branch.Outer.TargetSlideMinimum = low;
                branch.Outer.TargetSlideMaximum = high;
                rolls.AddRange(DirectYInsertionGeometry.PositionRolls(branch.Outer, V(main.Axis), V(center), V(prototype.OpenDirection), V(prototype.Offset), height));
            }
            foreach (double roll in rolls)
                yield return new Prototype
                {
                    InputDirection = X(DirectYArticulation.Rotate(V(prototype.InputDirection), V(main.Axis), roll)),
                    OpenDirection = X(DirectYArticulation.Rotate(V(prototype.OpenDirection), V(main.Axis), roll)),
                    Offset = X(DirectYArticulation.Rotate(V(prototype.Offset), V(main.Axis), roll)),
                    MinimumCenter = prototype.MinimumCenter,
                    MaximumCenter = prototype.MaximumCenter
                };
        }
    }

    private static IEnumerable<double> InsertionShifts(BranchGeometry branch, Prototype prototype, XYZ center, MainSnapshot main,
        double centerDistance, double height)
    {
        double low = prototype.MinimumCenter - centerDistance, high = prototype.MaximumCenter - centerDistance;
        double? singleSlide = Math.Abs(branch.Axis.Z) > 1e-6 ? height / branch.Axis.Z : (double?)null;
        if ((Math.Abs(branch.Axis.Z) > 1e-6 || Math.Abs(height) < FixedTolerance) &&
            DirectYInsertionGeometry.TryFitSingle(V(branch.Pivot), V(branch.Axis), V(branch.Open), V(branch.Direction),
                V(center + prototype.Offset), V(prototype.OpenDirection), V(main.Axis), low, high, branch.AnchorLength,
                branch.SourceLength, main.MinimumLength, MaximumAdjustment, Tolerance, out double shift, singleSlide)) yield return shift;
        if (branch.Outer == null) yield break;
        var input = branch.Outer;
        input.TargetPoint = V(center + prototype.Offset); input.TargetOpenDirection = V(prototype.OpenDirection);
        input.TargetSlideAxis = V(main.Axis); input.TargetSlideMinimum = low; input.TargetSlideMaximum = high;
        foreach (var result in DirectYArticulation.HeightSolutions(input, height)) yield return result.TargetSlide;
    }

    private static bool TryInsert(Document document, DirectYModelBaseline baseline, DirectYDiagnostics log, ElementId mainId, ElementId sourceId, XYZ center, XYZ direction,
        double minimumLength, out FamilyInstance y, out ElementId splitId, out string error, bool perpendicularTee = false,
        Action<double, double, double> observeRunWindow = null)
    {
        y = null; splitId = ElementId.InvalidElementId; error = string.Empty;
        foreach (bool perpendicular in perpendicularTee ? new[] { false } : new[] { false, true })
        {
            using var method = new SubTransaction(document);
            if (method.Start() != TransactionStatus.Started) throw new DirectYAbortException("Không thể mở phương án tạo Y.");
            try
            {
                log.Write("insert/method", $"perpendicular={perpendicular}; center={center}; direction={direction}");
                if (TryInsertCore(document, baseline, log, mainId, sourceId, center, direction, minimumLength, perpendicular, out y, out splitId, out error, perpendicularTee, observeRunWindow))
                    if (method.Commit() == TransactionStatus.Committed) return true;
            }
            catch (Exception exception) when (CanRetry(exception)) { error = log.Stage + ": " + exception.Message; }
            finally
            {
                if (method.GetStatus() == TransactionStatus.Started && method.RollBack() != TransactionStatus.RolledBack)
                    throw new DirectYAbortException("Không xác nhận được rollback phương án tạo Y.");
            }
            log.Write("insert/rejected", error);
        }
        y = null; splitId = ElementId.InvalidElementId; return false;
    }

    private static bool TryInsertCore(Document document, DirectYModelBaseline baseline, DirectYDiagnostics log, ElementId mainId, ElementId sourceId, XYZ center, XYZ direction,
        double minimumLength, bool perpendicular, out FamilyInstance y, out ElementId splitId, out string error, bool perpendicularTee = false,
        Action<double, double, double> observeRunWindow = null)
    {
        y = null; splitId = ElementId.InvalidElementId; error = string.Empty;
        Pipe main = document.GetElement(mainId) as Pipe, source = document.GetElement(sourceId) as Pipe;
        if (main == null || source == null) { error = "insert/input: Pipe đã mất sau lần thử."; return false; }
        // Same source-side type choice as 1E1Y; let native Revit join compatible systems.
        ElementId system = SystemType(source);
        ElementId level = source.ReferenceLevel?.Id;
        if (system == null || system == ElementId.InvalidElementId || level == null || level == ElementId.InvalidElementId)
        { error = "insert/source-context: Không đọc được System Type/level của Pipe nhánh."; return false; }
        Line originalRun = (Line)((LocationCurve)main.Location).Curve;
        double originalLength = originalRun.Length;
        XYZ originalStart = originalRun.GetEndPoint(0);
        XYZ mainAxis = originalRun.Direction.Normalize();
        XYZ stubDirection = perpendicular ? (direction - mainAxis.Multiply(direction.DotProduct(mainAxis))).Normalize() : direction;
        log.Write("insert/split-main", "main=" + mainId.Value);
        splitId = PlumbingUtils.BreakCurve(document, mainId, center);
        if (splitId == ElementId.InvalidElementId) { error = "insert/split-main: Không chia được Pipe tuyến chính."; return false; }
        document.Regenerate();
        double diameter = source.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
        log.Write("insert/create-stub", "SystemType=" + system.Value);
        Pipe stub = Pipe.Create(document, system, source.GetTypeId(), level, center, center + stubDirection.Multiply(Math.Max(1000 / Mm, diameter * 8)));
        stub.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(diameter);
        ElementId stubId = stub.Id;
        document.Regenerate();
        Connector cut1 = At(document.GetElement(mainId), center), cut2 = At(document.GetElement(splitId), center), branch = At(document.GetElement(stubId), center);
        if (cut1 == null || cut2 == null || branch == null || cut1.IsConnected || cut2.IsConnected || branch.IsConnected)
        { error = "insert/cut-connectors: Đầu chia Pipe không hở hoặc không trùng điểm tạo Y."; return false; }
        log.Write("insert/NewTeeFitting", $"main={mainId.Value}; split={splitId.Value}; stub={stubId.Value}");
        y = document.Create.NewTeeFitting(cut1, cut2, branch);
        document.Regenerate();
        log.Element("insert/created-fitting", y);
        if (y == null || !DirectYTopology.IsY(y)) { error = "insert/NewTeeFitting: Revit không tạo được fitting Y/tee hỗ trợ từ Routing Preferences hiện tại."; return false; }
        ElementId yId = y.Id;
        if (!DirectYTemporaryCleanup.TryRemove(document, baseline, log, yId, mainId, splitId, sourceId, stubId, out error)) return false;
        y = document.GetElement(yId) as FamilyInstance;
        if ((perpendicular || perpendicularTee) && !TrySetNewYDirection(document, yId, direction, out error, perpendicularTee)) return false;
        y = document.GetElement(yId) as FamilyInstance;
        var ports = DirectYTopology.Ports(y);
        if (ports.Count != 3 || ports.Count(port => !port.IsConnected) != 1) { error = "insert/validate-Y: Y sau tạo không có đúng một cổng nhánh hở."; return false; }
        Connector open = ports.Single(port => !port.IsConnected);
        if (open.Shape != ConnectorProfileType.Round || Math.Abs(open.Radius * 2 - diameter) > FixedTolerance ||
            open.CoordinateSystem.BasisZ.Normalize().DotProduct(direction) < Math.Cos((perpendicularTee ? 0.5 : 2) * Math.PI / 180) ||
            (perpendicularTee && !DirectYGuards.SupportsBranchAngle(open.CoordinateSystem.BasisZ.Normalize().DotProduct(mainAxis), true)))
        { error = perpendicularTee ? "insert/validate-Tee90: Family không giữ đúng DN hoặc nhánh vuông 90° trong 3D." : "insert/validate-Y: Family Y không giữ đúng DN hoặc góc nhánh 45° yêu cầu."; return false; }
        var connected = ports.Where(port => port.IsConnected).ToArray();
        if (perpendicularTee)
        {
            double firstOffset = connected.Min(port => (port.Origin - center).DotProduct(mainAxis));
            double lastOffset = connected.Max(port => (port.Origin - center).DotProduct(mainAxis));
            bool fits = DirectYInsertionGeometry.TryMeasuredRunBounds(originalLength, minimumLength, firstOffset, lastOffset,
                minimumLength + FixedTolerance, out double low, out double high, out double required);
            log.Write("insert/measured-Tee-window", $"main={mainId.Value}; lengthMm={originalLength * Mm:F3}; firstOffsetMm={firstOffset * Mm:F3}; lastOffsetMm={lastOffset * Mm:F3}; requiredMm={required * Mm:F3}; centerMinMm={low * Mm:F3}; centerMaxMm={high * Mm:F3}; fits={fits}");
            if (required <= 0) { error = "insert/Tee-clearance: Không đọc được khoảng takeout hợp lệ của Tee thử."; return false; }
            if (required > 0) observeRunWindow?.Invoke(low, high, required);
            double actualCenter = (center - originalStart).DotProduct(mainAxis);
            if (!fits || actualCenter < low - 1e-9 || actualCenter > high + 1e-9)
            {
                error = $"insert/Tee-clearance: Đoạn chính dài {originalLength * Mm:F3} mm; Tee đã đo cần ít nhất {required * Mm:F3} mm gồm phần ống còn lại hai đầu. " +
                        (fits ? "Vị trí thử chưa nằm trong khoảng chèn đo được; thử lại theo kích thước thực tế." : "Không có khoảng chèn hợp lệ cho Tee này.");
                return false;
            }
        }
        ElementId newMainId = splitId;
        if (!connected.All(port => DirectYTopology.SinglePeer(port, out Connector peer) && (peer.Owner.Id == mainId || peer.Owner.Id == newMainId)) ||
            connected.Select(port => FitSpaceConnectorService.GetConnectedPhysicalReferences(port).Single().Owner.Id).Distinct().Count() != 2 ||
            new[] { mainId, splitId }.Any(id => (document.GetElement(id)?.Location as LocationCurve)?.Curve is not Line line || line.Length <= minimumLength))
        { error = "insert/validate-main-run: Y phải nối trực tiếp hai đoạn tuyến chính, không qua fitting phụ."; return false; }
        return true;
    }

    private static bool TrySetNewYDirection(Document document, ElementId yId, XYZ direction, out string error, bool perpendicularTee = false)
    {
        error = perpendicularTee ? "Family không tạo được nhánh 90°; cần Tee 90° đúng DN trong Routing Preferences." :
            "Family Y mới không có góc connector instance có thể chỉnh về 45°.";
        Connector current = DirectYTopology.Ports(document.GetElement(yId)).Single(port => !port.IsConnected);
        if (perpendicularTee && current.CoordinateSystem.BasisZ.Normalize().DotProduct(direction) > Math.Cos(Math.PI / 360))
        { error = string.Empty; return true; } // A fixed 90-degree Tee needs no writable Angle parameter.
        foreach (double angle in perpendicularTee ? new[] { Math.PI / 2 } : new[] { Math.PI / 4, 3 * Math.PI / 4 })
        {
            using var adjustment = new SubTransaction(document); adjustment.Start();
            try
            {
                Connector open = DirectYTopology.Ports(document.GetElement(yId)).Single(port => !port.IsConnected);
                open.Angle = angle; document.Regenerate();
                open = DirectYTopology.Ports(document.GetElement(yId)).Single(port => !port.IsConnected);
                if (open.CoordinateSystem.BasisZ.Normalize().DotProduct(direction) > Math.Cos(2 * Math.PI / 180))
                    if (adjustment.Commit() == TransactionStatus.Committed) return true;
            }
            catch (Exception exception) when (CanRetry(exception)) { error = "insert/adjust-new-Y-angle: " + exception.Message; }
            finally
            {
                if (adjustment.GetStatus() == TransactionStatus.Started && adjustment.RollBack() != TransactionStatus.RolledBack)
                    throw new DirectYAbortException("Không xác nhận được rollback góc Y mới.");
            }
        }
        return false;
    }

    private static bool TryCaptureMain(Document document, Pipe main, out MainSnapshot snapshot, out string error, bool perpendicularTee = false)
    {
        snapshot = null; error = string.Empty;
        if (!DirectYRunner.IsEditable(document, main, out error)) return false;
        if ((main.Location as LocationCurve)?.Curve is not Line line || !line.IsBound || !DirectYRunner.HasOnlyEndPorts(main))
        { error = "Tuyến chính phải là Pipe thẳng không có tap/connector phụ."; return false; }
        var ports = DirectYTopology.Ports(main);
        if (ports.Count != 2 || ports.Any(port => !DirectYRunner.HasOnlySupportedEndReferences(port) ||
            FitSpaceConnectorService.GetConnectedPhysicalReferences(port).Count > 1))
        { error = "Hai đầu tuyến chính không có topology End an toàn."; return false; }
        var ends = new List<(FitSpaceConnectorKey, FitSpaceConnectorKey)>();
        var fixedNeighbors = new List<FitSpaceConnectorKey>();
        foreach (Connector port in ports)
        {
            var peers = FitSpaceConnectorService.GetConnectedPhysicalReferences(port);
            ends.Add((FitSpaceConnectorService.CreateKey(port), peers.Count == 1 ? FitSpaceConnectorService.CreateKey(peers[0]) : null));
            if (peers.Count == 1)
            {
                var binding = new FitSpaceConnectorBinding(ends.Last().Item1, ends.Last().Item2);
                if (!FitSpaceConnectorService.ValidateBindingGeometry(document, binding, Tolerance, out error)) return false;
                fixedNeighbors.AddRange(DirectYTopology.Ports(peers[0].Owner).Select(FitSpaceConnectorService.CreateKey));
            }
        }
        double diameter = main.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
        double minimum = Math.Max(document.Application.ShortCurveTolerance * 2, 10 / Mm);
        if (!DirectYInsertionGeometry.TryInitialRunBounds(line.Length, diameter, minimum, FixedTolerance, perpendicularTee, out double margin))
        {
            error = perpendicularTee ? $"Đoạn tuyến chính dài {line.Length * Mm:F3} mm, không đủ để chia thành hai đoạn ống thử (cần hơn {2 * (minimum + FixedTolerance) * Mm:F3} mm trước khi đo Tee)." :
                "Đoạn tuyến chính quá ngắn để chèn Y và giữ khoảng lùi hai đầu.";
            return false;
        }
        snapshot = new MainSnapshot
        {
            Id = main.Id,
            UniqueId = main.UniqueId,
            Type = main.GetTypeId(),
            SystemType = main.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId(),
            Level = main.ReferenceLevel?.Id,
            Diameter = diameter,
            Start = line.GetEndPoint(0),
            End = line.GetEndPoint(1),
            Axis = line.Direction.Normalize(),
            MinimumLength = minimum,
            Margin = margin,
            Ends = ends,
            FixedNeighbors = fixedNeighbors
        };
        return true;
    }

    private static bool TryCaptureBranch(Document document, Pipe source, ElementId mainId, out BranchGeometry geometry, out string error,
        DirectYDiagnostics log)
    {
        geometry = null; error = string.Empty;
        if (!DirectYRunner.IsEditable(document, source, out error)) return false;
        var ports = DirectYTopology.Ports(source);
        if (ports.Count != 2 || ports.Count(port => !port.IsConnected) != 1 || (source.Location as LocationCurve)?.Curve is not Line)
        { error = "Chọn Pipe cuối nhánh có đúng một đầu hở."; return false; }
        Connector open = ports.Single(port => !port.IsConnected), near = ports.Single(port => port.IsConnected);
        if (!DirectYTopology.TryCollectTail(document, source, near, out var tail, out error)) return false;
        Connector anchorFar = DirectYTopology.Ports(tail.Anchor).Single(port => port.Id != tail.AnchorNear.Id);
        foreach (var binding in tail.Bindings) log.Binding("input/branch-binding", document, binding, Tolerance);
        log.Binding("input/branch-binding", document, tail.AnchorNear, tail.MovingBoundary, Tolerance);
        log.BindingToSinglePeer("input/branch-binding", document, anchorFar, Tolerance);
        if (tail.MovingIds.Any(id => id == mainId) || tail.Anchor.Id == mainId) { error = "Tuyến chính trùng cụm nhánh di chuyển."; return false; }
        foreach (ElementId id in tail.MovingIds.Concat(new[] { tail.Anchor.Id }))
            if (!DirectYRunner.IsEditable(document, document.GetElement(id), out error)) return false;
        geometry = new BranchGeometry
        {
            Pivot = tail.AnchorNear.Origin,
            Axis = (tail.AnchorNear.Origin - anchorFar.Origin).Normalize(),
            Open = open.Origin,
            Direction = (open.Origin - near.Origin).Normalize(),
            AnchorLength = anchorFar.Origin.DistanceTo(tail.AnchorNear.Origin),
            SourceLength = open.Origin.DistanceTo(near.Origin)
        };
        if (DirectYTopology.SinglePeer(anchorFar, out var peer) && DirectYTopology.IsElbow(peer.Owner, out var elbow))
        {
            Connector other = DirectYTopology.Ports(elbow).Single(port => port.Id != peer.Id);
            if (DirectYTopology.SinglePeer(other, out var uNear) && uNear.Owner is Pipe upstream &&
                (upstream.Location as LocationCurve)?.Curve is Line)
            {
                if (!DirectYRunner.IsEditable(document, upstream, out error) || !DirectYRunner.IsEditable(document, elbow, out error)) return false;
                if (mainId == upstream.Id || tail.MovingIds.Any(id => id == upstream.Id || id == elbow.Id))
                { error = "Tuyến chính/cụm xoay tạo vòng; không thực thi."; return false; }
                Connector far = DirectYTopology.Ports(upstream).Single(port => port.Id != uNear.Id);
                log.Binding("input/branch-binding", document, other, uNear, Tolerance);
                log.BindingToSinglePeer("input/branch-binding", document, far, Tolerance);
                geometry.Outer = new DirectYArticulationInput
                {
                    UpstreamPivot = V(uNear.Origin),
                    UpstreamAxis = V((uNear.Origin - far.Origin).Normalize()),
                    AnchorPivot = V(geometry.Pivot),
                    AnchorAxis = V(geometry.Axis),
                    SourceOpen = V(geometry.Open),
                    SourceDirection = V(geometry.Direction),
                    UpstreamLength = uNear.Origin.DistanceTo(far.Origin),
                    AnchorLength = geometry.AnchorLength,
                    SourceLength = geometry.SourceLength,
                    MinimumLength = Math.Max(document.Application.ShortCurveTolerance * 2, 10 / Mm),
                    MaximumAdjustment = MaximumAdjustment,
                    PositionTolerance = Tolerance
                };
            }
        }
        if (!DirectYInnerClusterBaseline.TryCapture(document, source, out var innerBaseline, out error)) return false;
        geometry.InnerBaseline = innerBaseline;
        return true;
    }

    private static bool ValidateMain(Document document, MainSnapshot snapshot, ElementId splitId, ElementId yId, ElementId sourceId, out string error)
    {
        error = "Tuyến chính/đầu xa/neighbor đã đổi sau tạo Y; rollback.";
        Pipe original = document.GetElement(snapshot.Id) as Pipe, split = document.GetElement(splitId) as Pipe;
        if (original == null || split == null || original.UniqueId != snapshot.UniqueId ||
            !DirectYRunner.PortsUnchanged(document, snapshot.FixedNeighbors, FixedTolerance)) return false;
        foreach (Pipe pipe in new[] { original, split })
        {
            if (pipe.GetTypeId() != snapshot.Type || Math.Abs(pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble() - snapshot.Diameter) > FixedTolerance ||
                pipe.ReferenceLevel?.Id != snapshot.Level ||
                (pipe.Location as LocationCurve)?.Curve is not Line line || line.Length <= snapshot.MinimumLength ||
                Math.Abs(line.Direction.Normalize().DotProduct(snapshot.Axis)) < 1 - 1e-9) return false;
            foreach (XYZ point in new[] { line.GetEndPoint(0), line.GetEndPoint(1) })
                if ((point - snapshot.Start).CrossProduct(snapshot.Axis).GetLength() > FixedTolerance) return false;
        }
        var ports = DirectYTopology.Ports(original).Concat(DirectYTopology.Ports(split)).ToArray();
        foreach (var end in snapshot.Ends)
        {
            var matched = ports.Where(port => port.Origin.DistanceTo(end.End.OriginalOrigin) <= FixedTolerance).ToArray();
            if (matched.Length != 1 || !SamePort(matched[0], end.End)) return false;
            var peers = FitSpaceConnectorService.GetConnectedPhysicalReferences(matched[0]);
            if (end.Peer == null) { if (peers.Count != 0 || matched[0].IsConnected) return false; }
            else if (peers.Count != 1 || !FitSpaceConnectorService.IsConnected(matched[0], FitSpaceConnectorService.FindByKey(document, end.Peer))) return false;
        }
        var yPorts = DirectYTopology.Ports(document.GetElement(yId));
        if (yPorts.Count != 3) return false;
        var owners = new List<ElementId>();
        foreach (Connector port in yPorts)
        {
            var peers = FitSpaceConnectorService.GetConnectedPhysicalReferences(port);
            if (peers.Count == 0 && sourceId == null && !port.IsConnected) continue;
            if (peers.Count != 1 || FitSpaceConnectorService.GetConnectedPhysicalReferences(peers[0]).Count != 1 ||
                port.Origin.DistanceTo(peers[0].Origin) > Tolerance ||
                port.CoordinateSystem.BasisZ.Normalize().DotProduct(peers[0].CoordinateSystem.BasisZ.Normalize()) > -Math.Cos(2 * Math.PI / 180)) return false;
            owners.Add(peers[0].Owner.Id);
        }
        if (owners.Count != (sourceId == null ? 2 : 3) || !owners.Contains(snapshot.Id) || !owners.Contains(splitId) || (sourceId != null && !owners.Contains(sourceId))) return false;
        error = string.Empty; return true;
    }

    private static bool SamePort(Connector port, FitSpaceConnectorKey key) => port.Shape == key.OriginalShape &&
        Math.Abs(port.Radius - key.OriginalRadius) <= FixedTolerance &&
        port.CoordinateSystem.BasisZ.Normalize().DotProduct(key.OriginalBasisZ.Normalize()) > Math.Cos(Math.PI / 360) &&
        port.CoordinateSystem.BasisX.Normalize().DotProduct(key.OriginalBasisX.Normalize()) > Math.Cos(Math.PI / 360) &&
        port.CoordinateSystem.BasisY.Normalize().DotProduct(key.OriginalBasisY.Normalize()) > Math.Cos(Math.PI / 360);
    private static Connector At(Element element, XYZ point) => DirectYTopology.Ports(element).SingleOrDefault(port => port.Origin.DistanceTo(point) <= Tolerance);
    private static Vector3d V(XYZ value) => new Vector3d(value.X, value.Y, value.Z);
    private static XYZ X(Vector3d value) => new XYZ(value.X, value.Y, value.Z);
    private static ElementId SystemType(Pipe pipe) => pipe?.MEPSystem?.GetTypeId() ??
        pipe?.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
    private static bool CanRetry(Exception exception) => exception is not Autodesk.Revit.Exceptions.RegenerationFailedException &&
        exception is not DirectYAbortException;
}
