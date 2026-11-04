using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Cabbe.Revit.Compare.Models
{
    public enum ComparisonStatus
    {
        Added,
        Modified,
        Removed,
        Unchanged
    }

    public enum ViewColorMode
    {
        All,          // Alla ändringar (Grön, Orange, Röd)
        AddedOnly,    // Endast Tillagda (Grön)
        ModifiedOnly, // Endast Ändrade (Orange)
        RemovedOnly   // Endast Borttagna (Röd)
    }

    public class ParameterDiff
    {
        public string ParameterName { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }

    public class ComparisonItem
    {
        public ElementId? HostId { get; set; }
        public ElementId? LinkId { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public ComparisonStatus Status { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(TypeName) ? FamilyName : $"{FamilyName}: {TypeName}";
        public string ChangeSummary { get; set; } = string.Empty;
        public List<ParameterDiff> ParameterDiffs { get; set; } = new();

        public string StatusLabel => Status switch
        {
            ComparisonStatus.Added => "Tillagd",
            ComparisonStatus.Modified => "Ändrad",
            ComparisonStatus.Removed => "Borttagen",
            _ => "Oförändrad"
        };

        public string StatusBadgeColor => Status switch
        {
            ComparisonStatus.Added => "#27AE60",       // Green
            ComparisonStatus.Modified => "#E67E22",    // Orange
            ComparisonStatus.Removed => "#E74C3C",     // Red
            _ => "#7F8C8D"                             // Gray
        };

        public bool CanSelectInActiveDoc => HostId != null && HostId != ElementId.InvalidElementId;
    }

    public class CategoryFilterItem
    {
        public string Name { get; set; } = string.Empty;
        public BuiltInCategory BuiltInCategory { get; set; }
        public bool IsSelected { get; set; } = true;
        public int ElementCount { get; set; }
    }

    public class ComparisonSummary
    {
        public int AddedCount { get; set; }
        public int ModifiedCount { get; set; }
        public int RemovedCount { get; set; }
        public int UnchangedCount { get; set; }
        public int TotalCount => AddedCount + ModifiedCount + RemovedCount;
    }
}
