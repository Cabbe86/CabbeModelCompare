using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace Cabbe.Revit.Compare
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "Cabbe";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Fliken kan redan finnas från ett annat Cabbe-tillägg
            }

            RibbonPanel? panel = null;
            var panels = application.GetRibbonPanels(tabName);
            foreach (var p in panels)
            {
                if (p.Name == "Jämförelse")
                {
                    panel = p;
                    break;
                }
            }

            if (panel == null)
            {
                panel = application.CreateRibbonPanel(tabName, "Jämförelse");
            }

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "cmdModelCompare",
                "Modell-\njämförelse",
                thisAssemblyPath,
                "Cabbe.Revit.Compare.Command"
            )
            {
                ToolTip = "Jämför aktiv RVT-modell mot en inlänkad version med ACC-stil (Grön = Ny, Orange = Ändrad, Röd = Borttagen)."
            };

            panel.AddItem(buttonData);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
