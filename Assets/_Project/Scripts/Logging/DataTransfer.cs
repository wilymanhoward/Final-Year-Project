using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FYP.Detective
{
    /// <summary>
    /// Protocol shared by the headset's <see cref="DataUploader"/> and the laptop's tools/receive_data.py (keep in sync):
    /// the receiver broadcasts a UDP beacon "FYPDATA1|&lt;httpPort&gt;" to port <see cref="BeaconPort"/> every 2 s; the
    /// headset PUTs each study file to http://&lt;laptop&gt;:&lt;httpPort&gt;/upload/&lt;device&gt;/&lt;file&gt; with header
    /// X-Sha256; the receiver answers 200 "OK &lt;sha256&gt;" after it has stored the file with that checksum.
    /// Plain HTTP on the local network only (user decision, session 6); files hold codes, never names.
    /// </summary>
    public static class DataTransfer
    {
        public const int DefaultHttpPort = 8765;
        public const int BeaconPort = 8766;
        public const string BeaconPrefix = "FYPDATA1|";
        public const string ShaHeader = "X-Sha256";
        public const int MaxFileNameLength = 128;

        /// <summary>Reads the receiver's HTTP port from a beacon message.</summary>
        public static bool TryParseBeacon(string message, out int httpPort)
        {
            httpPort = 0;
            if (message == null || !message.StartsWith(BeaconPrefix, StringComparison.Ordinal)) return false;
            return int.TryParse(message.Substring(BeaconPrefix.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out httpPort)
                && httpPort >= 1024 && httpPort <= 65535;
        }

        /// <summary>A file the study writes (run log, summary, meta JSON) with a name safe to put in a URL.</summary>
        public static bool IsStudyFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Length > MaxFileNameLength || !IsSafeName(fileName)) return false;
            return fileName.EndsWith(".csv", StringComparison.Ordinal) || fileName.EndsWith("_meta.json", StringComparison.Ordinal);
        }

        /// <summary>Letters, digits, '_', '-', '.' only, not starting with '.' (no paths, nothing to escape).</summary>
        public static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] == '.') return false;
            foreach (char c in name)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.'))
                    return false;
            return true;
        }

        /// <summary>Folder name for this headset on the laptop: "quest-" + the first 8 letters/digits of its device id.</summary>
        public static string DeviceTag(string deviceUniqueId)
        {
            var sb = new StringBuilder("quest-", 14);
            if (deviceUniqueId != null)
                foreach (char c in deviceUniqueId.ToLowerInvariant())
                {
                    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                    if (sb.Length == 14) break;
                }
            return sb.Length > 6 ? sb.ToString() : "quest-unknown";
        }

        public static string UploadUrl(string host, int port, string deviceTag, string fileName) =>
            "http://" + host + ":" + port.ToString(CultureInfo.InvariantCulture) + "/upload/" + deviceTag + "/" + fileName;

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>True if the receiver's reply confirms the file arrived with this checksum.</summary>
        public static bool IsConfirmed(long responseCode, string body, string sha256) =>
            responseCode == 200 && body != null && body.Trim() == "OK " + sha256;
    }
}
