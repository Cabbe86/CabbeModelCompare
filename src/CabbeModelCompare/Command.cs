using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Cabbe.Revit.Compare.UI;

namespace Cabbe.Revit.Compare
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        private static CompareWindow? _activeWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                TaskDialog.Show("Modelljämförelse", "Öppna ett Revit-projekt innan du startar modelljämförelsen.");
                return Result.Cancelled;
            }

            try
            {
                // Om fönstret redan är öppet, aktivera det
                if (_activeWindow != null && _activeWindow.IsLoaded)
                {
                    _activeWindow.Activate();
                    return Result.Succeeded;
                }

                var handler = new CompareExternalEventHandler();
                var extEvent = ExternalEvent.Create(handler);

                _activeWindow = new CompareWindow(uidoc, handler, extEvent);
                new WindowInteropHelper(_activeWindow).Owner = uiapp.MainWindowHandle;
                _activeWindow.Closed += (s, e) => _activeWindow = null;
                _activeWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Fel vid start av Modelljämförelse", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
