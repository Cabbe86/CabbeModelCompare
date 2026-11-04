using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Cabbe.Revit.Compare.Models;
using Cabbe.Revit.Compare.Services;

namespace Cabbe.Revit.Compare.UI
{
    public partial class CompareWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly CompareExternalEventHandler _eventHandler;
        private readonly ExternalEvent _externalEvent;

        private List<RevitLinkInstance> _linkInstances = new();
        private List<ComparisonItem> _allItems = new();
        private ObservableCollection<CategoryFilterItem> _categories = new();
        private ComparisonStatus? _activeStatusFilter = null;

        public CompareWindow(UIDocument uidoc, CompareExternalEventHandler handler, ExternalEvent extEvent)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _eventHandler = handler;
            _externalEvent = extEvent;

            _eventHandler.OnCompareCompleted = HandleCompareCompleted;
            _eventHandler.OnStatusMessage = msg => Dispatcher.Invoke(() => TxtStatus.Text = msg);

            LoadLinks();
            LoadCategories();
        }

        private void LoadLinks()
        {
            var engine = new CompareEngine(_doc);
            _linkInstances = engine.GetRevitLinks();

            CmbLinks.Items.Clear();
            foreach (var link in _linkInstances)
            {
                Document linkDoc = link.GetLinkDocument();
                string title = linkDoc != null ? linkDoc.Title : link.Name;
                CmbLinks.Items.Add(new ComboBoxItem { Content = title, Tag = link });
            }

            if (CmbLinks.Items.Count > 0)
            {
                CmbLinks.SelectedIndex = 0;
            }
            else
            {
                TxtStatus.Text = "Inga Revit-länkar hittades i den aktiva modellen. Länka in en RVT-modell först.";
            }
        }

        private void LoadCategories()
        {
            var defaultCats = CompareEngine.GetDefaultCategories();
            _categories = new ObservableCollection<CategoryFilterItem>(defaultCats);
            ItemsCategories.ItemsSource = _categories;
        }

        private RevitLinkInstance? GetSelectedLink()
        {
            if (CmbLinks.SelectedItem is ComboBoxItem cbi && cbi.Tag is RevitLinkInstance link)
            {
                return link;
            }
            return null;
        }

        private void BtnRefreshLinks_Click(object sender, RoutedEventArgs e)
        {
            LoadLinks();
            TxtStatus.Text = "Länklistan har uppdaterats.";
        }

        private void BtnRunCompare_Click(object sender, RoutedEventArgs e)
        {
            var link = GetSelectedLink();
            if (link == null)
            {
                MessageBox.Show("Välj en inlänkad RVT-modell att jämföra mot.", "Ingen länk vald", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedCats = _categories.Where(c => c.IsSelected).Select(c => c.BuiltInCategory).ToList();
            if (selectedCats.Count == 0)
            {
                MessageBox.Show("Välj minst en kategori att jämföra i vänsterpanelen.", "Ingen kategori vald", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtStatus.Text = "Jämför modeller... Vänligen vänta.";
            _eventHandler.PendingAction = CompareAction.RunCompare;
            _eventHandler.SelectedLink = link;
            _eventHandler.SelectedCategories = selectedCats;
            _externalEvent.Raise();
        }

        private void HandleCompareCompleted(List<ComparisonItem> items)
        {
            Dispatcher.Invoke(() =>
            {
                _allItems = items;

                int added = items.Count(i => i.Status == ComparisonStatus.Added);
                int modified = items.Count(i => i.Status == ComparisonStatus.Modified);
                int removed = items.Count(i => i.Status == ComparisonStatus.Removed);
                int total = items.Count(i => i.Status != ComparisonStatus.Unchanged);

                TxtCountAdded.Text = $"+{added} Tillagda";
                TxtCountModified.Text = $"~{modified} Ändrade";
                TxtCountRemoved.Text = $"-{removed} Borttagna";
                TxtCountTotal.Text = $"{total} Ändrade totalt";

                ApplyFilter();
            });
        }

        private void ApplyFilter()
        {
            if (_allItems == null) return;

            string search = TxtSearch.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            var filtered = _allItems.Where(item =>
            {
                // Dölj oförändrade i listan som standard såvida inte användaren specifikt söker
                if (_activeStatusFilter == null && item.Status == ComparisonStatus.Unchanged)
                    return false;

                if (_activeStatusFilter.HasValue && item.Status != _activeStatusFilter.Value)
                    return false;

                if (!string.IsNullOrEmpty(search))
                {
                    bool matchName = item.DisplayName.ToLowerInvariant().Contains(search);
                    bool matchCat = item.CategoryName.ToLowerInvariant().Contains(search);
                    bool matchSummary = item.ChangeSummary.ToLowerInvariant().Contains(search);
                    bool matchId = (item.HostId?.ToString() ?? string.Empty).Contains(search);
                    return matchName || matchCat || matchSummary || matchId;
                }

                return true;
            }).ToList();

            GridItems.ItemsSource = filtered;
            TxtCurrentFilter.Text = _activeStatusFilter switch
            {
                ComparisonStatus.Added => "Visar: Endast Tillagda",
                ComparisonStatus.Modified => "Visar: Endast Ändrade",
                ComparisonStatus.Removed => "Visar: Endast Borttagna",
                _ => "Visar: Alla förändringar"
            };
        }

        private void BadgeAdded_Click(object sender, MouseButtonEventArgs e)
        {
            _activeStatusFilter = ComparisonStatus.Added;
            ApplyFilter();
        }

        private void BadgeModified_Click(object sender, MouseButtonEventArgs e)
        {
            _activeStatusFilter = ComparisonStatus.Modified;
            ApplyFilter();
        }

        private void BadgeRemoved_Click(object sender, MouseButtonEventArgs e)
        {
            _activeStatusFilter = ComparisonStatus.Removed;
            ApplyFilter();
        }

        private void BadgeAll_Click(object sender, MouseButtonEventArgs e)
        {
            _activeStatusFilter = null;
            ApplyFilter();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnApplyColors_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems == null || _allItems.Count == 0)
            {
                MessageBox.Show("Kör en jämförelse först innan du applicerar färger i vyn.", "Ingen data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var link = GetSelectedLink();
            if (link == null) return;

            TxtStatus.Text = "Applicerar ACC-färgkodning och spökobjekt...";
            _eventHandler.PendingAction = CompareAction.ApplyColors;
            _eventHandler.ComparisonItems = _allItems;
            _eventHandler.SelectedLink = link;
            _externalEvent.Raise();
        }

        private void BtnResetView_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Återställer vy och raderar temporära spökobjekt...";
            _eventHandler.PendingAction = CompareAction.ResetView;
            _externalEvent.Raise();
        }

        private void GridItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridItems.SelectedItem is ComparisonItem item)
            {
                TxtSelectedItemName.Text = $"{item.CategoryName} - {item.DisplayName} [{item.StatusLabel}]";
                GridDiffs.ItemsSource = item.ParameterDiffs;
            }
            else
            {
                TxtSelectedItemName.Text = "Inget objekt markerat";
                GridDiffs.ItemsSource = null;
            }
        }

        private void BtnZoomToElement_Click(object sender, RoutedEventArgs e)
        {
            ZoomToCurrentItem();
        }

        private void BtnSelectInRevit_Click(object sender, RoutedEventArgs e)
        {
            ZoomToCurrentItem();
        }

        private void GridItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ZoomToCurrentItem();
        }

        private void ZoomToCurrentItem()
        {
            if (GridItems.SelectedItem is ComparisonItem item)
            {
                if (item.HostId != null && item.HostId != ElementId.InvalidElementId)
                {
                    _eventHandler.TargetElementId = item.HostId;
                    _eventHandler.PendingAction = CompareAction.SelectAndZoom;
                    _externalEvent.Raise();
                }
                else if (item.Status == ComparisonStatus.Removed)
                {
                    MessageBox.Show("Objektet är borttaget ur den aktiva modellen (finns endast i den länkade modellen).\nKlicka på 'Färga vy' för att se dess röda placering i modellen.", 
                                    "Borttaget objekt", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnSelectAllCats_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _categories) c.IsSelected = true;
            ICollectionView view = CollectionViewSource.GetDefaultView(_categories);
            view?.Refresh();
        }

        private void BtnDeselectAllCats_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _categories) c.IsSelected = false;
            ICollectionView view = CollectionViewSource.GetDefaultView(_categories);
            view?.Refresh();
        }
    }
}
