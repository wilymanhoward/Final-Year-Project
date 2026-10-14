using System;
using System.Globalization;
using System.Text;

namespace FYP.Detective
{
    /// <summary>
    /// Wire formats for the team condition, kept free of Meta/Netcode types so they can be unit tested:
    /// the colocation advertisement metadata (how the client finds the host) and the message in which the host
    /// tells the client which shared anchor holds CaseRoot.
    /// </summary>
    public static class ColocationProtocol
    {
        /// <summary>Prefix of our advertisements; other apps' adverts (or older builds) are ignored.</summary>
        public const string AdvertPrefix = "FYPDET1";

        /// <summary>Named Netcode message carrying <see cref="FormatAnchorMessage"/>.</summary>
        public const string AnchorMessageName = "fyp.case_anchor";

        /// <summary>Named Netcode message (empty payload) a client sends to ask the host for the anchor message.</summary>
        public const string AnchorRequestName = "fyp.case_anchor_request";

        /// <summary>Maximum advert size allowed by OVRColocationSession.Data.MaxMetadataSize.</summary>
        public const int MaxAdvertBytes = 1024;

        /// <summary>UTF-8 "FYPDET1|ip|port".</summary>
        public static byte[] EncodeAdvert(string ipv4, int port)
        {
            if (!LanAddress.TryParseIpv4(ipv4, out string ip)) throw new ArgumentException("Not an IPv4 address: " + ipv4);
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            return Encoding.UTF8.GetBytes(AdvertPrefix + "|" + ip + "|" + port.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>False for null, foreign or malformed adverts.</summary>
        public static bool TryDecodeAdvert(byte[] data, out string ipv4, out int port)
        {
            ipv4 = null;
            port = 0;
            if (data == null || data.Length == 0 || data.Length > MaxAdvertBytes) return false;
            string text;
            try { text = Encoding.UTF8.GetString(data); }
            catch (ArgumentException) { return false; }

            string[] parts = text.Split('|');
            if (parts.Length != 3 || parts[0] != AdvertPrefix) return false;
            if (!LanAddress.TryParseIpv4(parts[1], out ipv4)) return false;
            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
            {
                ipv4 = null;
                port = 0;
                return false;
            }
            return true;
        }

        /// <summary>"groupUuid|anchorUuid" (both "D" format).</summary>
        public static string FormatAnchorMessage(Guid group, Guid anchor)
        {
            return group.ToString("D") + "|" + anchor.ToString("D");
        }

        public static bool TryParseAnchorMessage(string text, out Guid group, out Guid anchor)
        {
            group = anchor = Guid.Empty;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('|');
            return parts.Length == 2
                && Guid.TryParseExact(parts[0], "D", out group) && group != Guid.Empty
                && Guid.TryParseExact(parts[1], "D", out anchor) && anchor != Guid.Empty;
        }

        /// <summary>
        /// Short label text for a failed anchor share/load, by the Meta result name
        /// (OVRAnchor.ShareResult / OVRSpatialAnchor.OperationResult member, e.g. "FailureNetworkTimeout").
        /// </summary>
        public static string ExplainAnchorFailure(string statusName)
        {
            switch (statusName)
            {
                case "FailureNetworkTimeout":
                case "FailureNetworkRequestFailed":
                case "Failure_SpaceNetworkTimeout":
                case "Failure_SpaceNetworkRequestFailed":
                    return "no internet / Meta cloud unreachable - check Wi-Fi internet or use a phone hotspot";
                case "FailureCloudStorageDisabled":
                case "Failure_SpaceCloudStorageDisabled":
                    return "'Share Point Cloud Data' (Enhanced Spatial Services) is off - Settings > Privacy > Device permissions";
                case "FailurePermissionInsufficient":
                case "Failure_SpacePermissionInsufficient":
                    return "spatial data permission missing - allow it and retry";
                case "FailureMappingInsufficient":
                case "Failure_SpaceMappingInsufficient":
                    return "room not mapped enough - look around the room for a few seconds and retry";
                case "FailureLocalizationFailed":
                case "Failure_SpaceLocalizationFailed":
                    return "could not find the anchor in this room - same room? good lighting? retry, or use Manual (X)";
                case "Failure_GroupNotFound":
                    return "host's anchor not found in the session - re-place on the host (A) and rejoin";
                default:
                    return "failed (" + statusName + ")";
            }
        }
    }
}
