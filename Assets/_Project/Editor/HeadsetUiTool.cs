using FYP.Detective.Meta;
using FYP.Detective.Net;
using Oculus.Interaction.Editor.QuickActions;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Phase C1: builds the experimenter <see cref="SetupPanel"/> and the <see cref="ParticipantCard"/> as pokeable
    /// world-space canvases (canvas units = mm, buttons ≥ 4 cm) and wires them. Idempotent: an existing panel/card is
    /// deleted and rebuilt, so layout changes in this file apply on the next run.
    /// Poke: <c>QuickActionsAPI.AddPokeCanvasInteraction(GameObject)</c> (Interaction SDK 207,
    /// Editor/QuickActions/Scripts/QuickActionsAPI.cs), as for the tag buttons.
    /// </summary>
    public static class HeadsetUiTool
    {
        private const string PanelName = "SetupPanel";
        private const string CardName = "ParticipantCard";
        private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.9f);
        private static readonly Color ButtonColor = new Color(0.16f, 0.20f, 0.27f, 1f);
        private static readonly Color StartColor = new Color(0.10f, 0.45f, 0.25f, 1f);

        [MenuItem("FYP/Set Up Headset UI")]
        public static void SetUp()
        {
            var session = Object.FindFirstObjectByType<SessionManager>();
            var panel = Object.FindFirstObjectByType<ExperimenterPanel>();
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            var shortcuts = Object.FindFirstObjectByType<ExperimenterControllerShortcuts>();
            var placement = Object.FindFirstObjectByType<ScenePlacement>();
            var launcher = Object.FindFirstObjectByType<NetworkSessionLauncher>();
            var coordinator = Object.FindFirstObjectByType<ColocationCoordinator>();
            var team = Object.FindFirstObjectByType<TeamSessionSync>();
            if (session == null || panel == null || rig == null || rig.Head == null || shortcuts == null || placement == null)
            {
                EditorUtility.DisplayDialog("Headset UI",
                    "Open Main.unity and run FYP > Set Up Case Root and Placement first.", "OK");
                return;
            }

            DeleteExisting<SetupPanel>();
            DeleteExisting<ParticipantCard>();

            SetupPanel setup = BuildSetupPanel(session, panel, rig.Head, placement, coordinator);
            ParticipantCard card = BuildParticipantCard(session, rig.Head);

            // Status feeds and the menu button.
            AddStringListener(placement.onStatusChanged, setup, setup.SetPlacementStatus);
            if (launcher != null) AddStringListener(launcher.onStatusChanged, setup, setup.SetNetworkStatus);
            if (coordinator != null) AddStringListener(coordinator.onStatusChanged, setup, setup.SetNetworkStatus);
            if (team != null) AddStringListener(team.onStatusChanged, setup, setup.SetNetworkStatus);
            RemoveListenersTo<SetupPanel>(shortcuts.onMenuPressed);
            UnityEventTools.AddPersistentListener(shortcuts.onMenuPressed, setup.Toggle);
            EditorUtility.SetDirty(shortcuts);
            EditorUtility.SetDirty(placement);

            var scene = session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog("Headset UI",
                "Built SetupPanel (left menu button) and ParticipantCard.\n" +
                (coordinator == null ? "WARNING: no ColocationCoordinator - run FYP > Set Up Networking, then this again.\n" : "") +
                "Scene saved. Rebuild to apply.", "OK");
            Selection.activeObject = card.gameObject;
        }

        // ---- Setup panel: status on top, 4 x 4 buttons below ----

        private static SetupPanel BuildSetupPanel(SessionManager session, ExperimenterPanel panel, Transform head,
            ScenePlacement placement, ColocationCoordinator coordinator)
        {
            const float width = 640f, height = 470f, pad = 12f, bw = 146f, bh = 52f, gap = 8f;
            var root = new GameObject(PanelName);
            GameObject content = Canvas(root.transform, width, height, PanelColor);
            Text status = Label(content.transform, "Status", new Rect(pad, pad, width - 2 * pad, 200f), 15, TextAnchor.UpperLeft);

            var setup = root.AddComponent<SetupPanel>();
            MainSceneBuilder.SetObject(setup, "session", session);
            MainSceneBuilder.SetObject(setup, "panel", panel);
            MainSceneBuilder.SetObject(setup, "head", head);
            MainSceneBuilder.SetObject(setup, "content", content);
            MainSceneBuilder.SetObject(setup, "statusText", status);
            var hint = Object.FindFirstObjectByType<StatusLabel>(FindObjectsInactive.Include);
            if (hint != null) MainSceneBuilder.SetObject(setup, "hintLabel", hint);

            var rows = new (string label, UnityAction action)[][]
            {
                new (string, UnityAction)[] { ("Pair -", panel.PreviousPair), ("Pair +", panel.NextPair),
                    ("Participant -", panel.PreviousParticipant), ("Participant +", panel.NextParticipant) },
                new (string, UnityAction)[] { ("Case X / Y", panel.ToggleCase), ("Place here (A)", placement.PlaceSceneHere),
                    ("Load saved (B)", placement.LoadSavedPlacement), ("Manual (X)", placement.ManualPlacement) },
                new (string, UnityAction)[] { ("Host", coordinator != null ? coordinator.Host : (UnityAction)null),
                    ("Join", coordinator != null ? coordinator.Join : (UnityAction)null),
                    ("Stop network", coordinator != null ? coordinator.Stop : (UnityAction)null), ("Close", setup.Close) },
                new (string, UnityAction)[] { ("ARM", setup.Arm), ("Disarm", setup.Disarm),
                    ("Start now", setup.StartNow), ("Abort", setup.Abort) },
            };
            float top = pad + 200f + gap;
            for (int r = 0; r < rows.Length; r++)
            {
                for (int c = 0; c < rows[r].Length; c++)
                {
                    var (label, action) = rows[r][c];
                    if (action == null) continue;
                    var rect = new Rect(pad + c * (bw + gap), top + r * (bh + gap), bw, bh);
                    Button(content.transform, label, rect, action, r == 3 && c == 0 ? StartColor : ButtonColor);
                }
            }
            QuickActionsAPI.AddPokeCanvasInteraction(content);
            return setup;
        }

        // ---- Participant card: title, body, Start ----

        private static ParticipantCard BuildParticipantCard(SessionManager session, Transform head)
        {
            const float width = 420f, height = 280f, pad = 16f;
            var root = new GameObject(CardName);
            GameObject content = Canvas(root.transform, width, height, PanelColor);
            Text title = Label(content.transform, "Title", new Rect(pad, pad, width - 2 * pad, 40f), 26, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Text body = Label(content.transform, "Body", new Rect(pad, pad + 48f, width - 2 * pad, 140f), 16, TextAnchor.UpperCenter);

            var card = root.AddComponent<ParticipantCard>();
            GameObject start = Button(content.transform, ParticipantCardModel.StartLabel,
                new Rect((width - 180f) / 2f, height - pad - 64f, 180f, 64f), card.PressStart, StartColor);

            MainSceneBuilder.SetObject(card, "session", session);
            MainSceneBuilder.SetObject(card, "head", head);
            MainSceneBuilder.SetObject(card, "content", content);
            MainSceneBuilder.SetObject(card, "title", title);
            MainSceneBuilder.SetObject(card, "body", body);
            MainSceneBuilder.SetObject(card, "startButton", start);
            QuickActionsAPI.AddPokeCanvasInteraction(content);
            return card;
        }

        // ---- uGUI helpers (top-left based rects in mm) ----

        internal static GameObject Canvas(Transform parent, float width, float height, Color background)
        {
            var go = new GameObject("Content", typeof(RectTransform), typeof(Canvas), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one * 0.001f;   // 1 unit = 1 mm
            go.GetComponent<Image>().color = background;
            return go;
        }

        internal static Text Label(Transform parent, string name, Rect r, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, r);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        internal static GameObject Button(Transform parent, string label, Rect r, UnityAction action, Color color)
        {
            var go = new GameObject("Button - " + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, r);
            go.GetComponent<Image>().color = color;
            Text text = Label(go.transform, "Text", new Rect(0f, 0f, r.width, r.height), 17, TextAnchor.MiddleCenter);
            text.text = label;
            if (action != null) UnityEventTools.AddVoidPersistentListener(go.GetComponent<Button>().onClick, action);
            return go;
        }

        internal static void Place(RectTransform rect, Rect r)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(r.x, -r.y);
            rect.sizeDelta = new Vector2(r.width, r.height);
        }

        private static void DeleteExisting<T>() where T : Component
        {
            foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(c.gameObject);
        }

        private static void AddStringListener(UnityEvent<string> evt, Object target, UnityAction<string> call)
        {
            for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                // Drop listeners whose target was deleted with the old panel, and avoid duplicates.
                if (evt.GetPersistentTarget(i) == null) UnityEventTools.RemovePersistentListener(evt, i);
            }
            UnityEventTools.AddPersistentListener(evt, call);
        }

        private static void RemoveListenersTo<T>(UnityEvent evt) where T : Object
        {
            for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                Object target = evt.GetPersistentTarget(i);
                if (target == null || target is T) UnityEventTools.RemovePersistentListener(evt, i);
            }
        }
    }
}
