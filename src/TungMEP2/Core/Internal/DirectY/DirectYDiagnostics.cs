using System;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using VP_RevitAPI.Internal.Ctn.FitSpace;

namespace VP_RevitAPI.Internal.Ctn.DirectY;

// Logging is best effort and must never hide the original rollback/failure.
internal sealed class DirectYDiagnostics
{
    internal string Path { get; private set; }
    internal string Stage { get; private set; }
    internal DirectYDiagnostics()
    {
        try
        {
            string directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TungMEP2", "Logs");
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "DirectY-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log");
            File.WriteAllText(Path, "DirectY native insertion / cleanup diagnostic (UTC)\r\n", Encoding.UTF8);
        }
        catch { Path = null; }
    }

    internal void Write(string stage, string message)
    {
        Stage = stage;
        if (Path == null) return;
        try { File.AppendAllText(Path, DateTime.UtcNow.ToString("O") + " [" + stage + "] " + message + "\r\n", Encoding.UTF8); }
        catch { Path = null; }
    }

    internal void Element(string stage, Autodesk.Revit.DB.Element element, string disposition = "")
    {
        try
        {
            if (element == null) { Write(stage, "element missing " + disposition); return; }
            Write(stage, $"ID={element.Id.Value}; UID={element.UniqueId}; class={element.GetType().FullName}; category={element.Category?.Name}; categoryId={element.Category?.Id.Value}; typeId={element.GetTypeId().Value}; name={element.Name}; {disposition}");
        }
        catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
        { Write(stage, "metadata unavailable: " + exception.Message + "; " + disposition); }
    }

    internal string WithLog(string message)
    {
        Write("result", message);
        return Path == null ? message : message + "\nLog chi tiết: " + Path;
    }

    internal void Binding(string stage, Document document, FitSpaceConnectorBinding binding, double tolerance)
    {
        // Native getters are diagnostic only. Never turn an unavailable measurement into
        // success, and never swallow RegenerationFailedException to continue model access.
        string identity = $"first={binding?.First?.OwnerId?.Value}:{binding?.First?.ConnectorId}; second={binding?.Second?.OwnerId?.Value}:{binding?.Second?.ConnectorId}";
        try
        {
            Connector first = FitSpaceConnectorService.FindByKey(document, binding?.First);
            Connector second = FitSpaceConnectorService.FindByKey(document, binding?.Second);
            XYZ firstOrigin = first?.Origin, secondOrigin = second?.Origin;
            XYZ firstAxis = first?.CoordinateSystem?.BasisZ, secondAxis = second?.CoordinateSystem?.BasisZ;
            double? gap = firstOrigin != null && secondOrigin != null ? firstOrigin.DistanceTo(secondOrigin) : (double?)null;
            double? snapshotGap = binding?.First?.OriginalOrigin != null && binding?.Second?.OriginalOrigin != null
                ? binding.First.OriginalOrigin.DistanceTo(binding.Second.OriginalOrigin) : (double?)null;
            double? dot = firstAxis != null && secondAxis != null && firstAxis.GetLength() > 1e-9 && secondAxis.GetLength() > 1e-9
                ? firstAxis.Normalize().DotProduct(secondAxis.Normalize()) : (double?)null;
            string report = DirectYBindingDiagnostic.Format(binding?.First?.OwnerId?.Value ?? -1, binding?.First?.ConnectorId ?? -1,
                binding?.Second?.OwnerId?.Value ?? -1, binding?.Second?.ConnectorId ?? -1,
                FitSpaceConnectorService.IsConnected(first, second), FitSpaceConnectorService.IsConnected(second, first),
                gap, snapshotGap, dot, tolerance);
            Write(stage, report + $"; firstOrigin={firstOrigin}; secondOrigin={secondOrigin}; firstAxis={firstAxis}; secondAxis={secondAxis}");
            Element(stage + "/first", first?.Owner);
            Element(stage + "/second", second?.Owner);
        }
        catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
        { Write(stage, identity + "; binding measurement unavailable: " + exception.Message); }
    }

    internal void Binding(string stage, Document document, Connector first, Connector second, double tolerance)
    {
        // These keys are supplemental measurements, not preconditions. Ordinary native
        // metadata failures must not abort the existing command before its real guards.
        DirectYBindingDiagnostic.Observe(
            () => Binding(stage, document, new FitSpaceConnectorBinding(
                FitSpaceConnectorService.CreateKey(first), FitSpaceConnectorService.CreateKey(second)), tolerance),
            error => error is Autodesk.Revit.Exceptions.RegenerationFailedException,
            error => Write(stage, "initial binding capture unavailable: " + error.Message));
    }

    internal void BindingToSinglePeer(string stage, Document document, Connector first, double tolerance)
    {
        DirectYBindingDiagnostic.Observe(
            () =>
            {
                if (DirectYTopology.SinglePeer(first, out Connector second))
                    Binding(stage, document, first, second, tolerance);
                else Write(stage, "initial boundary has no single physical peer; no binding measurement");
            },
            error => error is Autodesk.Revit.Exceptions.RegenerationFailedException,
            error => Write(stage, "initial peer capture unavailable: " + error.Message));
    }
}

// Must escape candidate retry loops: rollback was not confirmed, so no further model reads are safe.
internal sealed class DirectYAbortException : Exception
{
    internal DirectYAbortException(string message) : base(message) { }
}

internal static class DirectYTransactions
{
    // pending also means unconfirmed native transaction state to callers; never claim unchanged
    // model unless the owning group is confirmed rolled back. No document reads here.
    internal static bool TryRollbackOwned(Transaction transaction, TransactionGroup group, out string error)
    {
        error = string.Empty;
        try
        {
            TransactionStatus status = transaction.GetStatus();
            if (status == TransactionStatus.Pending)
            { error = "Revit đang ở trạng thái Pending; chưa xác nhận rollback."; return false; }
            if (status == TransactionStatus.Started && transaction.RollBack() != TransactionStatus.RolledBack)
            { error = "Không xác nhận được rollback transaction DirectY."; return false; }
            TransactionStatus groupStatus = group.GetStatus();
            if (groupStatus == TransactionStatus.Started) groupStatus = group.RollBack();
            if (groupStatus != TransactionStatus.RolledBack)
            { error = "Không xác nhận được rollback TransactionGroup DirectY: " + groupStatus + "."; return false; }
            return true;
        }
        catch (Exception exception)
        { error = "Không xác nhận được rollback DirectY: " + exception.Message; return false; }
    }
}
