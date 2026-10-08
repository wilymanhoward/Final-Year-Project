using FYP.Detective.Meta;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Adds CaseRoot and the placement system to the open scene (idempotent: running it again only fills
    /// in what is missing and re-wires references), then saves the scene.
    /// </summary>
    public static class PlacementSetupTool
    {
        private const string TestCubeName = "TestCube (delete me)";
        private const string LabelName = "ExperimenterStatusLabel";

        [MenuItem("FYP/Set Up Case Root and Placement")]
        public static void SetUp()
        {
            var session = Object.FindFirstObjectByType<SessionManager>();
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            var logger = Object.FindFirstObjectByType<DataLogger>();
            var panel = Object.FindFirstObjectByType<ExperimenterPanel>();
            if (session == null || rig == null || logger == null || panel == null || rig.Head == null)
            {
                EditorUtility.DisplayDialog("Placement",
                    "Open Main.unity first (it needs SessionManager, DataLogger, ExperimenterPanel and a PlayerRig with a head).", "OK");
                return;
            }

            // 1. CaseRoot at the origin (placement moves it at runtime).
            var caseRoot = Object.FindFirstObjectByType<CaseRoot>(FindObjectsInactive.Include);
            bool createdRoot = false;
            if (caseRoot == null)
            {
                caseRoot = new GameObject("CaseRoot").AddComponent<CaseRoot>();
                createdRoot = true;
            }

            // 2. Case content belongs under CaseRoot. For now that is only the test cube.
            var cube = GameObject.Find(TestCubeName);
            bool movedCube = cube != null && cube.transform.parent == null;
            if (movedCube) cube.transform.SetParent(caseRoot.transform, true);

            // 3. Placement + experimenter shortcuts on SessionSystems.
            var systems = session.gameObject;
            var placement = GetOrAdd<ScenePlacement>(systems);
            MainSceneBuilder.SetObject(placement, "session", session);
            MainSceneBuilder.SetObject(placement, "rig", rig);
            MainSceneBuilder.SetObject(placement, "caseRoot", caseRoot);
            var cameraRig = Object.FindFirstObjectByType<OVRCameraRig>();
            var trackingSpace = cameraRig != null ? MainSceneBuilder.FindDeep(cameraRig.transform, "TrackingSpace") : null;
            MainSceneBuilder.SetObject(placement, "trackingSpace", trackingSpace);

            var shortcuts = GetOrAdd<ExperimenterControllerShortcuts>(systems);
            MainSceneBuilder.SetObject(shortcuts, "placement", placement);
            MainSceneBuilder.SetObject(shortcuts, "panel", panel);

            // 4. Logger writes CaseRoot-local positions.
            MainSceneBuilder.SetObject(logger, "caseRoot", caseRoot);

            // 5. Status label in front of the user, fed by placement and session shortcuts.
            var label = Object.FindFirstObjectByType<StatusLabel>(FindObjectsInactive.Include);
            if (label == null) label = CreateLabel();
            MainSceneBuilder.SetObject(label, "head", rig.Head);
            AddListenerOnce(placement.onStatusChanged, label);
            AddListenerOnce(shortcuts.onFeedback, label);

            EditorUtility.SetDirty(placement);
            EditorUtility.SetDirty(shortcuts);
            var scene = session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorUtility.DisplayDialog("Placement",
                (createdRoot ? "Created CaseRoot.\n" : "CaseRoot already present.\n") +
                (movedCube ? "Moved the test cube under CaseRoot.\n" : "") +
                (trackingSpace == null ? "WARNING: TrackingSpace not found; floor height defaults to y = 0.\n" : "") +
                "ScenePlacement, ExperimenterControllerShortcuts and the status label are wired. Scene saved.", "OK");
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void AddListenerOnce(UnityEvent<string> evt, StatusLabel label)
        {
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (evt.GetPersistentTarget(i) == label && evt.GetPersistentMethodName(i) == nameof(StatusLabel.Show)) return;
            }
            UnityEventTools.AddPersistentListener(evt, label.Show);
        }

        /// <summary>World-space canvas (0.8 x 0.22 m) with a dark background and white text.</summary>
        private static StatusLabel CreateLabel()
        {
            var go = new GameObject(LabelName, typeof(RectTransform), typeof(Canvas), typeof(Image));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(800f, 220f);
            rect.localScale = Vector3.one * 0.001f;   // 1 canvas unit = 1 mm
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 10f);
            textRect.offsetMax = new Vector2(-20f, -10f);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 44;   // ~4.4 cm cap height at 1 mm per unit
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = "Status";

            var label = go.AddComponent<StatusLabel>();
            MainSceneBuilder.SetObject(label, "text", text);
            MainSceneBuilder.SetObject(label, "canvas", canvas);
            return label;
        }
    }
}
