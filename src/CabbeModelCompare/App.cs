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
                // Fliken kan redan finnas från pyRevit eller ett annat Cabbe-tillägg
            }

            RibbonPanel? panel = null;
            try
            {
                var panels = application.GetRibbonPanels(tabName);
                foreach (var p in panels)
                {
                    if (p.Name.Equals("Compare", StringComparison.OrdinalIgnoreCase))
                    {
                        panel = p;
                        break;
                    }
                }
            }
            catch { }

            if (panel == null)
            {
                try
                {
                    panel = application.CreateRibbonPanel(tabName, "Compare");
                }
                catch
                {
                    panel = application.CreateRibbonPanel("Compare");
                }
            }

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "cmdCabbeModelCompare",
                "Model\nCompare",
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
