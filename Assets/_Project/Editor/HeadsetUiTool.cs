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

            PlacementSetupTool.StyleLabel(Object.FindFirstObjectByType<StatusLabel>(FindObjectsInactive.Include));   // same look as the panel
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

        // ---- Setup panel (session 6 redesign): header with codes + run-state chip, four section cards ----
        // Cards in the order the experimenter works: 1 Participant, 2 Placement, 3 Network & data, 4 Run. Each card has
        // a traffic light (SetupStatusModel), its status text and its buttons; rounded shapes use Unity's built-in
        // "UI/Skin/UISprite.psd" (sliced) and "UI/Skin/Knob.psd" sprites.

        private static readonly Color HeaderColor = new Color(0.09f, 0.12f, 0.19f, 0.97f);
        private static readonly Color CardColor = new Color(0.13f, 0.15f, 0.20f, 0.97f);
        private static readonly Color MutedText = new Color(0.62f, 0.68f, 0.78f, 1f);
        private static readonly Color BodyText = new Color(0.86f, 0.89f, 0.94f, 1f);
        private static readonly Color NeutralButton = new Color(0.22f, 0.27f, 0.36f, 1f);
        private static readonly Color ArmColor = new Color(0.12f, 0.50f, 0.30f, 1f);
        private static readonly Color StartNowColor = new Color(0.15f, 0.38f, 0.70f, 1f);
        private static readonly Color AbortColor = new Color(0.62f, 0.16f, 0.14f, 1f);

        private static SetupPanel BuildSetupPanel(SessionManager session, ExperimenterPanel panel, Transform head,
            ScenePlacement placement, ColocationCoordinator coordinator)
        {
            const float width = 720f, height = 560f, gap = 14f, headerH = 72f;
            const float cardW = (width - 3 * gap) / 2f, cardH = (height - headerH - 3 * gap) / 2f;
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var root = new GameObject(PanelName);
            GameObject content = Canvas(root.transform, width, height, new Color(0.05f, 0.06f, 0.09f, 0.94f));
            Round(content.GetComponent<Image>(), rounded);

            var setup = root.AddComponent<SetupPanel>();
            MainSceneBuilder.SetObject(setup, "session", session);
            MainSceneBuilder.SetObject(setup, "panel", panel);
            MainSceneBuilder.SetObject(setup, "head", head);
            MainSceneBuilder.SetObject(setup, "content", content);
            MainSceneBuilder.SetObject(setup, "statusText", null);
            var setupSo = new SerializedObject(setup);
            setupSo.FindProperty("distance").floatValue = 0.55f;
            setupSo.ApplyModifiedPropertiesWithoutUndo();
            var hint = Object.FindFirstObjectByType<StatusLabel>(FindObjectsInactive.Include);
            if (hint != null) MainSceneBuilder.SetObject(setup, "hintLabel", hint);

            // Header: what this run is + its state.
            var header = Panel(content.transform, "Header", new Rect(0f, 0f, width, headerH), HeaderColor, rounded);
            Text kicker = Label(header, "Kicker", new Rect(20f, 8f, 400f, 20f), 13, TextAnchor.MiddleLeft);
            kicker.text = "EXPERIMENTER SETUP   (menu button: close)";
            kicker.color = MutedText;
            Text headerText = Label(header, "Codes", new Rect(20f, 28f, 500f, 38f), 24, TextAnchor.MiddleLeft);
            headerText.fontStyle = FontStyle.Bold;
            var chip = Panel(header, "Chip", new Rect(width - 20f - 190f, 16f, 190f, 40f), SetupStatusModel.ColorOf(StatusLight.Ok), rounded);
            Text chipText = Label(chip, "Text", new Rect(0f, 0f, 190f, 40f), 17, TextAnchor.MiddleCenter);
            chipText.fontStyle = FontStyle.Bold;
            MainSceneBuilder.SetObject(setup, "headerText", headerText);
            MainSceneBuilder.SetObject(setup, "chipText", chipText);
            MainSceneBuilder.SetObject(setup, "chipImage", chip.GetComponent<Image>());

            float top = headerH + gap;
            Transform Card(int index, string title, string textField, string lightField, out Text status, float statusH)
            {
                float x = gap + (index % 2) * (cardW + gap), y = top + (index / 2) * (cardH + gap);
                var card = Panel(content.transform, "Card " + (index + 1) + " " + title, new Rect(x, y, cardW, cardH), CardColor, rounded);
                var light = Panel(card, "Light", new Rect(14f, 12f, 16f, 16f), SetupStatusModel.ColorOf(StatusLight.Off), knob);
                Text t = Label(card, "Title", new Rect(38f, 6f, cardW - 52f, 28f), 15, TextAnchor.MiddleLeft);
                t.text = (index + 1) + "   " + title;
                t.fontStyle = FontStyle.Bold;
                status = Label(card, "Status", new Rect(14f, 36f, cardW - 28f, statusH), 13, TextAnchor.UpperLeft);
                status.color = BodyText;
                MainSceneBuilder.SetObject(setup, textField, status);
                if (lightField != null) MainSceneBuilder.SetObject(setup, lightField, light.GetComponent<Image>());
                return card;
            }

            const float bh = 44f, bgap = 8f;
            float row1 = cardH - 14f - 2 * bh - bgap, row2 = cardH - 14f - bh;
            float w3 = (cardW - 28f - 2 * bgap) / 3f, w2 = (cardW - 28f - bgap) / 2f;
            void Buttons(Transform card, float y, float w, params (string label, UnityAction action, Color color)[] buttons)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i].action == null) continue;
                    var b = Button(card, buttons[i].label, new Rect(14f + i * (w + bgap), y, w, bh), buttons[i].action, buttons[i].color);
                    Round(b.GetComponent<Image>(), rounded);
                    b.GetComponentInChildren<Text>().fontSize = 15;
                }
            }

            var participant = Card(0, "PARTICIPANT", "participantText", null, out _, row1 - 44f);
            Buttons(participant, row1, w3, ("Pair -", panel.PreviousPair, NeutralButton), ("Pair +", panel.NextPair, NeutralButton),
                ("Case X / Y", panel.ToggleCase, NeutralButton));
            Buttons(participant, row2, w3, ("Participant -", panel.PreviousParticipant, NeutralButton),
                ("Participant +", panel.NextParticipant, NeutralButton), ("Condition A / B", panel.ToggleCondition, NeutralButton));
            Object.DestroyImmediate(participant.Find("Light").gameObject);   // codes can't be "wrong": no light

            var place = Card(1, "PLACEMENT & VIEW", "placementText", "placementLight", out _, row1 - 44f);
            Buttons(place, row2, w3, ("Place here (A)", placement.PlaceSceneHere, NeutralButton),
                ("Load saved (B)", placement.LoadSavedPlacement, NeutralButton), ("Manual (X)", placement.ManualPlacement, NeutralButton));
            // Depth API occlusion on/off (session 6): label and colour updated by SetupPanel from the saved setting.
            var occlusion = Button(place, "Occlusion", new Rect(14f, row1, cardW - 28f, bh), panel.ToggleOcclusion, ArmColor);
            Round(occlusion.GetComponent<Image>(), rounded);
            var occlusionText = occlusion.GetComponentInChildren<Text>();
            occlusionText.fontSize = 15;
            occlusionText.text = "Occlusion: ON";
            MainSceneBuilder.SetObject(setup, "occlusionLabel", occlusionText);

            var network = Card(2, "NETWORK & DATA", "networkText", "networkLight", out _, row2 - 44f);
            var dataLight = Panel(network, "DataLight", new Rect(cardW - 30f, 12f, 16f, 16f), SetupStatusModel.ColorOf(StatusLight.Off), knob);
            MainSceneBuilder.SetObject(setup, "dataLight", dataLight.GetComponent<Image>());
            Text dataTag = Label(network, "DataTag", new Rect(cardW - 90f, 6f, 56f, 28f), 12, TextAnchor.MiddleRight);
            dataTag.text = "data";
            dataTag.color = MutedText;
            Buttons(network, row2, w3, ("Host", coordinator != null ? coordinator.Host : (UnityAction)null, NeutralButton),
                ("Join", coordinator != null ? coordinator.Join : (UnityAction)null, NeutralButton),
                ("Stop network", coordinator != null ? coordinator.Stop : (UnityAction)null, NeutralButton));

            var run = Card(3, "RUN", "runText", "runLight", out _, row1 - 44f);
            Buttons(run, row1, w2, ("ARM", setup.Arm, ArmColor), ("Disarm", setup.Disarm, NeutralButton));
            Buttons(run, row2, w2, ("Start now", setup.StartNow, StartNowColor), ("Abort", setup.Abort, AbortColor));

            QuickActionsAPI.AddPokeCanvasInteraction(content);
            return setup;
        }

        private static Transform Panel(Transform parent, string name, Rect r, Color color, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, r);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null) Round(image, sprite);
            return go.transform;
        }

        private static void Round(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 0.6f;   // corner radius ~ 15 mm at 1 unit = 1 mm
        }

        // ---- Participant card (session 6 redesign, user: "like a detective"): a case-file folder ----
        // Manila folder with a "CASE FILE" tab, a typed sheet held by a paperclip, a red CONFIDENTIAL stamp, the case
        // title with a red rule, the text in ink, and stamp-like buttons. Same buttons / wiring as before.

        private static readonly Color Manila = new Color(0.80f, 0.66f, 0.42f, 1f);
        private static readonly Color ManilaDark = new Color(0.60f, 0.47f, 0.27f, 1f);
        private static readonly Color Sheet = new Color(0.98f, 0.96f, 0.89f, 1f);
        private static readonly Color Ink = new Color(0.13f, 0.12f, 0.14f, 1f);
        private static readonly Color StampRed = new Color(0.76f, 0.10f, 0.08f, 1f);
        private static readonly Color InkBlue = new Color(0.14f, 0.24f, 0.46f, 1f);

        private static ParticipantCard BuildParticipantCard(SessionManager session, Transform head)
        {
            // Big enough for a case briefing (~350 characters) plus the instructions.
            const float width = 560f, height = 470f;
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var root = new GameObject(CardName);
            GameObject content = Canvas(root.transform, width, height, new Color(0f, 0f, 0f, 0f));
            Transform c = content.transform;

            // Folder: tab, back cover, the sheet inside, a paperclip.
            Panel(c, "FolderShadow", new Rect(8f, 52f, width - 8f, height - 52f), new Color(0f, 0f, 0f, 0.35f), rounded);
            var tab = Panel(c, "Tab", new Rect(20f, 6f, 210f, 60f), Manila, rounded);
            Text tabText = Label(tab, "Text", new Rect(0f, 4f, 210f, 40f), 20, TextAnchor.MiddleCenter);
            tabText.text = "CASE FILE";
            tabText.fontStyle = FontStyle.Bold;
            tabText.color = ManilaDark;
            Panel(c, "Folder", new Rect(0f, 44f, width - 8f, height - 52f), Manila, rounded);
            Text caseNo = Label(c, "CaseNumber", new Rect(width - 230f, 50f, 200f, 26f), 14, TextAnchor.MiddleRight);
            caseNo.text = "No. " + session.Config.CaseCode + "-01";
            caseNo.color = ManilaDark;
            var sheet = Panel(c, "Sheet", new Rect(22f, 78f, width - 52f, height - 100f), Sheet, null);
            ((RectTransform)sheet).localRotation = Quaternion.Euler(0f, 0f, -0.8f);
            var clipOuter = Panel(c, "PaperclipOuter", new Rect(width - 112f, 60f, 30f, 78f), new Color(0.62f, 0.64f, 0.68f, 1f), rounded);
            Panel(clipOuter, "Inner", new Rect(5f, 5f, 20f, 60f), Sheet, rounded);

            // CONFIDENTIAL stamp (decoration; drawn under the text).
            var stamp = Panel(c, "Stamp", new Rect(width - 250f, 96f, 200f, 46f), new Color(StampRed.r, StampRed.g, StampRed.b, 0.12f), rounded);
            ((RectTransform)stamp).localRotation = Quaternion.Euler(0f, 0f, 9f);
            Text stampText = Label(stamp, "Text", new Rect(0f, 0f, 200f, 46f), 22, TextAnchor.MiddleCenter);
            stampText.text = "CONFIDENTIAL";
            stampText.fontStyle = FontStyle.Bold;
            stampText.color = new Color(StampRed.r, StampRed.g, StampRed.b, 0.75f);

            // Typed title, red rule, body.
            Text title = Label(c, "Title", new Rect(44f, 96f, width - 300f, 46f), 24, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.color = Ink;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 16;
            title.resizeTextMaxSize = 24;
            Panel(c, "Rule", new Rect(44f, 148f, width - 104f, 3f), StampRed, null);
            Text body = Label(c, "Body", new Rect(44f, 160f, width - 104f, height - 160f - 96f), 15, TextAnchor.UpperLeft);
            body.color = Ink;

            var card = root.AddComponent<ParticipantCard>();
            GameObject Stamp(string label, Rect r, UnityAction action, Color color, int size)
            {
                var b = Button(c, label, r, action, color);
                Round(b.GetComponent<Image>(), rounded);
                var t = b.GetComponentInChildren<Text>();
                t.fontStyle = FontStyle.Bold;
                t.fontSize = size;
                return b;
            }
            float buttonsY = height - 22f - 66f;
            GameObject start = Stamp("OPEN THE CASE  (" + ParticipantCardModel.StartLabel + ")", new Rect((width - 280f) / 2f + 40f, buttonsY, 280f, 66f),
                card.PressStart, StampRed, 18);
            GameObject begin = Stamp(ParticipantCardModel.BeginLabel, new Rect((width - 260f) / 2f + 40f, buttonsY, 260f, 66f),
                card.PressBegin, InkBlue, 20);
            GameObject replay = Stamp(ParticipantCardModel.ReplayLabel, new Rect(40f, buttonsY + 8f, 120f, 54f), card.PressReplay, ManilaDark, 17);
            begin.SetActive(false);
            replay.SetActive(false);

            MainSceneBuilder.SetObject(card, "session", session);
            MainSceneBuilder.SetObject(card, "head", head);
            MainSceneBuilder.SetObject(card, "content", content);
            MainSceneBuilder.SetObject(card, "title", title);
            MainSceneBuilder.SetObject(card, "body", body);
            MainSceneBuilder.SetObject(card, "startButton", start);
            MainSceneBuilder.SetObject(card, "beginButton", begin);
            MainSceneBuilder.SetObject(card, "replayButton", replay);
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
