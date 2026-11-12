using System;
using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>
    /// Pure rules of the floor check (user, 2026-11-12: after the headset is taken off and put on again the Quest's floor,
    /// and the anchor with it, can come back ~40 cm off). The depth sensor's hit heights are clustered: the floor is the
    /// lowest height that many hits agree on, so a few noisy hits below it and table tops above it are ignored.
    /// </summary>
    public static class FloorEstimate
    {
        /// <summary>Hits within this band (from the band's lowest hit) count as one surface.</summary>
        public const float ClusterBand = 0.04f;
        /// <summary>A surface needs at least this many hits, and this share of all hits, to be the floor.</summary>
        public const int MinClusterSize = 12;
        public const float MinClusterShare = 0.25f;
        /// <summary>
        /// Automatic checks leave smaller differences alone (so the scene doesn't twitch on every put-on); the Fix floor
        /// button applies anything above <see cref="ManualCorrectionThreshold"/>. Was 3 cm for both (user, 2026-11-13:
        /// "the floor is a little below the real floor" - the check measured it but moved 0).
        /// </summary>
        public const float AutoCorrectionThreshold = 0.015f;
        public const float ManualCorrectionThreshold = 0.005f;
        /// <summary>Larger corrections are refused as implausible.</summary>
        public const float MaxCorrection = 0.8f;
        /// <summary>The head must be at least this far above the measured floor.</summary>
        public const float MinHeadAboveFloor = 0.5f;

        /// <summary>Finds the floor height in <paramref name="heights"/>; false if no surface has enough support.</summary>
        public static bool TryFindFloor(IReadOnlyList<float> heights, out float floorY, out int support)
        {
            floorY = 0f;
            support = 0;
            if (heights == null || heights.Count == 0) return false;
            var h = new List<float>(heights);
            h.Sort();
            int need = Math.Max(MinClusterSize, (int)Math.Ceiling(MinClusterShare * h.Count));
            for (int i = 0, end = 0; i < h.Count; i++)
            {
                if (end < i) end = i;
                while (end + 1 < h.Count && h[end + 1] - h[i] <= ClusterBand) end++;
                int n = end - i + 1;
                if (n < need) continue;
                // The first band with enough hits starts in the floor's lower noise tail, so its median sits low
                // (user, 2026-11-13: floor a little below the real one). Re-centre on the hits around it instead.
                floorY = h[i + n / 2];
                for (int pass = 0; pass < 3; pass++)
                    floorY = MedianNear(h, floorY, out support);
                return true;
            }
            return false;
        }

        // Median of the sorted heights within ClusterBand of centre (h is sorted, so they are one run).
        private static float MedianNear(List<float> h, float centre, out int count)
        {
            int lo = 0;
            while (lo < h.Count && h[lo] < centre - ClusterBand) lo++;
            int hi = lo;
            while (hi < h.Count && h[hi] <= centre + ClusterBand) hi++;
            count = hi - lo;
            if (count == 0) return centre;
            return count % 2 == 1 ? h[lo + count / 2] : 0.5f * (h[lo + count / 2 - 1] + h[lo + count / 2]);
        }

        /// <summary>True when the measured floor is believable: not too far from the scene floor, head well above it.</summary>
        public static bool IsPlausible(float sceneFloorY, float measuredFloorY, float headY) =>
            Math.Abs(measuredFloorY - sceneFloorY) <= MaxCorrection && headY - measuredFloorY >= MinHeadAboveFloor;

        /// <summary>True when the scene floor is further than <paramref name="threshold"/> from the measured floor.</summary>
        public static bool NeedsCorrection(float sceneFloorY, float measuredFloorY, float threshold = AutoCorrectionThreshold) =>
            Math.Abs(measuredFloorY - sceneFloorY) > threshold;
    }
}
