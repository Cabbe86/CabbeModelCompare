using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Cabbe.Revit.Compare.Models;
using Cabbe.Revit.Compare.Services;

namespace Cabbe.Revit.Compare
{
    public enum CompareAction
    {
        None,
        RunCompare,
        ApplyColors,
        ResetView,
        SelectAndZoom
    }

    public class CompareExternalEventHandler : IExternalEventHandler
    {
        public CompareAction PendingAction { get; set; } = CompareAction.None;
        public ComparisonItem? TargetItem { get; set; }
        public ElementId? TargetElementId { get; set; }
        public RevitLinkInstance? SelectedLink { get; set; }
        public List<BuiltInCategory>? SelectedCategories { get; set; }
        public List<ComparisonItem>? ComparisonItems { get; set; }

        public Action<List<ComparisonItem>>? OnCompareCompleted { get; set; }
        public Action<string>? OnStatusMessage { get; set; }

        public void Execute(UIApplication uiapp)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null) return;

            Document doc = uidoc.Document;
            CompareEngine engine = new CompareEngine(doc);

            try
            {
                switch (PendingAction)
                {
                    case CompareAction.RunCompare:
                        if (SelectedLink != null && SelectedCategories != null)
                        {
                            var results = engine.Compare(SelectedLink, SelectedCategories);
                            OnCompareCompleted?.Invoke(results);
                            OnStatusMessage?.Invoke($"Jämförelse klar: {results.Count} objekt granskade.");
                        }
                        break;

                    case CompareAction.ApplyColors:
                        if (ComparisonItems != null && SelectedLink != null && uidoc.ActiveView != null)
                        {
                            using (Transaction t = new Transaction(doc, "Applicera ACC Modelljämförelse-färger"))
                            {
                                t.Start();
                                engine.ApplyACCGraphicOverrides(uidoc.ActiveView, ComparisonItems, SelectedLink);
                                t.Commit();
                            }
                            uidoc.RefreshActiveView();
                            OnStatusMessage?.Invoke("Färger och spökobjekt applicerade i vyn.");
                        }
                        break;

                    case CompareAction.ResetView:
                        if (uidoc.ActiveView != null)
                        {
                            using (Transaction t = new Transaction(doc, "Återställ jämförelsevy"))
                            {
                                t.Start();
                                engine.ResetViewAndClearVisuals(uidoc.ActiveView);
                                t.Commit();
                            }
                            uidoc.RefreshActiveView();
                            OnStatusMessage?.Invoke("Vyn har återställts till ursprungliga färger.");
                        }
                        break;

                    case CompareAction.SelectAndZoom:
                        if (TargetItem != null)
                        {
                            if (TargetItem.HostId != null && TargetItem.HostId != ElementId.InvalidElementId)
                            {
                                uidoc.Selection.SetElementIds(new List<ElementId> { TargetItem.HostId });
                                uidoc.ShowElements(TargetItem.HostId);
                            }
                            else if (TargetItem.Status == ComparisonStatus.Removed && SelectedLink != null)
                            {
                                DirectShape? ghostDs = null;
                                using (Transaction t = new Transaction(doc, "Skapa/visa borttaget spökobjekt"))
                                {
                                    t.Start();
                                    ghostDs = engine.GetOrCreateGhostDirectShape(uidoc.ActiveView, TargetItem, SelectedLink);
                                    t.Commit();
                                }

                                if (ghostDs != null)
                                {
                                    uidoc.RefreshActiveView();
                                    uidoc.Selection.SetElementIds(new List<ElementId> { ghostDs.Id });
                                    uidoc.ShowElements(ghostDs.Id);
                                }
                                else if (TargetItem.LinkId != null)
                                {
                                    Document linkDoc = SelectedLink.GetLinkDocument();
                                    if (linkDoc != null)
                                    {
                                        Element? linkElem = linkDoc.GetElement(TargetItem.LinkId);
                                        BoundingBoxXYZ? bbox = linkElem?.get_BoundingBox(null);
                                        if (bbox != null)
                                        {
                                            Transform tr = SelectedLink.GetTotalTransform();
                                            XYZ min = tr.OfPoint(bbox.Min);
                                            XYZ max = tr.OfPoint(bbox.Max);
                                            UIView? uiview = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == uidoc.ActiveView.Id);
                                            uiview?.ZoomAndCenterRectangle(min, max);
                                        }
                                    }
                                }
                            }
                        }
                        else if (TargetElementId != null && TargetElementId != ElementId.InvalidElementId)
                        {
                            uidoc.Selection.SetElementIds(new List<ElementId> { TargetElementId });
                            uidoc.ShowElements(TargetElementId);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Modelljämförelse - Fel", "Ett fel uppstod: " + ex.Message);
            }
            finally
            {
                PendingAction = CompareAction.None;
            }
        }

        public string GetName() => "Cabbe Model Compare Event Handler";
    }
}
