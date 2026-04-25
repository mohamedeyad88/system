using System;

namespace Apex.Services.Users
{
    /// <summary>
    /// Event data for <see cref="UserSessionManager.SessionChanged"/>.
    /// </summary>
    public class UserSessionChangedEventArgs : EventArgs
    {
        public UserAccount? PreviousUser { get; init; }
        public UserAccount? CurrentUser  { get; init; }

        /// <summary>True when <see cref="CurrentUser"/> is null (i.e. a logout event).</summary>
        public bool IsLogout => CurrentUser == null;
    }

    /// <summary>
    /// Singleton that manages the desktop application's currently authenticated user.
    /// </summary>
    public class UserSessionManager
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static readonly UserSessionManager Instance = new();

        private UserSessionManager() { }

        // ── State ─────────────────────────────────────────────────────────────
        public UserAccount? CurrentUser { get; private set; }

        public bool IsLoggedIn   => CurrentUser != null;
        public bool IsAdmin      => CurrentUser?.Role == UserRole.Admin;

        /// <summary>True when the current user is a Supervisor or above.</summary>
        public bool IsSupervisor => CurrentUser?.Role >= UserRole.Supervisor;

        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler<UserSessionChangedEventArgs>? SessionChanged;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to authenticate with <paramref name="username"/> and
        /// <paramref name="password"/>. Sets <see cref="CurrentUser"/> and fires
        /// <see cref="SessionChanged"/> on success. Returns <c>false</c> on failure
        /// without throwing.
        /// </summary>
        public bool Login(string username, string password)
        {
            var user = UserAccountRepository.Instance.Authenticate(username, password);
            if (user == null) return false;

            var previous = CurrentUser;
            CurrentUser  = user;

            SessionChanged?.Invoke(this, new UserSessionChangedEventArgs
            {
                PreviousUser = previous,
                CurrentUser  = user
            });

            return true;
        }

        /// <summary>
        /// Logs out the current user. Sets <see cref="CurrentUser"/> to <c>null</c>
        /// and fires <see cref="SessionChanged"/>.
        /// </summary>
        public void Logout()
        {
            if (!IsLoggedIn) return;

            var previous = CurrentUser;
            CurrentUser  = null;

            SessionChanged?.Invoke(this, new UserSessionChangedEventArgs
            {
                PreviousUser = previous,
                CurrentUser  = null
            });
        }

        /// <summary>
        /// Logs out the current session and immediately attempts to log in with
        /// new credentials. Returns <c>true</c> on success; the current user is
        /// unchanged if the new credentials are rejected.
        /// </summary>
        public bool SwitchUser(string username, string password)
        {
            var newUser = UserAccountRepository.Instance.Authenticate(username, password);
            if (newUser == null) return false;

            var previous = CurrentUser;
            CurrentUser  = newUser;

            SessionChanged?.Invoke(this, new UserSessionChangedEventArgs
            {
                PreviousUser = previous,
                CurrentUser  = newUser
            });

            return true;
        }

        /// <summary>
        /// Throws <see cref="UnauthorizedAccessException"/> when the current user
        /// does not meet <paramref name="minimumRole"/>.
        /// </summary>
        public void RequireRole(UserRole minimumRole)
        {
            if (!IsLoggedIn || CurrentUser!.Role < minimumRole)
            {
                var requiredArabic = minimumRole switch
                {
                    UserRole.Admin      => "مدير",
                    UserRole.Supervisor => "مشرف",
                    UserRole.Operator   => "مشغّل",
                    _                   => minimumRole.ToString()
                };

                throw new UnauthorizedAccessException(
                    $"هذه العملية تتطلب دور '{requiredArabic}' على الأقل.");
            }
        }

        /// <summary>
        /// Creates a transient Guest <see cref="UserAccount"/> (not persisted) and
        /// sets it as the current user. Intended for kiosk / walk-up mode.
        /// </summary>
        public void LoginAsGuest()
        {
            var guest = new UserAccount
            {
                Id          = "guest_" + Guid.NewGuid().ToString("N")[..8],
                Username    = "guest",
                DisplayName = "زائر",
                Role        = UserRole.Guest,
                Department  = Department.General,
                IsActive    = true,
                CreatedAt   = DateTime.Now,
                // Guests get conservative defaults
                DailyPageQuota  = 20,
                DailyJobQuota   = 5,
                ColorPrintAllowed = false
            };

            var previous = CurrentUser;
            CurrentUser  = guest;

            SessionChanged?.Invoke(this, new UserSessionChangedEventArgs
            {
                PreviousUser = previous,
                CurrentUser  = guest
            });
        }
    }
}
