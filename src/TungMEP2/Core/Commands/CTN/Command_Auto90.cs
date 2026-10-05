using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using VP_RevitAPI.Commands.Common;
using VP_RevitAPI.Internal.Ctn;

namespace VP_RevitAPI.Commands.CTN;

[Transaction(TransactionMode.Manual)]
public class Command_Auto90 : SecuredExternalCommand
{
    protected override void ExecuteCore()
    {
        try { Auto90Runner.Run(base.UiDocument, base.Document); }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            TaskDialog.Show("TungMEP2 - Auto90", "Auto90 không thể hoàn tất: " + exception.Message);
        }
    }
}
