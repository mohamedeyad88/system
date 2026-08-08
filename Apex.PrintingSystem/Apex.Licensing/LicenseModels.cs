using System;

namespace Apex.Licensing
{
    /// <summary>
    /// Type of license granted.
    /// </summary>
    public enum LicenseType
    {
        Trial = 0,
        Full = 1,
        Pro = 2
    }

    /// <summary>
    /// Result of a license / trial validation check.
    /// </summary>
    public enum LicenseStatus
    {
        Valid,
        Expired,
        Invalid,
        ClockTampered,
        HardwareMismatch,
        Corrupted
    }

    /// <summary>
    /// The logical content of a signed license – serialized to JSON and then base64-encoded.
    /// Never add private keys or secrets here; this lives inside the client binary.
    /// </summary>
    public class LicensePayload
    {
        /// <summary>Schema version for forward compatibility.</summary>
        public string SchemaVersion { get; set; } = "1";

        /// <summary>Unique license identifier (UUID v4).</summary>
        public string LicenseId { get; set; } = "";

        public string DeviceId { get; set; } = "";
        public LicenseType Type { get; set; } = LicenseType.Full;
        public DateTime IssuedUtc { get; set; }

        /// <summary>For perpetual Full licenses, set to new DateTime(9999,12,31).</summary>
        public DateTime ExpiresUtc { get; set; }

        public string ProductVersion { get; set; } = "1.0";
        public string CustomerName { get; set; } = "";

        /// <summary>Optional order/invoice reference for admin audit trail.</summary>
        public string OrderReference { get; set; } = "";

        /// <summary>Max activations – always 1 for per-machine licenses.</summary>
        public int MaxActivations { get; set; } = 1;

        /// <summary>
        /// CSPRNG nonce (32 hex chars) prevents replay attacks.
        /// Every issued license has a unique nonce.
        /// </summary>
        public string Nonce { get; set; } = "";
    }

    /// <summary>
    /// The on-disk / transmitted license: base64(payload JSON) + ECDSA-P256 signature.
    /// </summary>
    public class SignedLicense
    {
        public string Payload { get; set; } = "";   // Base64-encoded JSON of LicensePayload
        public string Signature { get; set; } = "";   // Base64-encoded ECDSA-SHA256 signature
        public string SchemaVersion { get; set; } = "1";
    }

    /// <summary>
    /// Persisted trial state stored in multiple locations for tamper resistance.
    /// </summary>
    public class TrialState
    {
        public DateTime StartUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public string DeviceId { get; set; } = "";

        /// <summary>
        /// Trial epoch. A stored state whose epoch differs from
        /// <see cref="TrialManager.TrialEpoch"/> is ignored, which starts a fresh
        /// trial. Bumping the constant once forces a one-time trial reset across all
        /// machines when a new build is installed.
        /// </summary>
        public int Epoch { get; set; }

        public string Hmac { get; set; } = "";   // HMAC-SHA256 of (Epoch|StartUtc|LastSeenUtc|DeviceId)
    }

    /// <summary>
    /// Result returned to the caller after a full license check.
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public LicenseStatus Status { get; set; }
        public LicenseType Type { get; set; }
        public int DaysRemaining { get; set; }
        public string ErrorMessage { get; set; } = "";
        public DateTime? ExpiresUtc { get; set; }
    }

    /// <summary>
    /// Human-friendly device identity for display in the activation UI.
    /// </summary>
    public class DeviceIdentityInfo
    {
        /// <summary>Raw 32-char hex fingerprint hash.</summary>
        public string DeviceId { get; set; } = "";

        /// <summary>Formatted as XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX for display.</summary>
        public string DisplayId { get; set; } = "";
    }

    /// <summary>
    /// Activation request generated on the client machine.
    /// The user sends DeviceDisplayId to the company (e.g., via WhatsApp).
    /// </summary>
    public class ActivationRequest
    {
        public string ProductId { get; set; } = "APEX-PRINTING-SUITE";
        public string ProductVersion { get; set; } = "1.0";
        public string DeviceId { get; set; } = "";
        public string DeviceDisplayId { get; set; } = "";
        public DateTime RequestedUtc { get; set; }
        /// <summary>CSPRNG nonce – prevents replaying an old request.</summary>
        public string Nonce { get; set; } = "";
    }

    /// <summary>
    /// Audit record written by the Admin Tool for every issued license.
    /// Appended to audit.log (JSON-Lines format) in the Admin Tool's output directory.
    /// </summary>
    public class AdminAuditRecord
    {
        public string LicenseId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string OrderRef { get; set; } = "";
        public LicenseType LicenseType { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string OutputFile { get; set; } = "";
        /// <summary>Hostname of the machine that ran the Admin Tool.</summary>
        public string AdminMachine { get; set; } = "";
    }

    /// <summary>
    /// Data transfer object for the WhatsApp activation deep-link.
    /// </summary>
    public class WhatsAppActivationMessage
    {
        public string Phone { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string MessageText { get; set; } = "";
        public string Url { get; set; } = "";
    }
}

