using UnityEditor;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Hand tracking frequency = Low, Meta's default (user decision, session 6, replacing the earlier Fast Motion / High
    /// trial). The study's hand use is slow (reach, grab, read, poke), which Meta's tooltip says Low is made for; High is
    /// meant for tracking loss from fast motion and costs extra processing. The setting becomes the AndroidManifest
    /// meta-data com.oculus.handtracking.frequency at build time (com.meta.xr.sdk.core Editor/OVRProjectConfig.cs
    /// HandTrackingFrequency, Editor/OVRManifestPreprocessor.cs:727). FYP > Prepare Study Build checks it. Idempotent;
    /// rebuild both headsets afterwards.
    /// </summary>
    public static class HandTrackingSetupTool
    {
        public const OVRProjectConfig.HandTrackingFrequency StudyFrequency = OVRProjectConfig.HandTrackingFrequency.LOW;

        [MenuItem("FYP/Set Hand Tracking to Low (default)")]
        public static void SetUp()
        {
            bool changed = Apply();
            EditorUtility.DisplayDialog("Hand tracking",
                "Hand tracking frequency = Low (default)" + (changed ? " (was Fast Motion / High)." : " (already set).") +
                "\nRebuild and install on both headsets to apply.", "OK");
        }

        /// <summary>Batch mode (Unity closed): the same, without the dialog blocking.</summary>
        public static void SetUpBatch() => Apply();

        private static bool Apply()
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config.handTrackingFrequency == StudyFrequency) return false;
            config.handTrackingFrequency = StudyFrequency;
            OVRProjectConfig.CommitProjectConfig(config);
            return true;
        }
    }
}
