using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// FYP > Set Up Everything (one click) (user, 2026-11-05: "one button instead of every FYP menu"): opens Main.unity and
    /// runs the setup chain in the documented order - Build Case Scenes, Set Up Networking, Set Up Evidence Board, Set Up
    /// Headset UI, Set Up Occlusion, Set Up Briefing Detective, Set Hand Tracking to Low, Prepare Study Build - collecting
    /// their message boxes into one summary. Every step is idempotent and saves the scene. Stops at the first step that
    /// throws. Also runs in batch mode (-executeMethod FYP.Detective.EditorTools.SetUpEverythingTool.RunBatch).
    /// </summary>
    public static class SetUpEverythingTool
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";

        private static readonly (string Name, Action Run)[] Steps =
        {
            ("Build Case Scenes", CaseSceneTool.Build),
            ("Set Up Networking", NetworkSetupTool.SetUp),
            ("Set Up Evidence Board", EvidenceBoardTool.SetUp),
            ("Set Up Headset UI", HeadsetUiTool.SetUp),
            ("Set Up Occlusion", OcclusionSetupTool.SetUp),
            ("Set Up Briefing Detective", CharacterSetupTool.SetUp),
            ("Set Up Crime Replay Cast", ReplayCastTool.SetUp),
            ("Set Hand Tracking to Low", HandTrackingSetupTool.SetUp),
            ("Prepare Study Build", StudyBuildTool.Prepare),
        };

        [MenuItem("FYP/Set Up Everything (one click)", false, 0)]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string summary = RunAll();
            EditorUtility.DisplayDialog("Set Up Everything", summary, "OK");
        }

        /// <summary>Batch mode entry (Unity closed): same chain, summary in the log.</summary>
        public static void RunBatch() => Debug.Log("[Set Up Everything]\n" + RunAll());

        private static string RunAll()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            AssetDatabase.Refresh();   // new meshes / voice clips are imported before the tools look for them
            string stopped = null;
            List<string> messages;
            SetupDialogs.BeginCollecting();
            try
            {
                for (int i = 0; i < Steps.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("Set Up Everything", Steps[i].Name, i / (float)Steps.Length);
                    try
                    {
                        Steps[i].Run();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        stopped = "STOPPED at " + Steps[i].Name + ": " + e.Message;
                        break;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                messages = SetupDialogs.EndCollecting();
            }
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            // One line per step message; the study build report (READY / NOT READY) last and in full.
            var summary = new StringBuilder();
            foreach (string m in messages)
                summary.Append(m.StartsWith("Study build") ? "\n" + m : "- " + Shorten(m)).Append('\n');
            if (stopped != null) summary.Append('\n').Append(stopped).Append('\n');
            return summary.ToString();
        }

        private static string Shorten(string s)
        {
            s = s.Replace('\n', ' ');
            return s.Length <= 160 ? s : s.Substring(0, 157) + "...";
        }
    }
}
