using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Apex.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Apex.AdminTool
{
    public partial class LicenseGeneratorViewModel : ObservableObject
    {
        // ── Inputs ──────────────────────────────────────────────────────────
        [ObservableProperty] private string _deviceId = "";
        [ObservableProperty] private string _customerName = "";
        [ObservableProperty] private string _orderRef = "";
        [ObservableProperty] private int _licenseTypeIndex = 0;   // 0=Full 1=Pro
        [ObservableProperty] private DateTime _expiryDate = DateTime.Today.AddYears(1);
        [ObservableProperty] private bool _isPerpetual = false;

        // ── Key ─────────────────────────────────────────────────────────────
        [ObservableProperty] private string _privateKeyPem = "";
        [ObservableProperty] private string _keyStatusText = "لم يتم تحميل المفتاح";
        [ObservableProperty] private string _keyStatusColor = "#EF4444";

        // ── Output ───────────────────────────────────────────────────────────
        [ObservableProperty] private string _resultText = "";
        [ObservableProperty] private string _resultColor = "#94A3B8";
        [ObservableProperty] private bool _isBusy = false;

        // ── Audit Log ────────────────────────────────────────────────────────
        [ObservableProperty] private string _auditLog = "";

        private static readonly string AuditPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "audit.log");

        // ── Constructor ─────────────────────────────────────────────────────
        public LicenseGeneratorViewModel()
        {
            TryAutoLoadPrivateKey();
        }

        /// <summary>
        /// Looks for private.pem next to the exe, then on the Desktop.
        /// Silently loads it so the tool is ready without any manual step.
        /// </summary>
        private void TryAutoLoadPrivateKey()
        {
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "private.pem"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "private.pem"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "OneDrive", "سطح المكتب", "private.pem")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var pem = File.ReadAllText(path).Trim();
                    using var test = ECDsa.Create();
                    test.ImportFromPem(pem);

                    PrivateKeyPem = pem;
                    KeyStatusText = $"✓  تم التحميل التلقائي: {Path.GetFileName(path)}";
                    KeyStatusColor = "#22C55E";
                    return;
                }
                catch { /* try next candidate */ }
            }
        }

        // ── Quick-expiry helpers ─────────────────────────────────────────────
        [RelayCommand]
        private void SetExpiry30() => ExpiryDate = DateTime.Today.AddDays(30);
        [RelayCommand]
        private void SetExpiry90() => ExpiryDate = DateTime.Today.AddDays(90);
        [RelayCommand]
        private void SetExpiry180() => ExpiryDate = DateTime.Today.AddDays(180);
        [RelayCommand]
        private void SetExpiry1Y() => ExpiryDate = DateTime.Today.AddYears(1);
        [RelayCommand]
        private void SetExpiry2Y() => ExpiryDate = DateTime.Today.AddYears(2);
        [RelayCommand]
        private void SetPerpetual() => ExpiryDate = new DateTime(9999, 12, 31);

        // ── Load Private Key ─────────────────────────────────────────────────
        [RelayCommand]
        private void LoadPrivateKey()
        {
            var dlg = new OpenFileDialog
            {
                Title = "اختر ملف المفتاح الخاص",
                Filter = "PEM Files|*.pem|All Files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var pem = File.ReadAllText(dlg.FileName).Trim();
                using var test = ECDsa.Create();
                test.ImportFromPem(pem);   // Validate

                PrivateKeyPem = pem;
                KeyStatusText = $"✓  {Path.GetFileName(dlg.FileName)}";
                KeyStatusColor = "#22C55E";
            }
            catch (Exception ex)
            {
                KeyStatusText = $"✗  مفتاح غير صالح: {ex.Message}";
                KeyStatusColor = "#EF4444";
                PrivateKeyPem = "";
            }
        }

        // ── Paste Private Key ────────────────────────────────────────────────
        [RelayCommand]
        private void PastePrivateKey()
        {
            try
            {
                var pem = Clipboard.GetText()?.Trim() ?? "";
                if (!pem.Contains("PRIVATE KEY"))
                {
                    KeyStatusText = "✗  الحافظة لا تحتوي على مفتاح PEM";
                    KeyStatusColor = "#EF4444";
                    return;
                }
                using var test = ECDsa.Create();
                test.ImportFromPem(pem);

                PrivateKeyPem = pem;
                KeyStatusText = "✓  تم لصق المفتاح بنجاح";
                KeyStatusColor = "#22C55E";
            }
            catch (Exception ex)
            {
                KeyStatusText = $"✗  {ex.Message}";
                KeyStatusColor = "#EF4444";
                PrivateKeyPem = "";
            }
        }

        // ── Generate License ─────────────────────────────────────────────────
        [RelayCommand]
        private void GenerateLicense()
        {
            // Validation
            if (string.IsNullOrWhiteSpace(PrivateKeyPem))
            {
                SetResult("يرجى تحميل المفتاح الخاص أولاً", "#EF4444"); return;
            }
            if (string.IsNullOrWhiteSpace(DeviceId))
            {
                SetResult("يرجى إدخال رقم الجهاز (Device ID)", "#EF4444"); return;
            }
            if (string.IsNullOrWhiteSpace(CustomerName))
            {
                SetResult("يرجى إدخال اسم العميل", "#EF4444"); return;
            }
            if (ExpiryDate <= DateTime.Today && !IsPerpetual)
            {
                SetResult("تاريخ الانتهاء يجب أن يكون في المستقبل", "#EF4444"); return;
            }

            IsBusy = true;
            try
            {
                var deviceId = DeviceId.Replace("-", "").Trim().ToUpper();
                var licType = LicenseTypeIndex == 1 ? LicenseType.Pro : LicenseType.Full;
                var expiry = IsPerpetual ? new DateTime(9999, 12, 31) : ExpiryDate.Date.AddHours(23).AddMinutes(59).AddSeconds(59);

                var payload = new LicensePayload
                {
                    LicenseId = Guid.NewGuid().ToString("N").ToUpper(),
                    DeviceId = deviceId,
                    CustomerName = CustomerName.Trim(),
                    OrderReference = OrderRef.Trim(),
                    Type = licType,
                    IssuedUtc = DateTime.UtcNow,
                    ExpiresUtc = expiry,
                    ProductVersion = "1.0",
                    MaxActivations = 1,
                    Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
                };

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(PrivateKeyPem);
                var signed = LicenseCrypto.SignLicense(payload, ecdsa);

                // Save file
                var safeName = MakeSafeFileName(CustomerName.Trim());
                var fileName = $"{safeName}_{deviceId[..8]}.apex";
                var saveDlg = new SaveFileDialog
                {
                    Title = "حفظ ملف الترخيص",
                    FileName = fileName,
                    Filter = "Apex License|*.apex|All Files|*.*",
                    DefaultExt = ".apex"
                };
                if (saveDlg.ShowDialog() != true) { IsBusy = false; return; }

                var json = JsonSerializer.Serialize(signed, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(saveDlg.FileName, json, System.Text.Encoding.UTF8);

                // Write audit
                WriteAudit(new AdminAuditRecord
                {
                    LicenseId = payload.LicenseId,
                    DeviceId = deviceId,
                    CustomerName = payload.CustomerName,
                    OrderRef = payload.OrderReference,
                    LicenseType = licType,
                    IssuedUtc = payload.IssuedUtc,
                    ExpiresUtc = expiry,
                    OutputFile = saveDlg.FileName,
                    AdminMachine = Environment.MachineName
                });

                var expiryTxt = IsPerpetual ? "دائم" : expiry.ToString("yyyy-MM-dd");
                SetResult(
                    $"✓  تم إنشاء الترخيص بنجاح\n" +
                    $"   العميل  : {CustomerName.Trim()}\n" +
                    $"   الجهاز  : {FormatDeviceId(deviceId)}\n" +
                    $"   النوع   : {licType}\n" +
                    $"   الانتهاء: {expiryTxt}\n" +
                    $"   الملف   : {Path.GetFileName(saveDlg.FileName)}",
                    "#22C55E");

                LoadAuditLog();
            }
            catch (Exception ex)
            {
                SetResult($"✗  خطأ: {ex.Message}", "#EF4444");
            }
            finally { IsBusy = false; }
        }

        // ── Copy Result ──────────────────────────────────────────────────────
        [RelayCommand]
        private void CopyResult() => Clipboard.SetText(ResultText);

        // ── Load Audit Log ───────────────────────────────────────────────────
        [RelayCommand]
        public void LoadAuditLog()
        {
            try
            {
                if (!File.Exists(AuditPath)) { AuditLog = "لا يوجد سجل بعد."; return; }
                var lines = File.ReadAllLines(AuditPath);
                var output = "";
                foreach (var line in lines)
                {
                    try
                    {
                        var rec = JsonSerializer.Deserialize<AdminAuditRecord>(line);
                        if (rec == null) continue;
                        var exp = rec.ExpiresUtc.Year == 9999 ? "دائم" : rec.ExpiresUtc.ToString("yyyy-MM-dd");
                        output += $"[{rec.IssuedUtc:yyyy-MM-dd HH:mm}]  {rec.CustomerName,-20}  {FormatDeviceId(rec.DeviceId),-38}  {rec.LicenseType,-5}  {exp}\n";
                    }
                    catch { }
                }
                AuditLog = string.IsNullOrWhiteSpace(output) ? "السجل فارغ." : output;
            }
            catch { AuditLog = "تعذّر تحميل السجل."; }
        }

        // ── Generate Key Pair ────────────────────────────────────────────────
        [RelayCommand]
        private void GenerateKeyPair()
        {
            var dlg = new SaveFileDialog
            {
                Title = "اختر مجلد حفظ المفاتيح",
                FileName = "private",
                Filter = "PEM Files|*.pem",
                DefaultExt = ".pem"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var (priv, pub) = LicenseCrypto.GenerateKeyPair();
                var dir = Path.GetDirectoryName(dlg.FileName)!;
                var privPath = Path.Combine(dir, "private.pem");
                var pubPath = Path.Combine(dir, "public.pem");

                File.WriteAllText(privPath, priv, System.Text.Encoding.UTF8);
                File.WriteAllText(pubPath, pub, System.Text.Encoding.UTF8);

                MessageBox.Show(
                    $"✓  تم توليد زوج المفاتيح:\n\nالخاص : {privPath}\nالعام  : {pubPath}\n\n⚠  احتفظ بـ private.pem في مكان آمن وابدأ به الأداة.\nانسخ public.pem إلى LicenseCrypto.EmbeddedPublicKeyPem في المشروع.",
                    "تم توليد المفاتيح", MessageBoxButton.OK, MessageBoxImage.Information);

                PrivateKeyPem = priv;
                KeyStatusText = "✓  تم توليد مفتاح جديد";
                KeyStatusColor = "#22C55E";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private void SetResult(string msg, string color)
        {
            ResultText = msg;
            ResultColor = color;
        }

        private static string MakeSafeFileName(string name) =>
            string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");

        private static string FormatDeviceId(string raw)
        {
            raw = raw.Replace("-", "").ToUpper();
            if (raw.Length == 32)
                return $"{raw[..8]}-{raw[8..16]}-{raw[16..24]}-{raw[24..32]}";
            return raw;
        }

        private static void WriteAudit(AdminAuditRecord record)
        {
            try
            {
                var line = JsonSerializer.Serialize(record);
                File.AppendAllText(AuditPath, line + "\n", System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
