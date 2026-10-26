using UnityEditor;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Hand tracking in Fast Motion Mode (user, session 6: the headset lost the hands 33 times in an 8 minute run and the Quest
    /// then showed its grey tracking-lost screen). Meta's own tooltip for HIGH: enable it when you see tracking loss from fast
    /// hand motion; the setting becomes the AndroidManifest meta-data com.oculus.handtracking.frequency at build time
    /// (com.meta.xr.sdk.core Editor/OVRProjectConfig.cs HandTrackingFrequency, Editor/OVRManifestPreprocessor.cs). Costs some
    /// battery. Idempotent; rebuild both headsets afterwards.
    /// </summary>
    public static class HandTrackingSetupTool
    {
        [MenuItem("FYP/Set Hand Tracking to Fast Motion (High)")]
        public static void SetUp()
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            bool changed = config.handTrackingFrequency != OVRProjectConfig.HandTrackingFrequency.HIGH;
            if (changed)
            {
                config.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
                OVRProjectConfig.CommitProjectConfig(config);
            }
            EditorUtility.DisplayDialog("Hand tracking",
                "Hand tracking frequency = High (Fast Motion Mode)" + (changed ? " (was Low)." : " (already set).") +
                "\nRebuild and install on both headsets to apply.", "OK");
        }

        /// <summary>Batch mode (Unity closed): the same, without the dialog blocking.</summary>
        public static void SetUpBatch() => SetUp();
    }
}
