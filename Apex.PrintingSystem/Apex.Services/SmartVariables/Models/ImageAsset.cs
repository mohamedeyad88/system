using System.Text.Json.Serialization;

namespace Apex.Services.SmartVariables.Models
{
    public enum AssetStatus { Available, Missing, Corrupted, UnsupportedFormat }

    public class ImageAsset
    {
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Extension { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public AssetStatus Status { get; set; } = AssetStatus.Available;
        public bool IsUsed { get; set; }
        public string? ThumbnailBase64 { get; set; }

        [JsonIgnore]
        public string StatusIcon => Status switch
        {
            AssetStatus.Available => IsUsed ? "✓" : "○",
            AssetStatus.Missing => "✗",
            AssetStatus.Corrupted => "⚠",
            AssetStatus.UnsupportedFormat => "?",
            _ => ""
        };

        [JsonIgnore]
        public string SizeLabel
        {
            get
            {
                if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
                if (FileSizeBytes < 1048576) return $"{FileSizeBytes / 1024.0:F0} KB";
                return $"{FileSizeBytes / 1048576.0:F1} MB";
            }
        }

        [JsonIgnore]
        public string DimensionsLabel =>
            Width > 0 && Height > 0 ? $"{Width}×{Height}" : "";
    }
}
