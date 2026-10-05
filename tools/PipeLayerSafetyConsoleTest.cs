using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using VP_RevitAPI.Internal.Ctn;

internal static class PipeLayerSafetyConsoleTest
{
    private const double Tolerance = 0.05 / 304.8;
    private static int assertions;
    private static void Main(string[] args)
    {
        string group = args.Length == 0 ? "all" : args[0];
        if (args.Length > 1 || !new[] { "all", "related", "host", "error", "flow", "resize" }.Contains(group))
            throw new ArgumentException("Expected one test group: all, related, host, error, flow or resize.");
        if (group == "all" || group == "related") RelatedGeometry();
        if (group == "all" || group == "host") HostedBoundary();
        if (group == "all" || group == "error") ReferenceDiagnostic();
        if (group == "all" || group == "flow") RunnerFlow();
        if (group == "all" || group == "flow") AllowOpenFlow();
        if (group == "all" || group == "flow") NativeFailureFlow();
        if (group == "all" || group == "resize") SelectedResizeFlow();
        Console.WriteLine($"PipeLayer runner/safety contract: {assertions} assertions passed ({group}).");
    }

    // Removing the relaxed unselected-Pipe policy must fail these observed
    // safe connector-preserving translation/stretch fixtures, not a mock assertion.
    private static void RelatedGeometry()
    {
        var f = new Fixture(); var baseline = f.Capture(); f.MoveRelated(false);
        Check(baseline.Validate(f.Doc, f.Shifts, Tolerance, out string error), "Connected unselected Pipe translation must be allowed: " + error);
        f = new Fixture(); baseline = f.Capture(); f.MoveRelated(true);
        Check(baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Connected unselected Pipe may change length/slope: " + error);
        foreach (string constraint in new[] { "pin", "group", "other-owner" })
        {
            f = new Fixture();
            if (constraint == "pin") f.Neighbor.Pinned = true;
            if (constraint == "group") f.Neighbor.GroupId = new ElementId(55);
            if (constraint == "other-owner") { f.Doc.IsWorkshared = true; f.Doc.OtherOwned.Add(f.Neighbor.Id.Value); }
            baseline = f.Capture(); f.MoveRelated(false);
            Check(!baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Protected related Pipe must stay fixed: " + constraint);
        }
        f = new Fixture(); baseline = f.Capture(); f.MoveRelated(false); SetPipe(f.Root, new XYZ(0, 0, 1), new XYZ(10, 0, 1));
        Check(!baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Fixed reference may not move");
        f = new Fixture(); baseline = f.Capture(); f.MoveRelated(false); SetPipe(f.Selected, new XYZ(0, 2, 0), new XYZ(10, 2, 1));
        Check(!baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Selected Pipe slope remains preserved");
        f = new Fixture(); baseline = f.Capture(); f.MoveRelated(false); f.Selected.ConnectorManager.Connectors[1].DisconnectFrom(f.Fitting.MEPModel.ConnectorManager.Connectors[0]);
        Check(!baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "New disconnection remains rejected");
        f = new Fixture(); baseline = f.Capture(); f.MoveRelated(false); f.Neighbor.Parameters[BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM].Value = new ElementId(999);
        Check(!baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "System Type change remains rejected");
        f = new Fixture(); baseline = f.Capture(); f.MoveRelated(false); f.Neighbor.Parameters[BuiltInParameter.RBS_PIPE_OUTER_DIAMETER].Value = 0.3;
        Check(!baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Outside diameter change remains rejected");
        f = new Fixture(); baseline = f.Capture(); f.MoveRelated(false); f.Neighbor.TypeId = new ElementId(77);
        Check(!baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Type replacement remains rejected");
        f = new Fixture(); f.Fitting.Category.Id = new ElementId((long)BuiltInCategory.OST_PipeAccessory); baseline = f.Capture(); f.MoveRelated(false);
        Check(baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Related unpinned PipeAccessory may follow the pipes: " + error);
        f = new Fixture(); f.Fitting.Category.Id = new ElementId((long)BuiltInCategory.OST_MechanicalEquipment); baseline = f.Capture(); f.MoveRelated(false);
        Check(!baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Unhosted equipment remains a fixed boundary");
        f = new Fixture(); f.Fitting.Category.Id = new ElementId((long)BuiltInCategory.OST_MechanicalEquipment); f.Fitting.Host = f.Neighbor;
        baseline = f.Capture(); f.MoveRelated(false);
        Check(baseline.Validate(f.Doc, f.Shifts, Tolerance, out error), "Physical-first hosted family is classified after host traversal: " + error);
    }

    // A connector-only capture passes these invalid host changes. The production
    // capture must include host/dependent families even when they have no ports.
    private static void HostedBoundary()
    {
        foreach (string association in new[] { "host", "face", "dependent", "nested" })
        {
            var f = new Fixture(); var hosted = f.AddHosted(association); hosted.Pinned = true;
            var baseline = f.Capture(); hosted.Placement.Origin = new XYZ(5, 2, 0);
            var unchangedPipes = new Dictionary<long, double> { [1] = 0, [2] = 0 };
            Check(!baseline.Validate(f.Doc, unchangedPipes, Tolerance, out string error), "Pinned hosted family movement must not be omitted: " + association);
        }
        var free = new Fixture(); var accessory = free.AddHosted("host"); var snapshot = free.Capture(); free.MoveRelated(false); accessory.Placement.Origin = new XYZ(5, 2, 0);
        Check(snapshot.Validate(free.Doc, free.Shifts, Tolerance, out string reason), "Unpinned hosted accessory can follow its host: " + reason);
        Check(snapshot.DescribeRelatedChanges(free.Doc, free.Shifts, Tolerance).Contains("2 fitting/phụ kiện"), "No-port hosted placement movement is included in reporting");
        foreach (string constraint in new[] { "group", "other-owner" })
        {
            var f = new Fixture(); var hosted = f.AddHosted("host");
            if (constraint == "group") hosted.GroupId = new ElementId(55);
            if (constraint == "other-owner") { f.Doc.IsWorkshared = true; f.Doc.OtherOwned.Add(hosted.Id.Value); }
            var baseline = f.Capture(); hosted.Placement.Origin = new XYZ(5, 2, 0);
            var unchangedPipes = new Dictionary<long, double> { [1] = 0, [2] = 0 };
            Check(!baseline.Validate(f.Doc, unchangedPipes, Tolerance, out string error), "Hosted constraint remains protected: " + constraint);
        }
        free = new Fixture(); accessory = free.AddHosted("host"); snapshot = free.Capture(); free.MoveRelated(false); accessory.Host = free.Root;
        Check(!snapshot.Validate(free.Doc, free.Shifts, Tolerance, out reason), "Existing host relation may not change");
        free = new Fixture(); accessory = free.AddHosted("host"); snapshot = free.Capture();
        accessory.Host = null; accessory.HostFace = new Reference { ElementId = free.Selected.Id };
        var unchanged = new Dictionary<long, double> { [1] = 0, [2] = 0 };
        Check(!snapshot.Validate(free.Doc, unchanged, Tolerance, out reason), "Same parent ID does not permit replacing the host relation role");
        free = new Fixture(); accessory = free.AddHosted("dependent"); snapshot = free.Capture(); free.MoveRelated(false); free.Doc.Elements.Remove(accessory.Id.Value);
        Check(!snapshot.Validate(free.Doc, free.Shifts, Tolerance, out reason), "Hosted accessory deletion must not be omitted");
        foreach (string association in new[] { "host", "nested" })
        {
            var f = new Fixture(); var parent = f.AddAncestor(association); var baseline = f.Capture();
            parent.Placement.Origin = new XYZ(11, 2, 0);
            var unchangedPipes = new Dictionary<long, double> { [1] = 0, [2] = 0 };
            Check(!baseline.Validate(f.Doc, unchangedPipes, Tolerance, out string error), "Standalone no-port ancestor equipment stays fixed: " + association);
        }
    }

    private static void ReferenceDiagnostic()
    {
        var f = new Fixture(); f.Root.Parameters.Remove(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
        TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
        string message = TaskDialog.Messages.Last();
        Check(message.Contains("đường kính ngoài"), "Reference OD failure must show actual diagnostic, got: " + message);
        Check(message.Contains("1"), "Reference diagnostic identifies the Pipe");
        Check(!f.Doc.GroupRolledBack && ((Line)((LocationCurve)f.Selected.Location).Curve).GetEndPoint(0).Z == 2, "Reference preflight does not mutate geometry");
    }

    private static void RunnerFlow()
    {
        var f = new Fixture(); f.Doc.MoveEffect = (id, delta) => { Check(id == f.Selected.Id && Math.Abs(delta.Z + 2) < 1E-9, "Runner moves selected pipe by planned Z only"); f.MoveRelated(true); };
        TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
        Check(TaskDialog.Messages.Last().StartsWith("Đã căn"), "Runner retains safe related geometry changes");
        Check(TaskDialog.Messages.Last().Contains("1 Pipe") && TaskDialog.Messages.Last().Contains("1 fitting/phụ kiện"), "Success reports the related Pipe and fitting affected outside selection");
        Check(TaskDialog.Messages.Last().Contains("3, 4"), "Success identifies the related changed elements");
        Check(!f.Doc.GroupRolledBack && ((Line)((LocationCurve)f.Selected.Location).Curve).GetEndPoint(0).Z == 0, "Successful alignment persists");
        Check(((Line)((LocationCurve)f.Root.Location).Curve).GetEndPoint(0).Z == 0, "Runner keeps reference fixed");
        f = new Fixture(); f.Doc.MoveEffect = (id, delta) => f.MoveRelated(false);
        f.Doc.CommitEffect = () => f.Selected.ConnectorManager.Connectors[1].DisconnectFrom(f.Fitting.MEPModel.ConnectorManager.Connectors[0]);
        TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
        Check(f.Doc.GroupRolledBack, "A new commit-time disconnect triggers group rollback");
        Check(((Line)((LocationCurve)f.Selected.Location).Curve).GetEndPoint(0).Z == 2, "Rollback path restores fixture geometry");
        Check(f.Selected.ConnectorManager.Connectors[1].IsConnected, "Rollback path restores fixture topology");
        var policy = new PipeLayerSafety.FailurePreprocessor();
        Check(policy.PreprocessFailures(new FailuresAccessor()) == FailureProcessingResult.Continue, "No posted failure may continue");
        var posted = new FailuresAccessor(); posted.Messages.Add(new FailureMessageAccessor { Text = "Would disconnect pipe" });
        Check(policy.PreprocessFailures(posted) == FailureProcessingResult.ProceedWithRollBack && policy.Error.Contains("disconnect"), "Posted failure requires rollback and reason");
    }

    private static void AllowOpenFlow()
    {
        TaskDialog.ConnectionResult = TaskDialogResult.CommandLink2;
        try
        {
            foreach (bool afterCommit in new[] { false, true })
            {
                var f = new Fixture(); var observed = f;
                f.Doc.MoveEffect = (id, delta) =>
                {
                    observed.MoveRelated(false);
                    if (!afterCommit) observed.Selected.ConnectorManager.Connectors[1].DisconnectFrom(observed.Fitting.MEPModel.ConnectorManager.Connectors[0]);
                };
                if (afterCommit) f.Doc.CommitEffect = () => observed.Selected.ConnectorManager.Connectors[1].DisconnectFrom(observed.Fitting.MEPModel.ConnectorManager.Connectors[0]);
                TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
                Check(TaskDialog.Messages.Last().StartsWith("Đã căn") && !f.Doc.GroupRolledBack,
                    "Explicit allow-open choice retains alignment despite a disconnect: " + TaskDialog.Messages.Last());
                Check(!f.Selected.ConnectorManager.Connectors[1].IsConnected && ((Line)((LocationCurve)f.Selected.Location).Curve).GetEndPoint(0).Z == 0,
                    "Allow-open retains real geometry/topology outcome, not only success text");
                Check(TaskDialog.Messages.Last().Contains("2:1") && TaskDialog.Messages.Last().Contains("4:0"),
                    "Success identifies both ends of the lost connection");
                string logLine = TaskDialog.Messages.Last().Split('\n').SingleOrDefault(line => line.StartsWith("Log kết nối: "));
                Check(logLine != null, "Allow-open provides a durable connection report path");
                string logPath = logLine.Substring("Log kết nối: ".Length);
                Check(File.ReadAllText(logPath).Contains("2:1") && File.ReadAllText(logPath).Contains("4:0"), "Persisted report contains the actual disconnected pair");
                File.Delete(logPath); // Only the unique report created by this fixture.
            }
            foreach (string defect in new[] { "gap", "angle", "offset" })
            {
                var f = new Fixture(); var observed = f;
                f.Doc.MoveEffect = (id, delta) =>
                {
                    observed.MoveRelated(false);
                    Connector port = observed.Fitting.MEPModel.ConnectorManager.Connectors[0];
                    if (defect == "gap") port.Origin = new XYZ(10,2,0.01);
                    if (defect == "angle") port.CoordinateSystem.BasisZ = new XYZ(-1,0,0.1).Normalize();
                    if (defect == "offset") port.Origin = new XYZ(10.01,2,0);
                };
                TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
                Check(!f.Doc.GroupRolledBack && TaskDialog.Messages.Last().StartsWith("Đã căn"), "Allow-open accepts observed connector geometry change: " + defect);
                Check(TaskDialog.Messages.Last().Contains("2:1") && TaskDialog.Messages.Last().Contains("4:0") && TaskDialog.Messages.Last().Contains("còn nối"), "Report distinguishes changed connector geometry from disconnected topology: " + defect);
                if (defect == "gap") Check(TaskDialog.Messages.Last().Contains((3.048).ToString("F3")), "Report converts the measured 0.01-foot gap into 3.048 mm");
                TaskDialog.ConnectionResult = TaskDialogResult.CommandLink1;
                f = new Fixture(); observed = f;
                f.Doc.MoveEffect = (id, delta) =>
                {
                    observed.MoveRelated(false);
                    Connector port = observed.Fitting.MEPModel.ConnectorManager.Connectors[0];
                    if (defect == "gap") port.Origin = new XYZ(10,2,0.01);
                    if (defect == "angle") port.CoordinateSystem.BasisZ = new XYZ(-1,0,0.1).Normalize();
                    if (defect == "offset") port.Origin = new XYZ(10.01,2,0);
                };
                TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
                Check(f.Doc.GroupRolledBack, "Strict choice still rejects connector gaps");
                TaskDialog.ConnectionResult = TaskDialogResult.CommandLink2;
            }
            foreach (string defect in new[] { "reference", "sideways", "slope", "height", "station", "type", "delete", "new-peer" })
            {
                var f = new Fixture(); var observed = f;
                f.Doc.MoveEffect = (id, delta) =>
                {
                    observed.MoveRelated(false);
                    observed.Selected.ConnectorManager.Connectors[1].DisconnectFrom(observed.Fitting.MEPModel.ConnectorManager.Connectors[0]);
                    if (defect == "reference") SetPipe(observed.Root, new XYZ(0,0,1), new XYZ(10,0,1));
                    if (defect == "sideways") SetPipe(observed.Selected, new XYZ(0,3,0), new XYZ(10,3,0));
                    if (defect == "slope") SetPipe(observed.Selected, new XYZ(0,2,0), new XYZ(10,2,1));
                    if (defect == "height") SetPipe(observed.Selected, new XYZ(0,2,1), new XYZ(10,2,1));
                    if (defect == "station") SetPipe(observed.Selected, new XYZ(6,2,0), new XYZ(10,2,0));
                    if (defect == "type") observed.Selected.TypeId = new ElementId(99);
                    if (defect == "delete") observed.Doc.Elements.Remove(observed.Fitting.Id.Value);
                    if (defect == "new-peer") observed.Selected.ConnectorManager.Connectors[1].ConnectTo(observed.Neighbor.ConnectorManager.Connectors[1]);
                };
                TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
                Check(f.Doc.GroupRolledBack && !TaskDialog.Messages.Last().StartsWith("Đã căn"), "Allow-open must not allow unrelated damage: " + defect);
                Check(f.Selected.TypeId.Value == 10 && f.Doc.Elements.ContainsKey(4), "Rollback restores type and document membership: " + defect);
            }
            var cancelled = new Fixture();
            TaskDialog.ConnectionResult = TaskDialogResult.Cancel;
            TaskDialog.Messages.Clear(); PipeLayerRunner.Run(cancelled.Ui(), cancelled.Doc);
            Check(TaskDialog.Messages.Count == 0 && ((Line)((LocationCurve)cancelled.Selected.Location).Curve).GetEndPoint(0).Z == 2,
                "Cancel connection choice performs no movement");
            var warningPolicy = new PipeLayerSafety.FailurePreprocessor(true);
            var posted = new FailuresAccessor(); posted.Messages.Add(new FailureMessageAccessor { Text = "Pipe disconnected", Severity = FailureSeverity.Warning });
            Check(warningPolicy.PreprocessFailures(posted) == FailureProcessingResult.Continue && posted.Messages.Count == 1,
                "Allow-open passes warnings to native Revit without deleting them");
            posted.Messages.Add(new FailureMessageAccessor { Text = "Invalid geometry", Severity = FailureSeverity.Error });
            Check(warningPolicy.PreprocessFailures(posted) == FailureProcessingResult.ProceedWithRollBack,
                "Allow-open never accepts an error mixed with warnings");
            posted.Messages.Clear(); posted.Messages.Add(new FailureMessageAccessor { Text = "Corrupt", Severity = FailureSeverity.DocumentCorruption });
            Check(warningPolicy.PreprocessFailures(posted) == FailureProcessingResult.ProceedWithRollBack, "Corruption always rolls back");
        }
        finally { TaskDialog.ConnectionResult = TaskDialogResult.CommandLink1; }
    }

    private static void NativeFailureFlow()
    {
        TaskDialog.ConnectionResult = TaskDialogResult.CommandLink2;
        try
        {
            foreach (string failure in new[] { "warning", "error", "native-cancel", "group" })
            {
                var f = new Fixture(); var observed = f;
                f.Doc.MoveEffect = (id, delta) =>
                {
                    observed.MoveRelated(false);
                    if (failure != "group") observed.Selected.ConnectorManager.Connectors[1].DisconnectFrom(observed.Fitting.MEPModel.ConnectorManager.Connectors[0]);
                };
                if (failure == "warning" || failure == "error")
                    f.Doc.PostedFailures.Messages.Add(new FailureMessageAccessor { Text = "Native failure " + failure, Severity = failure == "warning" ? FailureSeverity.Warning : FailureSeverity.Error });
                if (failure == "native-cancel") f.Doc.CommitResult = TransactionStatus.RolledBack;
                if (failure == "group")
                {
                    f.Doc.AssimilateResult = TransactionStatus.RolledBack;
                    f.Doc.CommitEffect = () => observed.Selected.ConnectorManager.Connectors[1].DisconnectFrom(observed.Fitting.MEPModel.ConnectorManager.Connectors[0]);
                }
                TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
                string message = TaskDialog.Messages.Last();
                Check(message.StartsWith("Đã căn") == (failure == "warning"), "Real runner handles commit outcome: " + failure + ": " + message);
                Check(((Line)((LocationCurve)f.Selected.Location).Curve).GetEndPoint(0).Z == (failure == "warning" ? 0 : 2), "Native failure contract restores observed geometry: " + failure);
                string path = message.Split('\n').Single(line => line.StartsWith("Log kết nối: ")).Substring("Log kết nối: ".Length);
                string report = File.ReadAllText(path);
                Check(report.Contains("2:1") && report.Contains("4:0"), "Evidence survives native commit/group rollback: " + failure);
                Check(report.StartsWith("Đã căn") == (failure == "warning"), "Persisted outcome never claims success for a failed group/commit");
                if (failure == "group") Check(report.Contains("TRƯỚC lưu nhóm"), "Group failure evidence is explicitly labelled as the final pre-group snapshot");
                if (failure == "error" || failure == "native-cancel") Check(report.Contains("TRƯỚC commit"), "Native rollback evidence is explicitly labelled pre-commit");
                File.Delete(path);
            }
            TaskDialog.ConnectionResult = TaskDialogResult.CommandLink1;
            var strict = new Fixture(); strict.Doc.MoveEffect = (id, delta) => strict.MoveRelated(false);
            strict.Doc.PostedFailures.Messages.Add(new FailureMessageAccessor { Text = "Native warning" });
            TaskDialog.Messages.Clear(); PipeLayerRunner.Run(strict.Ui(), strict.Doc);
            Check(strict.Doc.GroupRolledBack && !TaskDialog.Messages.Last().StartsWith("Đã căn"), "Strict runner still rejects native warnings");
        }
        finally { TaskDialog.ConnectionResult = TaskDialogResult.CommandLink1; }
    }

    // The real runner must retain safe axial resizing, rather than accepting only
    // rigid endpoint translations. The double provides observed solver geometry.
    private static void SelectedResizeFlow()
    {
        var f = new Fixture();
        f.Doc.MoveEffect = (id, delta) =>
        {
            f.MoveRelated(false);
            SetPipe(f.Selected, new XYZ(-2,2,0), new XYZ(10,2,0));
        };
        TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
        Check(TaskDialog.Messages.Last().StartsWith("Đã căn"), "Selected Pipe axial stretch must be retained: " + TaskDialog.Messages.Last());
        Check(!f.Doc.GroupRolledBack && ((Line)((LocationCurve)f.Selected.Location).Curve).GetEndPoint(0).X == -2,
            "Successful selected resize persists and is not only reported");
        foreach (string defect in new[] { "reference", "sideways", "slope", "height", "station", "disconnect" })
        {
            f = new Fixture(); var observed = f;
            f.Doc.MoveEffect = (id, delta) =>
            {
                observed.MoveRelated(false);
                if (defect == "reference") SetPipe(observed.Root, new XYZ(-1,0,0), new XYZ(10,0,0));
                if (defect == "sideways") SetPipe(observed.Selected, new XYZ(-2,3,0), new XYZ(10,2,0));
                if (defect == "slope") SetPipe(observed.Selected, new XYZ(-2,2,1), new XYZ(10,2,0));
                if (defect == "height") SetPipe(observed.Selected, new XYZ(-2,2,1), new XYZ(10,2,1));
                if (defect == "station") SetPipe(observed.Selected, new XYZ(6,2,0), new XYZ(10,2,0));
                if (defect == "disconnect") observed.Selected.ConnectorManager.Connectors[1].DisconnectFrom(observed.Fitting.MEPModel.ConnectorManager.Connectors[0]);
            };
            TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
            Check(f.Doc.GroupRolledBack && !TaskDialog.Messages.Last().StartsWith("Đã căn"), "Resize must not hide unsafe outcome: " + defect);
            if (defect == "reference") Check(TaskDialog.Messages.Last().Contains("Ống gốc 1"), "Diagnostic distinguishes the fixed reference from movable selected Pipes");
        }
        f = new Fixture(); var fixture = f;
        Pipe sloped = f.AddPipe(7, new XYZ(0,4,4), new XYZ(10,4,5));
        f.Doc.MoveEffect = (id, delta) =>
        {
            if (id == fixture.Selected.Id)
            {
                fixture.MoveRelated(false);
                SetPipe(sloped, new XYZ(-2,4,3.8), new XYZ(10,4,5));
            }
            else
            {
                Check(id == sloped.Id && Math.Abs(delta.Z + 4.5) < 1e-9,
                    "Sloped resized Pipe shift is measured at a fixed XY station, not averaged endpoints");
                SetPipe(sloped, new XYZ(-2,4,-0.7), new XYZ(10,4,0.5));
            }
        };
        UIDocument ui = f.Ui(); ui.Selection.Selected = new[] { f.Root.Id, f.Selected.Id, sloped.Id };
        TaskDialog.Messages.Clear(); PipeLayerRunner.Run(ui, f.Doc);
        Check(TaskDialog.Messages.Last().StartsWith("Đã căn"), "Already resized sloped selected Pipe must be handled: " + TaskDialog.Messages.Last());
        foreach (string constraint in new[] { "none", "pin", "group", "other-owner" })
        {
            f = new Fixture(); fixture = f;
            Pipe aligned = f.AddPipe(7, new XYZ(0,4,0), new XYZ(10,4,0));
            if (constraint == "pin") aligned.Pinned = true;
            if (constraint == "group") aligned.GroupId = new ElementId(55);
            if (constraint == "other-owner") { f.Doc.IsWorkshared = true; f.Doc.OtherOwned.Add(aligned.Id.Value); }
            f.Doc.MoveEffect = (id, delta) =>
            {
                fixture.MoveRelated(false);
                SetPipe(aligned, new XYZ(-2,4,0), new XYZ(10,4,0));
            };
            ui = f.Ui(); ui.Selection.Selected = new[] { f.Root.Id, f.Selected.Id, aligned.Id };
            TaskDialog.Messages.Clear(); PipeLayerRunner.Run(ui, f.Doc);
            Check(TaskDialog.Messages.Last().StartsWith("Đã căn") == (constraint == "none"),
                "A zero-shift selected Pipe is not the reference, but locked geometry remains fixed: " + constraint);
        }
        f = new Fixture(); fixture = f;
        f.Doc.MoveEffect = (id, delta) => fixture.MoveRelated(false);
        f.Doc.CommitEffect = () => SetPipe(fixture.Selected, new XYZ(-2,2,0), new XYZ(10,2,0));
        TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
        Check(TaskDialog.Messages.Last().StartsWith("Đã căn"), "Commit-time safe axial resize is accepted by post-commit validation");
        f = new Fixture(); fixture = f;
        f.Doc.MoveEffect = (id, delta) => fixture.MoveRelated(false);
        f.Doc.CommitEffect = () => SetPipe(fixture.Root, new XYZ(-1,0,0), new XYZ(10,0,0));
        TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
        Check(f.Doc.GroupRolledBack && TaskDialog.Messages.Last().Contains("Sau khi Revit commit"),
            "A commit-time reference resize is rolled back and its stage is identified");
        foreach (var choice in new[] { (Mode: TaskDialogResult.CommandLink1, Datum: -0.1, Shift: -2.1),
                                      (Mode: TaskDialogResult.CommandLink3, Datum: 0.1, Shift: -1.9) })
        {
            f = new Fixture(); fixture = f;
            f.Root.Parameters[BuiltInParameter.RBS_PIPE_OUTER_DIAMETER].Value = 0.4;
            f.Root.Parameters[BuiltInParameter.RBS_PIPE_DIAMETER_PARAM].Value = 0.4;
            foreach (Connector port in f.Root.ConnectorManager.Connectors) port.Radius = 0.2;
            f.Doc.MoveEffect = (id, delta) =>
            {
                Check(id == fixture.Selected.Id && Math.Abs(delta.Z - choice.Shift) < 1e-9,
                    "BOP/TOP resize applies OD-aware target delta through real Runner");
                fixture.MoveRelated(false);
                SetPipe(fixture.Selected, new XYZ(-2,2,choice.Datum), new XYZ(10,2,choice.Datum));
                SetPipe(fixture.Neighbor, new XYZ(12,2,choice.Datum), new XYZ(20,2,choice.Datum));
                fixture.Fitting.Placement.Origin = new XYZ(11,2,choice.Datum);
                fixture.Fitting.MEPModel.ConnectorManager.Connectors[0].Origin = new XYZ(10,2,choice.Datum);
                fixture.Fitting.MEPModel.ConnectorManager.Connectors[1].Origin = new XYZ(12,2,choice.Datum);
            };
            TaskDialog.NextResult = choice.Mode;
            try
            {
                TaskDialog.Messages.Clear(); PipeLayerRunner.Run(f.Ui(), f.Doc);
                Check(TaskDialog.Messages.Last().StartsWith("Đã căn") &&
                    Math.Abs(((Line)((LocationCurve)f.Selected.Location).Curve).GetEndPoint(0).Z - choice.Datum) < 1e-9,
                    "Unequal OD BOP/TOP with selected resize persists at the correct datum: " + TaskDialog.Messages.Last());
            }
            finally { TaskDialog.NextResult = TaskDialogResult.CommandLink2; }
        }
    }

    private sealed class Fixture
    {
        internal readonly Document Doc = new Document();
        internal readonly Pipe Root, Selected, Neighbor;
        internal readonly FamilyInstance Fitting;
        internal readonly Dictionary<long, double> Shifts = new Dictionary<long, double> { [1] = 0, [2] = -2 };
        internal Fixture()
        {
            Root = AddPipe(1, new XYZ(0, 0, 0), new XYZ(10, 0, 0));
            Selected = AddPipe(2, new XYZ(0, 2, 2), new XYZ(10, 2, 2));
            Neighbor = AddPipe(3, new XYZ(12, 2, 2), new XYZ(20, 2, 2));
            Fitting = new FamilyInstance { Id = new ElementId(4), UniqueId = "fixture-4", Category = new Category { Id = new ElementId((long)BuiltInCategory.OST_PipeFitting) }, MEPModel = new MEPModel(), Placement = new Transform { Origin = new XYZ(11, 2, 2) } };
            AddPort(Fitting, Fitting.MEPModel.ConnectorManager, 0, new XYZ(10, 2, 2), new XYZ(-1, 0, 0));
            AddPort(Fitting, Fitting.MEPModel.ConnectorManager, 1, new XYZ(12, 2, 2), XYZ.BasisX);
            Doc.Add(Fitting); Selected.ConnectorManager.Connectors[1].ConnectTo(Fitting.MEPModel.ConnectorManager.Connectors[0]); Neighbor.ConnectorManager.Connectors[0].ConnectTo(Fitting.MEPModel.ConnectorManager.Connectors[1]);
        }
        internal Pipe AddPipe(long id, XYZ a, XYZ b)
        {
            var pipe = new Pipe { Id = new ElementId(id), UniqueId = "fixture-" + id, Category = new Category { Id = new ElementId((long)BuiltInCategory.OST_PipeCurves) } };
            pipe.Parameters[BuiltInParameter.RBS_PIPE_OUTER_DIAMETER] = new Parameter { Value = 0.2 };
            pipe.Parameters[BuiltInParameter.RBS_PIPE_DIAMETER_PARAM] = new Parameter { Value = 0.2 };
            pipe.Parameters[BuiltInParameter.RBS_PIPE_SLOPE] = new Parameter { Value = 0.0 };
            pipe.Parameters[BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM] = new Parameter { Value = new ElementId(100) };
            AddPort(pipe, pipe.ConnectorManager, 0, a, new XYZ(-1, 0, 0)); AddPort(pipe, pipe.ConnectorManager, 1, b, XYZ.BasisX); SetPipe(pipe, a, b); Doc.Add(pipe); return pipe;
        }
        internal FamilyInstance AddHosted(string association)
        {
            var family = new FamilyInstance { Id = new ElementId(5), UniqueId = "hosted-5", Category = new Category { Id = new ElementId((long)BuiltInCategory.OST_PipeAccessory) }, Placement = new Transform { Origin = new XYZ(5, 2, 2) } };
            if (association == "host") family.Host = Selected;
            if (association == "face") family.HostFace = new Reference { ElementId = Selected.Id };
            if (association == "dependent") Selected.Dependents.Add(family.Id);
            if (association == "nested") family.SuperComponent = Fitting;
            Doc.Add(family); return family;
        }
        internal FamilyInstance AddAncestor(string association)
        {
            var parent = new FamilyInstance { Id = new ElementId(6), UniqueId = "parent-6", Category = new Category { Id = new ElementId((long)BuiltInCategory.OST_MechanicalEquipment) }, Placement = new Transform { Origin = new XYZ(11, 2, 2) } };
            if (association == "host") Fitting.Host = parent;
            if (association == "nested") Fitting.SuperComponent = parent;
            Doc.Add(parent); return parent;
        }
        internal PipeLayerSafety Capture()
        {
            Check(PipeLayerSafety.TryCapture(Doc, new[] { Root, Selected }, out var baseline, out string error), "Capture supported fixture: " + error); return baseline;
        }
        internal void MoveRelated(bool stretch)
        {
            SetPipe(Selected, new XYZ(0, 2, 0), new XYZ(10, 2, 0));
            SetPipe(Neighbor, new XYZ(12, 2, 0), new XYZ(20, 2, stretch ? 2 : 0));
            Fitting.Placement.Origin = new XYZ(11, 2, 0);
            var ports = Fitting.MEPModel.ConnectorManager.Connectors; ports[0].Origin = new XYZ(10, 2, 0); ports[1].Origin = new XYZ(12, 2, 0);
            ports[1].CoordinateSystem.BasisZ = new XYZ(8, 0, stretch ? 2 : 0).Normalize();
        }
        internal UIDocument Ui()
        {
            var ui = new UIDocument(); ui.Selection.Selected = new[] { Root.Id, Selected.Id }; ui.Selection.Pick = new Reference { ElementId = Root.Id, GlobalPoint = new XYZ(5, 0, 0) }; return ui;
        }
    }
    private static void SetPipe(Pipe pipe, XYZ a, XYZ b)
    {
        pipe.Location = new LocationCurve { Curve = Line.CreateBound(a, b) }; var direction = b.Subtract(a).Normalize(); var ports = pipe.ConnectorManager.Connectors; ports[0].Origin = a; ports[1].Origin = b; ports[0].CoordinateSystem.BasisZ = direction.Multiply(-1); ports[1].CoordinateSystem.BasisZ = direction;
        pipe.Parameters[BuiltInParameter.RBS_PIPE_SLOPE].Value = (b.Z - a.Z) / Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    }
    private static void AddPort(Element owner, ConnectorManager manager, int id, XYZ point, XYZ axis) => manager.Connectors.Add(new Connector { Owner = owner, Id = id, Origin = point, CoordinateSystem = new Transform { Origin = point, BasisZ = axis } });
    private static void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
}
