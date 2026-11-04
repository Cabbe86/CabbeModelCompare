using System;
using System.Collections.Generic;
using System.Linq;
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

        public bool OnlyGeometry { get; set; } = true;
        public ViewColorMode ColorMode { get; set; } = ViewColorMode.All;
        public bool HalftoneUnchanged { get; set; } = true;

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
                            var results = engine.Compare(SelectedLink, SelectedCategories, OnlyGeometry);
                            OnCompareCompleted?.Invoke(results);
                            string modeText = OnlyGeometry ? "ren geometri" : "geometri & parametrar";
                            OnStatusMessage?.Invoke($"Jämförelse klar ({modeText}): {results.Count} objekt granskade.");
                        }
                        break;

                    case CompareAction.ApplyColors:
                        if (ComparisonItems != null && SelectedLink != null && uidoc.ActiveView != null)
                        {
                            using (Transaction t = new Transaction(doc, "Applicera ACC Modelljämförelse-färger"))
                            {
                                t.Start();
                                engine.ApplyACCGraphicOverrides(uidoc.ActiveView, ComparisonItems, SelectedLink, ColorMode, HalftoneUnchanged);
                                t.Commit();
                            }
                            uidoc.RefreshActiveView();
                            string colorText = ColorMode switch
                            {
                                ViewColorMode.AddedOnly => "Endast tillagda (grönt)",
                                ViewColorMode.ModifiedOnly => "Endast ändrade (orange)",
                                ViewColorMode.RemovedOnly => "Endast borttagna (rött)",
                                _ => "Alla ändringar (grönt, orange, rött)"
                            };
                            OnStatusMessage?.Invoke($"Färgvy applicerad: {colorText}.");
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
                            Element? hostElem = TargetItem.HostId != null && TargetItem.HostId != ElementId.InvalidElementId
                                ? doc.GetElement(TargetItem.HostId)
                                : null;

                            BoundingBoxXYZ? targetBbox = null;
                            if (hostElem != null)
                            {
                                targetBbox = hostElem.get_BoundingBox(null);
                            }
                            else if (TargetItem.Status == ComparisonStatus.Removed && SelectedLink != null)
                            {
                                Document linkDoc = SelectedLink.GetLinkDocument();
                                Element? linkElem = TargetItem.LinkId != null ? linkDoc?.GetElement(TargetItem.LinkId) : null;
                                if (linkElem == null && !string.IsNullOrEmpty(TargetItem.UniqueId))
                                {
                                    linkElem = linkDoc?.GetElement(TargetItem.UniqueId);
                                }

                                if (linkElem != null)
                                {
                                    Transform tr = SelectedLink.GetTotalTransform();
                                    BoundingBoxXYZ? rawBbox = null;
                                    try { rawBbox = linkElem.get_BoundingBox(null); } catch { }

                                    if (rawBbox != null)
                                    {
                                        XYZ[] pts = new XYZ[8]
                                        {
                                            tr.OfPoint(new XYZ(rawBbox.Min.X, rawBbox.Min.Y, rawBbox.Min.Z)),
                                            tr.OfPoint(new XYZ(rawBbox.Max.X, rawBbox.Min.Y, rawBbox.Min.Z)),
                                            tr.OfPoint(new XYZ(rawBbox.Min.X, rawBbox.Max.Y, rawBbox.Min.Z)),
                                            tr.OfPoint(new XYZ(rawBbox.Max.X, rawBbox.Max.Y, rawBbox.Min.Z)),
                                            tr.OfPoint(new XYZ(rawBbox.Min.X, rawBbox.Min.Y, rawBbox.Max.Z)),
                                            tr.OfPoint(new XYZ(rawBbox.Max.X, rawBbox.Min.Y, rawBbox.Max.Z)),
                                            tr.OfPoint(new XYZ(rawBbox.Min.X, rawBbox.Max.Y, rawBbox.Max.Z)),
                                            tr.OfPoint(new XYZ(rawBbox.Max.X, rawBbox.Max.Y, rawBbox.Max.Z)),
                                        };
                                        targetBbox = new BoundingBoxXYZ
                                        {
                                            Min = new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)),
                                            Max = new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z))
                                        };
                                    }
                                    else if (linkElem.Location is LocationPoint lp)
                                    {
                                        XYZ pt = tr.OfPoint(lp.Point);
                                        targetBbox = new BoundingBoxXYZ
                                        {
                                            Min = new XYZ(pt.X - 1.5, pt.Y - 1.5, pt.Z),
                                            Max = new XYZ(pt.X + 1.5, pt.Y + 1.5, pt.Z + 7.0)
                                        };
                                    }
                                    else if (linkElem.Location is LocationCurve lc)
                                    {
                                        XYZ p0 = tr.OfPoint(lc.Curve.GetEndPoint(0));
                                        XYZ p1 = tr.OfPoint(lc.Curve.GetEndPoint(1));
                                        targetBbox = new BoundingBoxXYZ
                                        {
                                            Min = new XYZ(Math.Min(p0.X, p1.X) - 0.5, Math.Min(p0.Y, p1.Y) - 0.5, Math.Min(p0.Z, p1.Z)),
                                            Max = new XYZ(Math.Max(p0.X, p1.X) + 0.5, Math.Max(p0.Y, p1.Y) + 0.5, Math.Max(p0.Z, p1.Z) + 8.0)
                                        };
                                    }
                                }
                            }

                            // 1. Bytt alltid till lämplig PLANVY (Floor Plan) istället för 3D-vy
                            ViewPlan? targetPlan = engine.FindBestFloorPlanView(hostElem, targetBbox);
                            if (targetPlan != null)
                            {
                                bool isAlreadyTargetPlan = uidoc.ActiveView is ViewPlan activePlan &&
                                                           activePlan.ViewType == ViewType.FloorPlan &&
                                                           (targetPlan.GenLevel == null || activePlan.GenLevel?.Id == targetPlan.GenLevel.Id);

                                if (!isAlreadyTargetPlan)
                                {
                                    uidoc.ActiveView = targetPlan;
                                }
                            }

                            // 2. Markera och zooma in till objektet i planvyn
                            if (hostElem != null)
                            {
                                uidoc.Selection.SetElementIds(new List<ElementId> { hostElem.Id });
                                UIView? uiview = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == uidoc.ActiveView.Id);
                                if (uiview != null && targetBbox != null)
                                {
                                    XYZ pad = new XYZ(3.0, 3.0, 1.0);
                                    uiview.ZoomAndCenterRectangle(targetBbox.Min - pad, targetBbox.Max + pad);
                                }
                                else
                                {
                                    uidoc.ShowElements(hostElem.Id);
                                }
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
                                    UIView? uiview = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == uidoc.ActiveView.Id);
                                    if (uiview != null && targetBbox != null)
                                    {
                                        XYZ pad = new XYZ(3.0, 3.0, 1.0);
                                        uiview.ZoomAndCenterRectangle(targetBbox.Min - pad, targetBbox.Max + pad);
                                    }
                                    else
                                    {
                                        uidoc.ShowElements(ghostDs.Id);
                                    }
                                }
                                else if (targetBbox != null)
                                {
                                    UIView? uiview = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == uidoc.ActiveView.Id);
                                    if (uiview != null)
                                    {
                                        XYZ pad = new XYZ(3.0, 3.0, 1.0);
                                        uiview.ZoomAndCenterRectangle(targetBbox.Min - pad, targetBbox.Max + pad);
                                    }
                                }
                            }
                        }
                        else if (TargetElementId != null && TargetElementId != ElementId.InvalidElementId)
                        {
                            Element? hostElem = doc.GetElement(TargetElementId);
                            BoundingBoxXYZ? bbox = hostElem?.get_BoundingBox(null);
                            ViewPlan? targetPlan = engine.FindBestFloorPlanView(hostElem, bbox);
                            if (targetPlan != null && uidoc.ActiveView.Id != targetPlan.Id)
                            {
                                uidoc.ActiveView = targetPlan;
                            }
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
