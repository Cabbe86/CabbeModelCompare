using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Cabbe.Revit.Compare.Models;

namespace Cabbe.Revit.Compare.Services
{
    public class CompareEngine
    {
        private readonly Document _doc;

        public CompareEngine(Document doc)
        {
            _doc = doc;
        }

        public List<RevitLinkInstance> GetRevitLinks()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null)
                .ToList();
        }

        public static List<CategoryFilterItem> GetDefaultCategories()
        {
            return new List<CategoryFilterItem>
            {
                new() { Name = "Väggar", BuiltInCategory = BuiltInCategory.OST_Walls, IsSelected = true },
                new() { Name = "Dörrar", BuiltInCategory = BuiltInCategory.OST_Doors, IsSelected = true },
                new() { Name = "Fönster", BuiltInCategory = BuiltInCategory.OST_Windows, IsSelected = true },
                new() { Name = "Pelare & Pelare (Bärande)", BuiltInCategory = BuiltInCategory.OST_Columns, IsSelected = true },
                new() { Name = "Bärande stomme", BuiltInCategory = BuiltInCategory.OST_StructuralFraming, IsSelected = true },
                new() { Name = "Bjälklag / Golv", BuiltInCategory = BuiltInCategory.OST_Floors, IsSelected = false },
                new() { Name = "Innertak", BuiltInCategory = BuiltInCategory.OST_Ceilings, IsSelected = false },
                new() { Name = "Yttertak", BuiltInCategory = BuiltInCategory.OST_Roofs, IsSelected = false },
                new() { Name = "Generiska modeller", BuiltInCategory = BuiltInCategory.OST_GenericModel, IsSelected = true },
                new() { Name = "Trappor & Räcken", BuiltInCategory = BuiltInCategory.OST_Stairs, IsSelected = true },
                new() { Name = "Glaspartier & Spröjs", BuiltInCategory = BuiltInCategory.OST_CurtainWallPanels, IsSelected = true }
            };
        }

        public List<ComparisonItem> Compare(RevitLinkInstance linkInstance, List<BuiltInCategory> categories, bool onlyGeometry = true)
        {
            var results = new List<ComparisonItem>();
            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null) return results;

            if (categories == null || categories.Count == 0) return results;

            ElementMulticategoryFilter multiCatFilter = new ElementMulticategoryFilter(categories);

            // Host elements
            var hostElements = new Dictionary<string, Element>();
            var hostCollector = new FilteredElementCollector(_doc)
                .WherePasses(multiCatFilter)
                .WhereElementIsNotElementType()
                .Where(e => !e.ViewSpecific && !e.Name.StartsWith("CabbeCompare_"));
            foreach (var e in hostCollector)
            {
                hostElements[e.UniqueId] = e;
            }

            // Link elements
            var linkElements = new Dictionary<string, Element>();
            var linkCollector = new FilteredElementCollector(linkDoc)
                .WherePasses(multiCatFilter)
                .WhereElementIsNotElementType()
                .Where(e => !e.ViewSpecific);
            foreach (var e in linkCollector)
            {
                linkElements[e.UniqueId] = e;
            }

            // 1. Check Host elements (Added or Modified or Unchanged)
            foreach (var kvp in hostElements)
            {
                string uId = kvp.Key;
                Element hostElem = kvp.Value;

                if (!linkElements.TryGetValue(uId, out Element? linkElem))
                {
                    // Added
                    var item = CreateItem(hostElem, null, ComparisonStatus.Added, "Nytt objekt i aktiv modell");
                    item.ParameterDiffs = GetElementParametersForDisplay(hostElem, isOld: false);
                    results.Add(item);
                }
                else
                {
                    // Exists in both -> check if modified
                    var diffs = CompareElements(hostElem, linkElem, onlyGeometry);
                    if (diffs.Count > 0)
                    {
                        string summary = onlyGeometry ? $"{diffs.Count} geometriändring(ar)" : $"{diffs.Count} ändring(ar)";
                        var item = CreateItem(hostElem, linkElem, ComparisonStatus.Modified, summary);
                        item.ParameterDiffs = diffs;
                        results.Add(item);
                    }
                    else
                    {
                        results.Add(CreateItem(hostElem, linkElem, ComparisonStatus.Unchanged, "Inga geometriska ändringar"));
                    }
                }
            }

            // 2. Check Link elements (Removed)
            foreach (var kvp in linkElements)
            {
                string uId = kvp.Key;
                Element linkElem = kvp.Value;

                if (!hostElements.ContainsKey(uId))
                {
                    var item = CreateItem(null, linkElem, ComparisonStatus.Removed, "Borttaget ur aktiv modell");
                    item.ParameterDiffs = GetElementParametersForDisplay(linkElem, isOld: true);
                    results.Add(item);
                }
            }

            return results;
        }

        private ComparisonItem CreateItem(Element? hostElem, Element? linkElem, ComparisonStatus status, string summary)
        {
            Element refElem = hostElem ?? linkElem!;
            string catName = refElem.Category?.Name ?? "Okänd";
            string typeName = string.Empty;
            string familyName = string.Empty;

            try
            {
                ElementId typeId = refElem.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    Document docRef = hostElem != null ? _doc : linkElem!.Document;
                    if (docRef.GetElement(typeId) is ElementType et)
                    {
                        typeName = et.Name;
                        familyName = et.FamilyName;
                    }
                }
                if (string.IsNullOrEmpty(familyName))
                {
                    familyName = refElem.Name;
                }
            }
            catch
            {
                familyName = refElem.Name;
            }

            return new ComparisonItem
            {
                HostId = hostElem?.Id,
                LinkId = linkElem?.Id,
                UniqueId = refElem.UniqueId,
                Status = status,
                CategoryName = catName,
                FamilyName = familyName,
                TypeName = typeName,
                ChangeSummary = summary
            };
        }

        private List<ParameterDiff> GetElementParametersForDisplay(Element elem, bool isOld)
        {
            var list = new List<ParameterDiff>();
            foreach (Parameter p in elem.Parameters)
            {
                if (p?.Definition == null || string.IsNullOrEmpty(p.Definition.Name)) continue;
                string pName = p.Definition.Name;
                if (pName.StartsWith("Internal") || pName.Contains("GUID")) continue;

                string val = p.AsValueString() ?? p.AsString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(val)) continue;

                list.Add(new ParameterDiff
                {
                    ParameterName = pName,
                    OldValue = isOld ? val : "(Ny i aktiv modell)",
                    NewValue = isOld ? "(Borttagen ur aktiv modell)" : val
                });
            }
            return list.OrderBy(x => x.ParameterName).ToList();
        }

        private List<ParameterDiff> CompareElements(Element hostElem, Element linkElem, bool onlyGeometry)
        {
            var diffs = new List<ParameterDiff>();
            bool geometryDiff = false;

            // 1. Compare BoundingBox (Placement and Dimensions)
            BoundingBoxXYZ? b1 = hostElem.get_BoundingBox(null);
            BoundingBoxXYZ? b2 = linkElem.get_BoundingBox(null);
            const double tol = 0.016; // ~5 mm i Revit-interna fötter

            if (b1 != null && b2 != null)
            {
                // Dimensions in host coordinates
                double hostDx = Math.Abs(b1.Max.X - b1.Min.X);
                double hostDy = Math.Abs(b1.Max.Y - b1.Min.Y);
                double hostDz = Math.Abs(b1.Max.Z - b1.Min.Z);

                double linkDx = Math.Abs(b2.Max.X - b2.Min.X);
                double linkDy = Math.Abs(b2.Max.Y - b2.Min.Y);
                double linkDz = Math.Abs(b2.Max.Z - b2.Min.Z);

                double diffDx = Math.Abs(hostDx - linkDx);
                double diffDy = Math.Abs(hostDy - linkDy);
                double diffDz = Math.Abs(hostDz - linkDz);

                // Center points
                XYZ c1 = (b1.Min + b1.Max) * 0.5;
                XYZ c2 = (b2.Min + b2.Max) * 0.5;
                double distCenter = c1.DistanceTo(c2);

                if (distCenter > tol)
                {
                    geometryDiff = true;
                    double distMm = distCenter * 304.8;
                    diffs.Add(new ParameterDiff
                    {
                        ParameterName = "Placering (Flyttad)",
                        OldValue = FormatPtMm(c2),
                        NewValue = $"{FormatPtMm(c1)} (Flyttad {distMm:F0} mm)"
                    });
                }

                if (diffDx > tol || diffDy > tol || diffDz > tol)
                {
                    geometryDiff = true;
                    diffs.Add(new ParameterDiff
                    {
                        ParameterName = "Dimensioner (BxDxH)",
                        OldValue = FormatDimMm(linkDx, linkDy, linkDz),
                        NewValue = FormatDimMm(hostDx, hostDy, hostDz)
                    });
                }
            }

            // 2. Compare LocationPoint / LocationCurve
            if (hostElem.Location is LocationPoint lp1 && linkElem.Location is LocationPoint lp2)
            {
                double pDist = lp1.Point.DistanceTo(lp2.Point);
                if (pDist > tol && !geometryDiff)
                {
                    geometryDiff = true;
                    diffs.Add(new ParameterDiff
                    {
                        ParameterName = "Infästningspunkt",
                        OldValue = FormatPtMm(lp2.Point),
                        NewValue = $"{FormatPtMm(lp1.Point)} (Flyttad {pDist * 304.8:F0} mm)"
                    });
                }

                double rotDiff = Math.Abs(lp1.Rotation - lp2.Rotation);
                if (rotDiff > 0.01) // > ~0.5 grader
                {
                    geometryDiff = true;
                    diffs.Add(new ParameterDiff
                    {
                        ParameterName = "Rotation",
                        OldValue = $"{lp2.Rotation * (180.0 / Math.PI):F1}°",
                        NewValue = $"{lp1.Rotation * (180.0 / Math.PI):F1}°"
                    });
                }
            }
            else if (hostElem.Location is LocationCurve lc1 && linkElem.Location is LocationCurve lc2)
            {
                double len1 = lc1.Curve.Length;
                double len2 = lc2.Curve.Length;
                if (Math.Abs(len1 - len2) > tol)
                {
                    geometryDiff = true;
                    diffs.Add(new ParameterDiff
                    {
                        ParameterName = "Längd",
                        OldValue = $"{len2 * 304.8:F0} mm",
                        NewValue = $"{len1 * 304.8:F0} mm"
                    });
                }
            }

            // 3. Compare Solids Volume if not yet confirmed
            if (!geometryDiff)
            {
                double v1 = GetTotalSolidsVolume(hostElem);
                double v2 = GetTotalSolidsVolume(linkElem);
                if (v1 > 0.001 && v2 > 0.001 && Math.Abs(v1 - v2) > 0.05) // ~1.4 liter volymskillnad
                {
                    geometryDiff = true;
                    diffs.Add(new ParameterDiff
                    {
                        ParameterName = "Volym",
                        OldValue = $"{v2 * 0.0283168:F2} m³",
                        NewValue = $"{v1 * 0.0283168:F2} m³"
                    });
                }
            }

            // Om användaren endast vill ha geometriska skillnader: returnera direkt
            if (onlyGeometry)
            {
                return diffs;
            }

            // Vid full parameterkontroll: jämför även typnamn och nyckelparametrar
            string hostType = GetTypeName(hostElem);
            string linkType = GetTypeName(linkElem);
            if (hostType != linkType)
            {
                diffs.Add(new ParameterDiff
                {
                    ParameterName = "Typnamn",
                    OldValue = linkType,
                    NewValue = hostType
                });
            }

            var hostParams = new Dictionary<string, Parameter>(StringComparer.OrdinalIgnoreCase);
            foreach (Parameter p in hostElem.Parameters)
            {
                if (p?.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                {
                    hostParams[p.Definition.Name] = p;
                }
            }

            foreach (Parameter lp in linkElem.Parameters)
            {
                if (lp?.Definition == null || string.IsNullOrEmpty(lp.Definition.Name)) continue;
                string pName = lp.Definition.Name;
                if (pName.StartsWith("Internal") || pName.Contains("GUID")) continue;

                if (hostParams.TryGetValue(pName, out Parameter? hp))
                {
                    string oldVal = lp.AsValueString() ?? lp.AsString() ?? string.Empty;
                    string newVal = hp.AsValueString() ?? hp.AsString() ?? string.Empty;

                    if (!string.Equals(oldVal, newVal, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(oldVal) && string.IsNullOrWhiteSpace(newVal)) continue;

                        diffs.Add(new ParameterDiff
                        {
                            ParameterName = pName,
                            OldValue = string.IsNullOrWhiteSpace(oldVal) ? "(tom)" : oldVal,
                            NewValue = string.IsNullOrWhiteSpace(newVal) ? "(tom)" : newVal
                        });
                    }
                }
            }

            return diffs;
        }

        private double GetTotalSolidsVolume(Element elem)
        {
            double totalVol = 0;
            try
            {
                Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
                GeometryElement geom = elem.get_Geometry(opt);
                if (geom != null)
                {
                    totalVol = AccumulateSolidsVolume(geom);
                }
            }
            catch { }
            return totalVol;
        }

        private double AccumulateSolidsVolume(GeometryElement geomElem)
        {
            double vol = 0;
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Volume > 0.0001)
                {
                    vol += solid.Volume;
                }
                else if (obj is GeometryInstance gi)
                {
                    GeometryElement instGeom = gi.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        vol += AccumulateSolidsVolume(instGeom);
                    }
                }
            }
            return vol;
        }

        private string GetTypeName(Element elem)
        {
            try
            {
                ElementId tid = elem.GetTypeId();
                if (tid != null && tid != ElementId.InvalidElementId)
                {
                    if (elem.Document.GetElement(tid) is ElementType et)
                        return et.Name;
                }
            }
            catch { }
            return string.Empty;
        }

        private string FormatPtMm(XYZ p) => $"({p.X * 304.8:F0}, {p.Y * 304.8:F0}, {p.Z * 304.8:F0}) mm";
        private string FormatDimMm(double dx, double dy, double dz) => $"{dx * 304.8:F0} × {dy * 304.8:F0} × {dz * 304.8:F0} mm";

        public ViewPlan? FindBestFloorPlanView(Element? elem, BoundingBoxXYZ? bbox)
        {
            var planViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.ViewType == ViewType.FloorPlan && !v.IsTemplate)
                .ToList();

            if (planViews.Count == 0) return null;

            Level? targetLevel = null;
            if (elem != null)
            {
                ElementId levelId = elem.LevelId;
                if (levelId != null && levelId != ElementId.InvalidElementId)
                {
                    targetLevel = _doc.GetElement(levelId) as Level;
                }

                if (targetLevel == null)
                {
                    BuiltInParameter[] levelParams = new[]
                    {
                        BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                        BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
                        BuiltInParameter.WALL_BASE_CONSTRAINT,
                        BuiltInParameter.FAMILY_LEVEL_PARAM,
                        BuiltInParameter.LEVEL_PARAM,
                        BuiltInParameter.ROOF_BASE_LEVEL_PARAM
                    };

                    foreach (var bip in levelParams)
                    {
                        Parameter? p = elem.get_Parameter(bip);
                        if (p != null && p.StorageType == StorageType.ElementId)
                        {
                            ElementId id = p.AsElementId();
                            if (id != null && id != ElementId.InvalidElementId && _doc.GetElement(id) is Level lvl)
                            {
                                targetLevel = lvl;
                                break;
                            }
                        }
                    }
                }
            }

            if (targetLevel == null && bbox != null)
            {
                double targetZ = (bbox.Min.Z + bbox.Max.Z) * 0.5;
                var allLevels = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                Level? bestLvl = null;
                double minDiff = double.MaxValue;
                foreach (var lvl in allLevels)
                {
                    double diff = Math.Abs(lvl.Elevation - targetZ);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        bestLvl = lvl;
                    }
                }
                targetLevel = bestLvl;
            }

            if (targetLevel != null)
            {
                var matchPlan = planViews.FirstOrDefault(v => v.GenLevel != null && v.GenLevel.Id == targetLevel.Id);
                if (matchPlan != null) return matchPlan;

                var bestPlan = planViews
                    .Where(v => v.GenLevel != null)
                    .OrderBy(v => Math.Abs(v.GenLevel.Elevation - targetLevel.Elevation))
                    .FirstOrDefault();
                if (bestPlan != null) return bestPlan;
            }

            return planViews.FirstOrDefault();
        }

        public void ApplyACCGraphicOverrides(View view, List<ComparisonItem> items, RevitLinkInstance linkInstance, ViewColorMode colorMode, bool halftoneUnchanged)
        {
            if (view == null || items == null) return;

            ElementId? solidFillId = GetSolidFillPatternId();

            Color green = new Color(39, 174, 96);    // #27AE60 Tillagd
            Color orange = new Color(230, 126, 34);  // #E67E22 Ändrad

            var ogsAdded = CreateOgs(green, solidFillId, 0);
            var ogsModified = CreateOgs(orange, solidFillId, 0);
            var ogsHalftone = new OverrideGraphicSettings();
            ogsHalftone.SetHalftone(true);
            var ogsDefault = new OverrideGraphicSettings();

            // 1. Override Host Elements in Active View
            foreach (var item in items)
            {
                if (item.HostId == null || item.HostId == ElementId.InvalidElementId) continue;

                try
                {
                    switch (item.Status)
                    {
                        case ComparisonStatus.Added:
                            if (colorMode == ViewColorMode.All || colorMode == ViewColorMode.AddedOnly)
                                view.SetElementOverrides(item.HostId, ogsAdded);
                            else
                                view.SetElementOverrides(item.HostId, halftoneUnchanged ? ogsHalftone : ogsDefault);
                            break;

                        case ComparisonStatus.Modified:
                            if (colorMode == ViewColorMode.All || colorMode == ViewColorMode.ModifiedOnly)
                                view.SetElementOverrides(item.HostId, ogsModified);
                            else
                                view.SetElementOverrides(item.HostId, halftoneUnchanged ? ogsHalftone : ogsDefault);
                            break;

                        case ComparisonStatus.Unchanged:
                            view.SetElementOverrides(item.HostId, halftoneUnchanged ? ogsHalftone : ogsDefault);
                            break;
                    }
                }
                catch { }
            }

            // 2. Removed Elements (Ghost DirectShapes)
            if (colorMode == ViewColorMode.All || colorMode == ViewColorMode.RemovedOnly)
            {
                // Säkerställ att Generic Models syns i vyn
                try
                {
                    var catId = new ElementId(BuiltInCategory.OST_GenericModel);
                    if (view.GetCategoryHidden(catId))
                    {
                        view.SetCategoryHidden(catId, false);
                    }
                }
                catch { }

                CreateGhostDirectShapes(view, items.Where(i => i.Status == ComparisonStatus.Removed), linkInstance, solidFillId);
            }
            else
            {
                ClearGhostDirectShapes();
            }
        }

        private OverrideGraphicSettings CreateOgs(Color color, ElementId? fillId, int transparency)
        {
            var ogs = new OverrideGraphicSettings();
            if (fillId != null && fillId != ElementId.InvalidElementId)
            {
                try
                {
                    ogs.SetSurfaceForegroundPatternId(fillId);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    ogs.SetCutForegroundPatternId(fillId);
                    ogs.SetCutForegroundPatternColor(color);
                }
                catch { }
            }
            ogs.SetProjectionLineColor(color);
            ogs.SetCutLineColor(color);
            if (transparency > 0)
            {
                ogs.SetSurfaceTransparency(transparency);
            }
            return ogs;
        }

        public DirectShape? GetOrCreateGhostDirectShape(View view, ComparisonItem item, RevitLinkInstance linkInstance, ElementId? fillId = null)
        {
            if (item.LinkId == null || string.IsNullOrEmpty(item.UniqueId)) return null;

            string dsName = "CabbeCompare_Deleted_" + item.UniqueId;
            var existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .FirstOrDefault(ds => ds.Name == dsName);

            fillId ??= GetSolidFillPatternId();
            Color redColor = new Color(231, 76, 60); // #E74C3C
            var ogsRemoved = CreateOgs(redColor, fillId, 40); // 40% transparency for ghost effect

            if (existing != null)
            {
                try { view.SetElementOverrides(existing.Id, ogsRemoved); } catch { }
                return existing;
            }

            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null) return null;

            Element? linkElem = linkDoc.GetElement(item.LinkId);
            if (linkElem == null) return null;

            Transform totalTransform = linkInstance.GetTotalTransform();

            // Förbered bounding box för transformation
            BoundingBoxXYZ? rawBbox = null;
            try { rawBbox = linkElem.get_BoundingBox(null); } catch { }

            XYZ? boxMin = null;
            XYZ? boxMax = null;
            if (rawBbox != null)
            {
                XYZ pMin = totalTransform.OfPoint(rawBbox.Min);
                XYZ pMax = totalTransform.OfPoint(rawBbox.Max);
                boxMin = new XYZ(Math.Min(pMin.X, pMax.X), Math.Min(pMin.Y, pMax.Y), Math.Min(pMin.Z, pMax.Z));
                boxMax = new XYZ(Math.Max(pMin.X, pMax.X), Math.Max(pMin.Y, pMax.Y), Math.Max(pMin.Z, pMax.Z));
            }

            // 1. Försök extrahera detaljerade solids från länkobjektet
            List<GeometryObject> solids = new List<GeometryObject>();
            try
            {
                Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
                GeometryElement geom = linkElem.get_Geometry(opt);
                if (geom != null)
                {
                    ExtractSolids(geom, totalTransform, solids);
                }
            }
            catch { }

            // 2. Fallback: Om inga solids hittades, skapa en 3D-box från elementets BoundingBox
            if (solids.Count == 0 && boxMin != null && boxMax != null)
            {
                Solid? fallbackBox = CreateBoxSolid(boxMin, boxMax);
                if (fallbackBox != null)
                {
                    solids.Add(fallbackBox);
                }
            }

            if (solids.Count == 0) return null;

            // 3. Skapa DirectShape som GenericModel i värddokumentet
            try
            {
                DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                ds.ApplicationId = "CabbeModelCompare";
                ds.ApplicationDataId = item.UniqueId;
                ds.Name = dsName;

                bool shapeSet = false;
                try
                {
                    ds.SetShape(solids);
                    shapeSet = true;
                }
                catch
                {
                    // Om detaljerad solid misslyckas i Revit (t.ex. icke-manifolderad geometri), använd 3D-box
                    if (boxMin != null && boxMax != null)
                    {
                        Solid? fallbackBox = CreateBoxSolid(boxMin, boxMax);
                        if (fallbackBox != null)
                        {
                            try
                            {
                                ds.SetShape(new List<GeometryObject> { fallbackBox });
                                shapeSet = true;
                            }
                            catch { }
                        }
                    }
                }

                if (!shapeSet)
                {
                    // Kunde inte sätta geometrin
                    try { _doc.Delete(ds.Id); } catch { }
                    return null;
                }

                // Sätt rött spökmaterial på elementet så det syns i 3D och planvy
                try
                {
                    ElementId redMatId = GetOrCreateRedGhostMaterial();
                    if (redMatId != ElementId.InvalidElementId)
                    {
                        var matParam = ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        matParam?.Set(redMatId);
                    }
                }
                catch { }

                // Se till att kategorin Generic Model är synlig i aktuell vy
                try
                {
                    var catId = new ElementId(BuiltInCategory.OST_GenericModel);
                    if (view.GetCategoryHidden(catId))
                    {
                        view.SetCategoryHidden(catId, false);
                    }
                }
                catch { }

                // Sätt vy-overrides (röd färg och transparens)
                try
                {
                    view.SetElementOverrides(ds.Id, ogsRemoved);
                }
                catch { }

                return ds;
            }
            catch
            {
                return null;
            }
        }

        private Solid? CreateBoxSolid(XYZ min, XYZ max)
        {
            try
            {
                double dx = max.X - min.X;
                double dy = max.Y - min.Y;
                double dz = max.Z - min.Z;

                // Minsta dimensioner (minst ~60 mm) så Revit inte nekar för små profiler
                double minDim = 0.2;
                if (dx < minDim) { double mid = (min.X + max.X) / 2.0; min = new XYZ(mid - minDim / 2.0, min.Y, min.Z); max = new XYZ(mid + minDim / 2.0, max.Y, max.Z); }
                if (dy < minDim) { double mid = (min.Y + max.Y) / 2.0; min = new XYZ(min.X, mid - minDim / 2.0, min.Z); max = new XYZ(max.X, mid + minDim / 2.0, max.Z); }
                if (dz < minDim) { double mid = (min.Z + max.Z) / 2.0; min = new XYZ(min.X, min.Y, mid - minDim / 2.0); max = new XYZ(max.X, max.Y, mid + minDim / 2.0); }

                XYZ p0 = new XYZ(min.X, min.Y, min.Z);
                XYZ p1 = new XYZ(max.X, min.Y, min.Z);
                XYZ p2 = new XYZ(max.X, max.Y, min.Z);
                XYZ p3 = new XYZ(min.X, max.Y, min.Z);

                CurveLoop loop = new CurveLoop();
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p0));

                double height = max.Z - min.Z;
                return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, height);
            }
            catch
            {
                return null;
            }
        }

        private ElementId GetOrCreateRedGhostMaterial()
        {
            string matName = "CabbeCompare_RedGhost";
            try
            {
                var existing = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name == matName);

                if (existing != null) return existing.Id;

                ElementId newId = Material.Create(_doc, matName);
                if (newId != ElementId.InvalidElementId)
                {
                    Material mat = (Material)_doc.GetElement(newId);
                    mat.Color = new Color(231, 76, 60); // #E74C3C Röd
                    mat.Transparency = 40; // 40% transparent
                    return newId;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        private void CreateGhostDirectShapes(View view, IEnumerable<ComparisonItem> removedItems, RevitLinkInstance linkInstance, ElementId? fillId)
        {
            foreach (var item in removedItems)
            {
                GetOrCreateGhostDirectShape(view, item, linkInstance, fillId);
            }
        }

        public void ClearGhostDirectShapes()
        {
            try
            {
                var directShapes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(DirectShape))
                    .Cast<DirectShape>()
                    .Where(ds => ds.Name.StartsWith("CabbeCompare_Deleted_"))
                    .Select(ds => ds.Id)
                    .ToList();

                if (directShapes.Count > 0)
                {
                    _doc.Delete(directShapes);
                }
            }
            catch { }
        }

        public void ResetViewAndClearVisuals(View view)
        {
            if (view == null) return;

            // 1. Delete all comparison DirectShapes
            ClearGhostDirectShapes();

            // 2. Clear element overrides in current view
            try
            {
                var elementsInView = new FilteredElementCollector(_doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Select(e => e.Id)
                    .ToList();

                var defaultOgs = new OverrideGraphicSettings();
                foreach (var id in elementsInView)
                {
                    try
                    {
                        view.SetElementOverrides(id, defaultOgs);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ExtractSolids(GeometryElement geomElem, Transform transform, List<GeometryObject> result)
        {
            foreach (GeometryObject obj in geomElem)
            {
                try
                {
                    if (obj is Solid solid && solid.Volume > 0.0001 && solid.Faces.Size > 0)
                    {
                        if (transform.IsIdentity)
                        {
                            result.Add(solid);
                        }
                        else
                        {
                            try
                            {
                                Solid transformedSolid = SolidUtils.CreateTransformed(solid, transform);
                                result.Add(transformedSolid);
                            }
                            catch
                            {
                                result.Add(solid);
                            }
                        }
                    }
                    else if (obj is GeometryInstance gi)
                    {
                        GeometryElement instGeom = gi.GetInstanceGeometry();
                        if (instGeom != null)
                        {
                            ExtractSolids(instGeom, transform, result);
                        }
                    }
                }
                catch { }
            }
        }

        private ElementId? GetSolidFillPatternId()
        {
            try
            {
                var patterns = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>();

                foreach (var fp in patterns)
                {
                    try
                    {
                        var pat = fp.GetFillPattern();
                        if (pat != null && pat.IsSolidFill)
                        {
                            return fp.Id;
                        }
                    }
                    catch { }
                }

                // Sök på namn om IsSolidFill inte matchade
                foreach (var fp in patterns)
                {
                    try
                    {
                        if (fp.Name.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fp.Name.IndexOf("Enfärgad", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return fp.Id;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }
}
