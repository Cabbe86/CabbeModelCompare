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

        public List<ComparisonItem> Compare(RevitLinkInstance linkInstance, List<BuiltInCategory> categories)
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
                    var diffs = CompareElements(hostElem, linkElem);
                    if (diffs.Count > 0)
                    {
                        var item = CreateItem(hostElem, linkElem, ComparisonStatus.Modified, $"{diffs.Count} ändring(ar)");
                        item.ParameterDiffs = diffs;
                        results.Add(item);
                    }
                    else
                    {
                        results.Add(CreateItem(hostElem, linkElem, ComparisonStatus.Unchanged, "Inga ändringar"));
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

        private List<ParameterDiff> CompareElements(Element hostElem, Element linkElem)
        {
            var diffs = new List<ParameterDiff>();

            // 1. Compare BoundingBox
            BoundingBoxXYZ? b1 = hostElem.get_BoundingBox(null);
            BoundingBoxXYZ? b2 = linkElem.get_BoundingBox(null);
            if (b1 != null && b2 != null)
            {
                double distMin = b1.Min.DistanceTo(b2.Min);
                double distMax = b1.Max.DistanceTo(b2.Max);
                if (distMin > 0.02 || distMax > 0.02) // ~6 mm tolerans
                {
                    diffs.Add(new ParameterDiff
                    {
                        ParameterName = "Geometri / Placering",
                        OldValue = $"Min: {FormatPt(b2.Min)}",
                        NewValue = $"Min: {FormatPt(b1.Min)}"
                    });
                }
            }

            // 2. Compare Type Name
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

            // 3. Compare key parameters
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

        private string FormatPt(XYZ p) => $"({p.X:F2}, {p.Y:F2}, {p.Z:F2})";

        public void ApplyACCGraphicOverrides(View view, List<ComparisonItem> items, RevitLinkInstance linkInstance)
        {
            if (view == null || items == null) return;

            ElementId? solidFillId = GetSolidFillPatternId();
            if (solidFillId == null) return;

            Color green = new Color(39, 174, 96);    // #27AE60 Tillagd
            Color orange = new Color(230, 126, 34);  // #E67E22 Ändrad

            var ogsAdded = CreateOgs(green, solidFillId, 0);
            var ogsModified = CreateOgs(orange, solidFillId, 0);
            var ogsUnchanged = new OverrideGraphicSettings();
            ogsUnchanged.SetHalftone(true);

            // 1. Override Host Elements in Active View
            foreach (var item in items)
            {
                if (item.HostId == null || item.HostId == ElementId.InvalidElementId) continue;

                if (item.Status == ComparisonStatus.Added)
                {
                    view.SetElementOverrides(item.HostId, ogsAdded);
                }
                else if (item.Status == ComparisonStatus.Modified)
                {
                    view.SetElementOverrides(item.HostId, ogsModified);
                }
                else if (item.Status == ComparisonStatus.Unchanged)
                {
                    view.SetElementOverrides(item.HostId, ogsUnchanged);
                }
            }

            // 2. Generate Ghost DirectShapes for Removed Elements (Red #E74C3C)
            CreateGhostDirectShapes(view, items.Where(i => i.Status == ComparisonStatus.Removed), linkInstance, solidFillId);
        }

        private OverrideGraphicSettings CreateOgs(Color color, ElementId fillId, int transparency)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternId(fillId);
            ogs.SetSurfaceForegroundPatternColor(color);
            ogs.SetCutForegroundPatternId(fillId);
            ogs.SetCutForegroundPatternColor(color);
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

            if (existing != null) return existing;

            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null) return null;

            Element? linkElem = linkDoc.GetElement(item.LinkId);
            if (linkElem == null) return null;

            fillId ??= GetSolidFillPatternId();
            if (fillId == null) return null;

            Transform totalTransform = linkInstance.GetTotalTransform();
            Color red = new Color(231, 76, 60); // #E74C3C
            var ogsRemoved = CreateOgs(red, fillId, 40); // 40% transparency for ghost effect

            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };

            try
            {
                GeometryElement geom = linkElem.get_Geometry(opt);
                if (geom == null) return null;

                List<GeometryObject> solids = new List<GeometryObject>();
                ExtractSolids(geom, totalTransform, solids);

                if (solids.Count > 0)
                {
                    DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    ds.ApplicationId = "CabbeModelCompare";
                    ds.ApplicationDataId = item.UniqueId;
                    ds.Name = dsName;
                    ds.SetShape(solids);

                    view.SetElementOverrides(ds.Id, ogsRemoved);
                    return ds;
                }
            }
            catch
            {
                // Ignorera enskilda geometrier som inte kan representeras som DirectShape
            }

            return null;
        }

        private void CreateGhostDirectShapes(View view, IEnumerable<ComparisonItem> removedItems, RevitLinkInstance linkInstance, ElementId fillId)
        {
            foreach (var item in removedItems)
            {
                GetOrCreateGhostDirectShape(view, item, linkInstance, fillId);
            }
        }

        private void ExtractSolids(GeometryElement geomElem, Transform transform, List<GeometryObject> result)
        {
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Volume > 0.0001)
                {
                    Solid transformedSolid = SolidUtils.CreateTransformed(solid, transform);
                    result.Add(transformedSolid);
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
        }

        public void ResetViewAndClearVisuals(View view)
        {
            if (view == null) return;

            // 1. Delete all comparison DirectShapes
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

            // 2. Clear element overrides in current view
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

        private ElementId? GetSolidFillPatternId()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill)?.Id;
        }
    }
}
