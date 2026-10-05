using Autodesk.Revit.Exceptions;
using Nice3point.Revit.Toolkit.External;

namespace VP_RevitAPI.Commands.Common;

/// <summary>
/// Compatibility base for recovered commands. TungMEP2 performs no license checks.
/// </summary>
public abstract class SecuredExternalCommand : ExternalCommand
{
    public sealed override void Execute()
    {
        try
        {
            ExecuteCore();
        }
        catch (OperationCanceledException)
        {
            // Cancelling a Revit pick operation is an expected user action.
        }
    }

    protected abstract void ExecuteCore();
}
