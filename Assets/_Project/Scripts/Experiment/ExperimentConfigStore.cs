using System;
using System.IO;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Saves/loads the last used <see cref="ExperimentConfig"/> as JSON in
    /// Application.persistentDataPath, so settings survive app restarts and can be
    /// pushed to the headset with adb without rebuilding:
    ///   adb push experiment_config.json /sdcard/Android/data/&lt;package&gt;/files/
    /// </summary>
    public static class ExperimentConfigStore
    {
        public const string FileName = "experiment_config.json";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Overwrites <paramref name="target"/> from the file if it exists and parses.</summary>
        public static bool TryLoadInto(ExperimentConfig target)
        {
            try
            {
                if (target == null || !File.Exists(FilePath)) return false;
                JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), target);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExperimentConfigStore] Could not read {FilePath}: {e.Message}");
                return false;
            }
        }

        public static bool Save(ExperimentConfig config)
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(config, true));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExperimentConfigStore] Could not write {FilePath}: {e.Message}");
                return false;
            }
        }
    }
}
