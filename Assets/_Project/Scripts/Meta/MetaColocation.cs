using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Thin async wrapper around Meta's colocation session (Bluetooth + same Wi-Fi discovery), so the networking
    /// assembly can use it without Meta types. Results come back as plain values plus a label-ready error text.
    /// </summary>
    /// <remarks>
    /// Verified in com.meta.xr.sdk.core 207, Scripts/OVRColocationSession.cs: <c>StartAdvertisementAsync(ReadOnlySpan&lt;byte&gt;)</c>
    /// → <c>OVRResult&lt;Guid, Result&gt;</c> (metadata ≤ <c>Data.MaxMetadataSize</c> = 1024 bytes; the Guid is the group
    /// UUID for anchor sharing), <c>StopAdvertisementAsync</c>, <c>StartDiscoveryAsync</c>, <c>StopDiscoveryAsync</c>
    /// (→ <c>OVRResult&lt;Result&gt;</c>), static event <c>ColocationSessionDiscovered(Data)</c> with
    /// <c>AdvertisementUuid</c> / <c>Metadata</c>. AlreadyAdvertising/AlreadyDiscovering count as success.
    /// The permission com.oculus.permission.USE_COLOCATION_DISCOVERY_API is added at build time because
    /// colocationSessionSupport = Supported (Editor/OVRManifestPreprocessor.cs).
    /// </remarks>
    public static class MetaColocation
    {
        /// <summary>Raised on the main thread for every advertisement seen while discovering.</summary>
        public static event Action<Guid, byte[]> SessionDiscovered;

        private static bool _listening;

        /// <summary>Starts advertising <paramref name="metadata"/>. Returns the group UUID, or Guid.Empty + an error.</summary>
        public static async Task<(Guid group, string error)> StartAdvertisingAsync(byte[] metadata)
        {
            try
            {
                var result = await OVRColocationSession.StartAdvertisementAsync(metadata);
                if (result.TryGetValue(out Guid group)) return (group, null);
                Debug.LogWarning("[Colocation] StartAdvertisementAsync failed: " + result.Status);
                return (Guid.Empty, Explain(result.Status));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return (Guid.Empty, "advertising threw " + e.GetType().Name);
            }
        }

        public static async Task StopAdvertisingAsync()
        {
            try
            {
                var result = await OVRColocationSession.StopAdvertisementAsync();
                if (!result.Success) Debug.Log("[Colocation] StopAdvertisementAsync: " + result.Status);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Starts discovery. Returns null on success, else an error text.</summary>
        public static async Task<string> StartDiscoveryAsync()
        {
            if (!_listening)
            {
                OVRColocationSession.ColocationSessionDiscovered += HandleDiscovered;
                _listening = true;
            }
            try
            {
                var result = await OVRColocationSession.StartDiscoveryAsync();
                if (result.Success) return null;
                Debug.LogWarning("[Colocation] StartDiscoveryAsync failed: " + result.Status);
                return Explain(result.Status);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return "discovery threw " + e.GetType().Name;
            }
        }

        public static async Task StopDiscoveryAsync()
        {
            try
            {
                var result = await OVRColocationSession.StopDiscoveryAsync();
                if (!result.Success) Debug.Log("[Colocation] StopDiscoveryAsync: " + result.Status);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void HandleDiscovered(OVRColocationSession.Data data)
        {
            SessionDiscovered?.Invoke(data.AdvertisementUuid, data.Metadata);
        }

        private static string Explain(OVRColocationSession.Result status)
        {
            switch (status)
            {
                case OVRColocationSession.Result.NoDiscoveryMethodAvailable:
                    return "Bluetooth is off - turn it on in Quick Settings";
                case OVRColocationSession.Result.NetworkFailed:
                    return "not connected to Wi-Fi";
                case OVRColocationSession.Result.Unsupported:
                    return "colocation not supported (check OS version / permission)";
                default:
                    return "colocation failed (" + status + ")";
            }
        }
    }
}
