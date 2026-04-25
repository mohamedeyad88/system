using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Apex.Services.Users
{
    public enum UserRole
    {
        Guest        = 0,   // Can submit jobs, no admin
        Operator     = 1,   // Can manage queue, cancel jobs
        Supervisor   = 2,   // Can view all analytics, manage quotas
        Admin        = 3    // Full access
    }

    public enum Department
    {
        General        = 0,
        Administration = 1,
        Finance        = 2,
        Operations     = 3,
        Marketing      = 4,
        HR             = 5,
        IT             = 6,
        Management     = 7
    }

    public class UserAccount
    {
        public string     Id                   { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string     Username             { get; set; } = "";
        public string     DisplayName          { get; set; } = "";
        public string     PasswordHash         { get; set; } = "";   // SHA-256 hex
        public UserRole   Role                 { get; set; } = UserRole.Operator;
        public Department Department           { get; set; } = Department.General;
        public bool       IsActive             { get; set; } = true;
        public DateTime   CreatedAt            { get; set; } = DateTime.Now;
        public DateTime?  LastLoginAt          { get; set; }
        public string?    Email                { get; set; }
        public string?    Phone                { get; set; }
        public string     Notes                { get; set; } = "";

        // Quota settings (null = unlimited)
        public int?  DailyPageQuota         { get; set; }
        public int?  MonthlyPageQuota       { get; set; }
        public int?  DailyJobQuota          { get; set; }
        public bool  ColorPrintAllowed      { get; set; } = true;
        public bool  DoubleSidedRequired    { get; set; } = false;  // Force duplex to save paper

        [JsonIgnore]
        public string RoleArabic => Role switch
        {
            UserRole.Guest      => "ضيف",
            UserRole.Operator   => "مشغّل",
            UserRole.Supervisor => "مشرف",
            UserRole.Admin      => "مدير",
            _                   => ""
        };

        [JsonIgnore]
        public string DepartmentArabic => Department switch
        {
            Department.Administration => "الإدارة",
            Department.Finance        => "المالية",
            Department.Operations     => "العمليات",
            Department.Marketing      => "التسويق",
            Department.HR             => "الموارد البشرية",
            Department.IT             => "تقنية المعلومات",
            Department.Management     => "الإدارة العليا",
            _                         => "عام"
        };
    }

    /// <summary>
    /// Password hashing helper using SHA-256 with a fixed application salt.
    /// </summary>
    public static class PasswordHelper
    {
        private const string Salt = "ApexPrintSalt2026";

        /// <summary>
        /// Hashes <paramref name="password"/> as SHA-256( password + salt ) and returns
        /// the result as a lowercase 64-character hex string.
        /// </summary>
        public static string Hash(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            var bytes  = Encoding.UTF8.GetBytes(password + Salt);
            var digest = SHA256.HashData(bytes);

            // Convert to lowercase hex without allocation overhead
            var sb = new StringBuilder(64);
            foreach (var b in digest)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="hash"/> equals the hash of
        /// <paramref name="password"/>. Uses a constant-time comparison to mitigate
        /// timing attacks.
        /// </summary>
        public static bool Verify(string password, string hash)
        {
            if (password == null || hash == null) return false;

            var computed = Hash(password);

            // Constant-time comparison
            if (computed.Length != hash.Length) return false;

            int diff = 0;
            for (int i = 0; i < computed.Length; i++)
                diff |= computed[i] ^ hash[i];

            return diff == 0;
        }
    }
}
