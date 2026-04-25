using Apex.Services.Printing.LoadBalancer;
using Apex.Services.Printing.VendorDetection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Apex.UI.ViewModels
{
    public partial class LoadBalancerViewModel : ViewModelBase
    {
        public LoadBalancerViewModel()
        {
            PrinterGroupManager.Instance.JobRouted += (_, decision) =>
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    RecentDecisions.Insert(0, decision);
                    while (RecentDecisions.Count > 50) RecentDecisions.RemoveAt(RecentDecisions.Count - 1);
                });
        }

        // ── Properties ────────────────────────────────────────────────

        [ObservableProperty] private ObservableCollection<PrinterGroup>    _groups          = new();
        [ObservableProperty] private PrinterGroup?                         _selectedGroup;
        [ObservableProperty] private ObservableCollection<RoutingDecision> _recentDecisions = new();
        [ObservableProperty] private bool   _isCreatingGroup   = false;
        [ObservableProperty] private string _newGroupName      = "";
        [ObservableProperty] private LoadBalancingStrategy _newGroupStrategy = LoadBalancingStrategy.LeastLoaded;
        [ObservableProperty] private string _statusText        = "جاهز";
        [ObservableProperty] private ObservableCollection<string> _availablePrinters = new();
        [ObservableProperty] private string? _selectedAvailablePrinter;

        public LoadBalancingStrategy[] AllStrategies =>
            (LoadBalancingStrategy[])Enum.GetValues(typeof(LoadBalancingStrategy));

        // ── Commands ──────────────────────────────────────────────────

        [RelayCommand]
        private async Task LoadAsync()
        {
            Groups.Clear();
            foreach (var g in PrinterGroupManager.Instance.GetAll())
                Groups.Add(g);

            AvailablePrinters.Clear();
            try
            {
                var printers = await VendorDetectionEngine.Instance.DetectAllPrintersAsync();
                foreach (var p in printers.OrderBy(p => p.Name))
                    AvailablePrinters.Add(p.Name);
            }
            catch (Exception ex)
            {
                StatusText = $"خطأ في تحميل الطابعات: {ex.Message}";
            }

            var decisions = PrinterGroupManager.Instance.GetRecentDecisions(50);
            RecentDecisions.Clear();
            foreach (var d in decisions) RecentDecisions.Add(d);
        }

        [RelayCommand]
        private void NewGroup()
        {
            NewGroupName     = "";
            NewGroupStrategy = LoadBalancingStrategy.LeastLoaded;
            IsCreatingGroup  = true;
        }

        [RelayCommand]
        private void CancelNewGroup()
        {
            IsCreatingGroup = false;
            NewGroupName    = "";
        }

        [RelayCommand]
        private void SaveNewGroup()
        {
            if (string.IsNullOrWhiteSpace(NewGroupName))
            {
                MessageBox.Show("يرجى إدخال اسم للمجموعة", "تحقق",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var group = new PrinterGroup
            {
                Name     = NewGroupName,
                Strategy = NewGroupStrategy,
                IsEnabled = true
            };
            PrinterGroupManager.Instance.AddGroup(group);
            IsCreatingGroup = false;
            NewGroupName    = "";

            Groups.Clear();
            foreach (var g in PrinterGroupManager.Instance.GetAll()) Groups.Add(g);
            StatusText = $"تمت إضافة المجموعة '{group.Name}'";
        }

        [RelayCommand]
        private void DeleteSelectedGroup()
        {
            if (SelectedGroup == null) return;
            var r = MessageBox.Show(
                $"هل تريد حذف مجموعة '{SelectedGroup.Name}'؟",
                "حذف مجموعة", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            PrinterGroupManager.Instance.DeleteGroup(SelectedGroup.Id);
            SelectedGroup = null;
            Groups.Clear();
            foreach (var g in PrinterGroupManager.Instance.GetAll()) Groups.Add(g);
        }

        [RelayCommand]
        private void AddMemberToGroup()
        {
            if (SelectedGroup == null || string.IsNullOrEmpty(SelectedAvailablePrinter)) return;
            if (SelectedGroup.Members.Any(m => m.PrinterName == SelectedAvailablePrinter)) return;

            SelectedGroup.Members.Add(new PrinterGroupMember
            {
                PrinterName = SelectedAvailablePrinter,
                IsEnabled   = true,
                Weight      = 1,
                MaxConcurrent = 1
            });
            PrinterGroupManager.Instance.UpdateGroup(SelectedGroup);
            StatusText = $"تمت إضافة '{SelectedAvailablePrinter}' إلى المجموعة";
            OnPropertyChanged(nameof(SelectedGroup));
        }

        [RelayCommand]
        private void RemoveMemberFromGroup(PrinterGroupMember? member)
        {
            if (SelectedGroup == null || member == null) return;
            SelectedGroup.Members.Remove(member);
            PrinterGroupManager.Instance.UpdateGroup(SelectedGroup);
            OnPropertyChanged(nameof(SelectedGroup));
        }

        [RelayCommand]
        private async Task CreateDefaultGroupAsync()
        {
            StatusText = "جارٍ الإنشاء...";
            try
            {
                await PrinterGroupManager.Instance.CreateDefaultGroupFromInstalledPrintersAsync();
                await LoadAsync();
                MessageBox.Show("تم إنشاء المجموعة الافتراضية بنجاح", "نجاح",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText = "تم إنشاء المجموعة الافتراضية ✅";
            }
            catch (Exception ex)
            {
                StatusText = $"خطأ: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task TestRoutingAsync()
        {
            if (SelectedGroup == null) return;
            try
            {
                var decision = await PrinterGroupManager.Instance.RouteJobAsync(
                    "test", SelectedGroup.Name);

                var msg = string.IsNullOrEmpty(decision.SelectedPrinter)
                    ? $"لا توجد طابعة متاحة\nالسبب: {decision.Reason}"
                    : $"سيتم التوجيه إلى: {decision.SelectedPrinter}\n" +
                      $"الاستراتيجية: {decision.Strategy}\n" +
                      $"السبب: {decision.Reason}" +
                      (decision.WasFallback ? "\n⚠️ تم استخدام الاحتياطي" : "");

                MessageBox.Show(msg, "اختبار التوجيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ: {ex.Message}", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        partial void OnSelectedGroupChanged(PrinterGroup? value)
        {
            if (value == null) return;
            RecentDecisions.Clear();
            var decisions = PrinterGroupManager.Instance
                .GetRecentDecisions(50)
                .Where(d => d.RequestedGroup == value.Id || d.RequestedGroup == value.Name);
            foreach (var d in decisions) RecentDecisions.Add(d);
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await LoadAsync();
        }
    }
}
