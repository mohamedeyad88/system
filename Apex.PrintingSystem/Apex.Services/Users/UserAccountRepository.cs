using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apex.Services.Users
{
    /// <summary>
    /// Singleton repository that persists user accounts to
    /// <c>%AppData%\Apex\Users\accounts.json</c>.
    /// </summary>
    public class UserAccountRepository
    {
        // ── Singleton ────────────────────────────────────────────────────────────
        public static readonly UserAccountRepository Instance = new();

        // ── State ─────────────────────────────────────────────────────────────
        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, UserAccount> _accounts = new(StringComparer.Ordinal);

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented       = true,
            Converters          = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        // ── Constructor ───────────────────────────────────────────────────────
        private UserAccountRepository()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Apex", "Users");

            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "accounts.json");

            Load();
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public UserAccount? GetById(string id)
        {
            _accounts.TryGetValue(id, out var account);
            return account;
        }

        public UserAccount? GetByUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            return _accounts.Values.FirstOrDefault(
                a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<UserAccount> GetAll()
            => _accounts.Values.OrderBy(a => a.Username).ToList();

        public IReadOnlyList<UserAccount> GetByDepartment(Department dept)
            => _accounts.Values.Where(a => a.Department == dept).OrderBy(a => a.Username).ToList();

        public IReadOnlyList<UserAccount> GetByRole(UserRole role)
            => _accounts.Values.Where(a => a.Role == role).OrderBy(a => a.Username).ToList();

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>
        /// Validates and persists a new <see cref="UserAccount"/>.
        /// If <see cref="UserAccount.PasswordHash"/> is not a 64-character hex string it
        /// is treated as a plain-text password and hashed automatically.
        /// </summary>
        public UserAccount Create(UserAccount account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));

            if (string.IsNullOrWhiteSpace(account.Username))
                throw new ArgumentException("اسم المستخدم مطلوب.", nameof(account));

            if (GetByUsername(account.Username) != null)
                throw new ArgumentException($"اسم المستخدم '{account.Username}' مستخدم بالفعل.", nameof(account));

            // Hash password if it looks like plain text (not a 64-char hex string)
            if (!IsHexHash(account.PasswordHash))
                account.PasswordHash = PasswordHelper.Hash(account.PasswordHash);

            if (!_accounts.TryAdd(account.Id, account))
                throw new InvalidOperationException("تعارض في معرّف الحساب — يرجى المحاولة مرة أخرى.");

            Save();
            return account;
        }

        /// <summary>
        /// Updates an existing account in memory and persists it.
        /// </summary>
        public void Update(UserAccount account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));

            if (!_accounts.ContainsKey(account.Id))
                throw new KeyNotFoundException($"لا يوجد حساب بالمعرّف: {account.Id}");

            // If the caller changed the password to plain text, re-hash
            if (!IsHexHash(account.PasswordHash))
                account.PasswordHash = PasswordHelper.Hash(account.PasswordHash);

            _accounts[account.Id] = account;
            Save();
        }

        /// <summary>
        /// Deletes an account by ID. Returns <c>false</c> (without deleting) when the
        /// account is the last remaining Admin.
        /// </summary>
        public bool Delete(string id)
        {
            if (!_accounts.TryGetValue(id, out var account))
                return false;

            // Protect the last Admin
            if (account.Role == UserRole.Admin)
            {
                int adminCount = _accounts.Values.Count(a => a.Role == UserRole.Admin);
                if (adminCount <= 1)
                    return false;
            }

            _accounts.TryRemove(id, out _);
            Save();
            return true;
        }

        /// <summary>
        /// Authenticates a user by username / plain-text password.
        /// Updates <see cref="UserAccount.LastLoginAt"/> on success.
        /// Returns <c>null</c> on failure.
        /// </summary>
        public UserAccount? Authenticate(string username, string password)
        {
            var account = GetByUsername(username);
            if (account == null) return null;
            if (!account.IsActive) return null;
            if (!PasswordHelper.Verify(password, account.PasswordHash)) return null;

            account.LastLoginAt = DateTime.Now;
            Save();
            return account;
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private void Save()
        {
            var list = _accounts.Values.ToList();
            var json = JsonSerializer.Serialize(list, _jsonOptions);

            // Atomic write: write to temp, then rename
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
            File.Move(tmp, _filePath, overwrite: true);
        }

        private void Load()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    var json     = File.ReadAllText(_filePath, System.Text.Encoding.UTF8);
                    var accounts = JsonSerializer.Deserialize<List<UserAccount>>(json, _jsonOptions);

                    if (accounts != null && accounts.Count > 0)
                    {
                        foreach (var a in accounts)
                            _accounts[a.Id] = a;

                        return; // loaded successfully
                    }
                }
                catch
                {
                    // File corrupt — fall through to create defaults
                }
            }

            CreateDefaultAdmin();
        }

        private void CreateDefaultAdmin()
        {
            var admin = new UserAccount
            {
                Username    = "admin",
                DisplayName = "المدير",
                PasswordHash = PasswordHelper.Hash("admin123"),
                Role        = UserRole.Admin,
                Department  = Department.Management,
                IsActive    = true,
                CreatedAt   = DateTime.Now,
                Notes       = "الحساب الافتراضي للمدير"
            };

            _accounts[admin.Id] = admin;
            Save();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> when <paramref name="value"/> is a 64-character
        /// lowercase/uppercase hexadecimal string (i.e. already hashed).
        /// </summary>
        private static bool IsHexHash(string? value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }
    }
}
