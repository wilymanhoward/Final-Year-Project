using System;
using System.Globalization;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>How the case was placed in the room.</summary>
    public enum PlacementMethod
    {
        /// <summary>Not placed: CaseRoot is at the world origin.</summary>
        None = 0,
        /// <summary>Placed and held by a spatial anchor (stable across recentering and restarts).</summary>
        Anchor = 1,
        /// <summary>Placed from the head pose without an anchor (fallback; shifts if the user recenters).</summary>
        Manual = 2,
    }

    /// <summary>
    /// Immutable record of the current placement of CaseRoot: method, anchor id, world pose and time.
    /// Kept by <see cref="SessionManager"/> and written to the log and the session meta file.
    /// </summary>
    public sealed class PlacementRecord
    {
        public static readonly PlacementRecord None = new PlacementRecord(
            PlacementMethod.None, false, null, Vector3.zero, 0f, DateTime.MinValue);

        public PlacementMethod Method { get; }

        /// <summary>True if restored from a saved anchor ("placement_loaded"), false if newly set ("placement_set").</summary>
        public bool LoadedFromSave { get; }

        /// <summary>Spatial anchor UUID, or null for manual placement.</summary>
        public string AnchorUuid { get; }

        public Vector3 Position { get; }
        public float YawDegrees { get; }
        public DateTime Utc { get; }

        /// <summary>"anchor", "manual" or "none" (as written to logs).</summary>
        public string MethodCode => Method.ToString().ToLowerInvariant();

        public string EventName => LoadedFromSave ? LogEvents.PlacementLoaded : LogEvents.PlacementSet;

        public PlacementRecord(PlacementMethod method, bool loadedFromSave, string anchorUuid,
            Vector3 position, float yawDegrees, DateTime utc)
        {
            Method = method;
            LoadedFromSave = loadedFromSave;
            AnchorUuid = anchorUuid;
            Position = position;
            YawDegrees = yawDegrees;
            Utc = utc;
        }

        /// <summary>Event detail, e.g. "method=anchor;uuid=...;x=0.1200;y=0.0000;z=-1.5000;yaw_deg=90.00;at_utc=...".</summary>
        public string FormatDetail()
        {
            var inv = CultureInfo.InvariantCulture;
            return "method=" + MethodCode
                + ";uuid=" + (AnchorUuid ?? "")
                + ";x=" + Position.x.ToString("F4", inv)
                + ";y=" + Position.y.ToString("F4", inv)
                + ";z=" + Position.z.ToString("F4", inv)
                + ";yaw_deg=" + YawDegrees.ToString("F2", inv)
                + ";at_utc=" + FormatUtc(Utc);
        }

        public static string FormatUtc(DateTime utc)
        {
            return utc == DateTime.MinValue ? "" : utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }
    }
}
