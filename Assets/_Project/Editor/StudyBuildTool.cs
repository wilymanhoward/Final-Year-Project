using System.Collections.Generic;
using System.Text;
using FYP.Detective.Meta;
using FYP.Detective.Net;
using Oculus.Interaction.Locomotion;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Phase E: <c>FYP > Prepare Study Build</c> switches the debug settings off (status-label "Resumed" messages,
    /// zone debug visuals) and checks that the open scene is ready for a study build: case specs valid, both case
    /// scenes built and matching their definitions, grabbable clues networked, test content off, UI and networking
    /// present, locomotion off, boundary suppression and the INTERNET permission on, Main.unity in the build, not a
    /// development build. It never fixes the scene beyond the debug switches; it reports what to run instead.
    /// <c>FYP > Restore Debug Settings</c> switches the debug messages back on for development.
    /// </summary>
    public static class StudyBuildTool
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("FYP/Prepare Study Build")]
        public static void Prepare()
        {
            var report = new StringBuilder();
            int fails = 0, warns = 0;
            void Ok(string s) => report.Append("OK    ").Append(s).Append('\n');
            void Fail(string s) { fails++; report.Append("FAIL  ").Append(s).Append('\n'); }
            void Warn(string s) { warns++; report.Append("WARN  ").Append(s).Append('\n'); }

            var session = Object.FindFirstObjectByType<SessionManager>();
            if (session == null || session.gameObject.scene.path != ScenePath)
            {
                EditorUtility.DisplayDialog("Study build", "Open " + ScenePath + " first.", "OK");
                return;
            }

            // 1. Debug switches off (the only changes this tool makes).
            int labels = SetDebugMessages(false);
            int zoneVisuals = 0;
            foreach (var z in Object.FindObjectsByType<ExclusionZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!z.ShowDebugVisual) continue;
                Undo.RecordObject(z, "Study build");
                z.ShowDebugVisual = false;
                EditorUtility.SetDirty(z);
                zoneVisuals++;
            }
            Ok($"Debug messages off on {labels} status label(s); {zoneVisuals} zone debug visual(s) switched off");

            // 2. Case content.
            var specErrors = CaseSpecValidator.ValidatePair(CaseSpecs.X, CaseSpecs.Y);
            if (specErrors.Count == 0) Ok("Case specs pass the validator");
            else Fail("Case specs fail the validator: " + string.Join("; ", specErrors));

            foreach (var spec in new[] { CaseSpecs.X, CaseSpecs.Y })
                CheckCase(spec, Ok, Fail);

            // 3. Old test content.
            var caseRoot = Object.FindFirstObjectByType<CaseRoot>(FindObjectsInactive.Include);
            foreach (string name in new[] { "TestCube (delete me)", "Quad" })
            {
                var t = caseRoot != null ? caseRoot.transform.Find(name) : null;
                if (t != null && t.gameObject.activeSelf) Fail($"'{name}' is still active (run FYP > Build Case Scenes)");
            }

            // 4. UI, networking, rig.
            Require<SetupPanel>("Setup panel", "FYP > Set Up Headset UI", Ok, Fail);
            Require<ParticipantCard>("Participant card", "FYP > Set Up Headset UI", Ok, Fail);
            Require<EvidenceBoardView>("Evidence board", "FYP > Set Up Evidence Board", Ok, Fail);
            Require<ScenePlacement>("Scene placement", "FYP > Set Up Case Root and Placement", Ok, Fail);
            Require<ColocationCoordinator>("Colocation coordinator", "FYP > Set Up Networking", Ok, Fail);
            Require<TeamSessionSync>("Team session sync", "FYP > Set Up Networking", Ok, Fail);

            bool locomotionOn = false;
            foreach (var l in Object.FindObjectsByType<FirstPersonLocomotor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.gameObject.activeInHierarchy) locomotionOn = true;
            if (locomotionOn) Fail("ISDK locomotion is active (run FYP > Disable Locomotion)");
            else Ok("Locomotion off");

            var ovr = Object.FindFirstObjectByType<OVRManager>();
            if (ovr == null) Fail("No OVRManager in the scene");
            else if (!ovr.shouldBoundaryVisibilityBeSuppressed) Warn("Boundary suppression off (FYP > Suppress Boundary)");
            else Ok("Boundary suppression requested");

            if (PlayerSettings.Android.forceInternetPermission) Ok("Android INTERNET permission forced");
            else Fail("INTERNET permission not forced (run FYP > Set Up Networking)");

            // 5. Build settings.
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length > 0 && scenes[0].enabled && scenes[0].path == ScenePath) Ok("Main.unity is the first build scene");
            else Fail("Main.unity must be the first enabled scene in Build Profiles");
            if (EditorUserBuildSettings.development) Warn("Development Build is on - turn it off for the study build");
            else Ok("Not a development build");

            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            EditorSceneManager.SaveScene(session.gameObject.scene);

            string title = fails == 0 ? (warns == 0 ? "Ready for a study build" : "Ready, with warnings") : $"NOT ready ({fails} problem(s))";
            Debug.Log("[StudyBuild] " + title + "\n" + report);
            EditorUtility.DisplayDialog("Study build - " + title, report + "\nScene saved. (Full report in the Console.)", "OK");
        }

        [MenuItem("FYP/Restore Debug Settings")]
        public static void RestoreDebug()
        {
            int labels = SetDebugMessages(true);
            var session = Object.FindFirstObjectByType<SessionManager>();
            if (session != null)
            {
                EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
                EditorSceneManager.SaveScene(session.gameObject.scene);
            }
            EditorUtility.DisplayDialog("Debug settings", $"Debug messages on for {labels} status label(s). Scene saved.", "OK");
        }

        private static int SetDebugMessages(bool on)
        {
            int n = 0;
            foreach (var label in Object.FindObjectsByType<StatusLabel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(label, "Debug messages");
                label.ShowDebugMessages = on;
                EditorUtility.SetDirty(label);
                n++;
            }
            return n;
        }

        private static void CheckCase(CaseSpec spec, System.Action<string> ok, System.Action<string> fail)
        {
            string name = "Case_" + spec.Case;
            CaseContent content = null;
            foreach (var c in Object.FindObjectsByType<CaseContent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.CaseId == spec.Case) content = c;
            if (content == null) { fail($"{name} missing (run FYP > Build Case Scenes)"); return; }

            var clues = new Dictionary<string, Clue>();
            foreach (var clue in content.GetComponentsInChildren<Clue>(true)) clues[clue.ClueId] = clue;
            int problems = 0;
            foreach (var cs in spec.Clues)
            {
                if (!clues.TryGetValue(cs.Id, out Clue clue)) { fail($"{name}: clue {cs.Id} missing (rebuild the case scenes)"); problems++; continue; }
                if (clue.InspectText != cs.InspectText) { fail($"{name}: {cs.Id} text differs from the spec (rebuild)"); problems++; }
                if (!cs.Grabbable) continue;
                var netObj = clue.GetComponent<NetworkObject>();
                var netTransform = clue.GetComponent<NetworkTransform>();
                if (netObj == null || clue.GetComponent<NetworkClue>() == null || netTransform == null)
                {
                    fail($"{name}: {cs.Id} isn't networked (run FYP > Set Up Networking)");
                    problems++;
                }
                else if (netTransform.AuthorityMode != NetworkTransform.AuthorityModes.Owner || !netTransform.InLocalSpace)
                {
                    fail($"{name}: {cs.Id} NetworkTransform must be Owner authority + local space (run FYP > Set Up Networking)");
                    problems++;
                }
            }
            if (content.GetComponentsInChildren<ExclusionZone>(true).Length != spec.Zones.Count)
            {
                fail($"{name}: expected {spec.Zones.Count} zones (rebuild)");
                problems++;
            }

            string defPath = $"Assets/_Project/Cases/Case{spec.Case}/Case{spec.Case}_Definition.asset";
            var def = AssetDatabase.LoadAssetAtPath<CaseDefinition>(defPath);
            if (def == null) { fail($"{defPath} missing"); problems++; }
            else
            {
                var keys = spec.Clues.FindAll(c => PlayArea.IsKey(c.Slot)).ConvertAll(c => c.Id);
                bool keysMatch = def.KeyEvidenceIds.Count == keys.Count;
                foreach (var k in keys) keysMatch &= System.Linq.Enumerable.Contains(def.KeyEvidenceIds, k);
                if (def.CorrectSuspectId != spec.Culprit.Id || !keysMatch || def.Suspects.Count != spec.Suspects.Count)
                {
                    fail($"{name}: {defPath} doesn't match the spec (rebuild the case scenes)");
                    problems++;
                }
            }
            if (problems == 0) ok($"{name}: 7 clues (grabbables networked), {spec.Zones.Count} zones, definition matches");
        }

        private static void Require<T>(string what, string fix, System.Action<string> ok, System.Action<string> fail) where T : Object
        {
            if (Object.FindFirstObjectByType<T>(FindObjectsInactive.Include) != null) ok(what + " present");
            else fail(what + " missing (run " + fix + ")");
        }
    }
}
