using Apex.Services.Users;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Apex.UI.ViewModels
{
    public partial class UserManagementViewModel : ViewModelBase
    {
        // ── Collections ───────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<UserAccount> _users = new();

        // ── Selection ─────────────────────────────────────────────────────────
        [ObservableProperty] private UserAccount? _selectedUser;

        // ── Search ────────────────────────────────────────────────────────────
        [ObservableProperty] private string _searchText = "";

        // ── Edit mode flag ────────────────────────────────────────────────────
        [ObservableProperty] private bool _isEditing = false;

        // ── Edit form fields ──────────────────────────────────────────────────
        [ObservableProperty] private string    _editUsername        = "";
        [ObservableProperty] private string    _editDisplayName     = "";
        [ObservableProperty] private string    _editPassword        = "";
        [ObservableProperty] private string    _editEmail           = "";
        [ObservableProperty] private UserRole  _editRole            = UserRole.Operator;
        [ObservableProperty] private Department _editDepartment     = Department.General;
        [ObservableProperty] private bool      _editIsActive        = true;
        [ObservableProperty] private bool      _editColorAllowed    = true;
        [ObservableProperty] private int?      _editDailyPageQuota;
        [ObservableProperty] private int?      _editMonthlyPageQuota;

        // ── Selected-user quota stats ─────────────────────────────────────────
        [ObservableProperty] private int    _selectedUserTodayPages;
        [ObservableProperty] private int    _selectedUserMonthPages;
        [ObservableProperty] private double _selectedUserQuotaPercent;
        [ObservableProperty] private string _selectedUserStatusText = "";

        // ── Enum collections for ComboBox binding ─────────────────────────────
        public UserRole[]   AvailableRoles       => (UserRole[])  Enum.GetValues(typeof(UserRole));
        public Department[] AvailableDepartments => (Department[])Enum.GetValues(typeof(Department));

        // ── Internal state ────────────────────────────────────────────────────
        private bool _isNewUser = false;

        // ── Initialization ────────────────────────────────────────────────────

        public override Task InitializeAsync()
        {
            LoadUsers();
            return Task.CompletedTask;
        }

        // ── Partial callbacks ─────────────────────────────────────────────────

        partial void OnSelectedUserChanged(UserAccount? value)
        {
            if (value == null)
            {
                SelectedUserTodayPages  = 0;
                SelectedUserMonthPages  = 0;
                SelectedUserQuotaPercent = 0;
                SelectedUserStatusText  = "";
                return;
            }

            var (todayPages, _, monthPages, _) =
                PrintQuotaManager.Instance.GetUsageSummary(value.Id);

            SelectedUserTodayPages = todayPages;
            SelectedUserMonthPages = monthPages;

            // Compute the most restrictive usage percentage
            double dailyPct   = 0;
            double monthlyPct = 0;

            if (value.DailyPageQuota.HasValue && value.DailyPageQuota.Value > 0)
                dailyPct = todayPages * 100.0 / value.DailyPageQuota.Value;

            if (value.MonthlyPageQuota.HasValue && value.MonthlyPageQuota.Value > 0)
                monthlyPct = monthPages * 100.0 / value.MonthlyPageQuota.Value;

            SelectedUserQuotaPercent = Math.Round(Math.Max(dailyPct, monthlyPct), 1);

            // Build status text
            SelectedUserStatusText = value.IsActive
                ? $"نشط — {SelectedUserTodayPages} صفحة اليوم، {SelectedUserMonthPages} هذا الشهر"
                : "موقوف";
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        private void LoadUsers()
        {
            var all     = UserAccountRepository.Instance.GetAll();
            var search  = SearchText?.Trim() ?? "";

            var filtered = string.IsNullOrWhiteSpace(search)
                ? all
                : all.Where(u =>
                    u.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    u.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (u.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
                  .ToList();

            Users.Clear();
            foreach (var user in filtered)
                Users.Add(user);
        }

        [RelayCommand]
        private void NewUser()
        {
            _isNewUser = true;

            EditUsername         = "";
            EditDisplayName      = "";
            EditPassword         = "";
            EditEmail            = "";
            EditRole             = UserRole.Operator;
            EditDepartment       = Department.General;
            EditIsActive         = true;
            EditColorAllowed     = true;
            EditDailyPageQuota   = null;
            EditMonthlyPageQuota = null;

            IsEditing = true;
        }

        [RelayCommand]
        private void EditUser()
        {
            if (SelectedUser == null) return;

            _isNewUser = false;

            EditUsername         = SelectedUser.Username;
            EditDisplayName      = SelectedUser.DisplayName;
            EditPassword         = "";   // Never pre-fill the password field
            EditEmail            = SelectedUser.Email ?? "";
            EditRole             = SelectedUser.Role;
            EditDepartment       = SelectedUser.Department;
            EditIsActive         = SelectedUser.IsActive;
            EditColorAllowed     = SelectedUser.ColorPrintAllowed;
            EditDailyPageQuota   = SelectedUser.DailyPageQuota;
            EditMonthlyPageQuota = SelectedUser.MonthlyPageQuota;

            IsEditing = true;
        }

        [RelayCommand]
        private void SaveUser()
        {
            // Validate
            if (string.IsNullOrWhiteSpace(EditUsername))
            {
                MessageBox.Show("يرجى إدخال اسم المستخدم.", "خطأ في التحقق",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(EditDisplayName))
            {
                MessageBox.Show("يرجى إدخال الاسم المعروض.", "خطأ في التحقق",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_isNewUser)
                {
                    var newAccount = new UserAccount
                    {
                        Username          = EditUsername.Trim(),
                        DisplayName       = EditDisplayName.Trim(),
                        PasswordHash      = string.IsNullOrWhiteSpace(EditPassword)
                                                ? PasswordHelper.Hash("apex123")
                                                : EditPassword,   // Will be hashed by repository
                        Email             = string.IsNullOrWhiteSpace(EditEmail) ? null : EditEmail.Trim(),
                        Role              = EditRole,
                        Department        = EditDepartment,
                        IsActive          = EditIsActive,
                        ColorPrintAllowed = EditColorAllowed,
                        DailyPageQuota    = EditDailyPageQuota,
                        MonthlyPageQuota  = EditMonthlyPageQuota,
                        CreatedAt         = DateTime.Now
                    };

                    UserAccountRepository.Instance.Create(newAccount);
                }
                else
                {
                    if (SelectedUser == null) return;

                    SelectedUser.Username          = EditUsername.Trim();
                    SelectedUser.DisplayName       = EditDisplayName.Trim();
                    SelectedUser.Email             = string.IsNullOrWhiteSpace(EditEmail) ? null : EditEmail.Trim();
                    SelectedUser.Role              = EditRole;
                    SelectedUser.Department        = EditDepartment;
                    SelectedUser.IsActive          = EditIsActive;
                    SelectedUser.ColorPrintAllowed = EditColorAllowed;
                    SelectedUser.DailyPageQuota    = EditDailyPageQuota;
                    SelectedUser.MonthlyPageQuota  = EditMonthlyPageQuota;

                    // Only update password if a new one was provided
                    if (!string.IsNullOrWhiteSpace(EditPassword))
                        SelectedUser.PasswordHash = EditPassword;  // Repository will hash if needed

                    UserAccountRepository.Instance.Update(SelectedUser);
                }

                LoadUsers();
                IsEditing = false;
                _isNewUser = false;
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "خطأ في الحفظ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"فشل حفظ البيانات: {ex.Message}", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void DeleteUser()
        {
            if (SelectedUser == null) return;

            var confirmResult = MessageBox.Show(
                $"هل أنت متأكد من حذف المستخدم '{SelectedUser.DisplayName}' ({SelectedUser.Username})؟",
                "تأكيد الحذف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            bool deleted = UserAccountRepository.Instance.Delete(SelectedUser.Id);

            if (!deleted)
            {
                MessageBox.Show(
                    "لا يمكن حذف هذا الحساب. يجب أن يكون في النظام مدير واحد على الأقل.",
                    "تعذّر الحذف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SelectedUser = null;
            LoadUsers();
        }

        [RelayCommand]
        private void CancelEdit()
        {
            IsEditing  = false;
            _isNewUser = false;
        }

        [RelayCommand]
        private void ResetPassword()
        {
            if (SelectedUser == null) return;

            const string defaultPassword = "apex123";
            SelectedUser.PasswordHash = PasswordHelper.Hash(defaultPassword);

            try
            {
                UserAccountRepository.Instance.Update(SelectedUser);
                MessageBox.Show(
                    $"تم إعادة تعيين كلمة مرور المستخدم '{SelectedUser.DisplayName}' إلى: {defaultPassword}",
                    "إعادة تعيين كلمة المرور",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"فشل تحديث كلمة المرور: {ex.Message}", "خطأ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void SearchUsers()
        {
            LoadUsers();
        }
    }
}
