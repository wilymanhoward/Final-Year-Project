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

        // ---- Setup panel (session 6, "detective game" + user friendly): the experimenter's case-control desk ----
        // Dark leather desk with a brass rim, a manila header (run codes + state stamp), a NEXT banner that always says
        // what to do now (SetupStatusModel.NextStep; that step's card gets a brass frame), and four case-file cards with
        // red numbered stamps and wax-seal lights: 1 Participant (value + [-]/[+] / Switch), 2 Placement & View,
        // 3 Network & Data, 4 Run (ARM, Cancel ARM, Start now (skip briefing), Abort with a confirming second tap).
        // Rounded shapes: Unity's built-in "UI/Skin/UISprite.psd" (sliced) and "UI/Skin/Knob.psd".

        private static readonly Color DeskLeather = new Color(0.14f, 0.10f, 0.08f, 0.97f);
        private static readonly Color DeskBrass = new Color(0.72f, 0.56f, 0.28f, 1f);
        private static readonly Color DeskManila = new Color(0.86f, 0.73f, 0.48f, 1f);
        private static readonly Color DeskPaper = new Color(0.96f, 0.93f, 0.85f, 1f);
        private static readonly Color DeskInk = new Color(0.14f, 0.12f, 0.13f, 1f);
        private static readonly Color DeskMuted = new Color(0.42f, 0.36f, 0.30f, 1f);
        private static readonly Color DeskStamp = new Color(0.72f, 0.13f, 0.10f, 1f);
        private static readonly Color NeutralButton = new Color(0.50f, 0.39f, 0.24f, 1f);
        private static readonly Color ArmColor = new Color(0.16f, 0.45f, 0.27f, 1f);
        private static readonly Color StartNowColor = new Color(0.18f, 0.28f, 0.50f, 1f);
        private static readonly Color AbortColor = new Color(0.62f, 0.14f, 0.11f, 1f);

        private static SetupPanel BuildSetupPanel(SessionManager session, ExperimenterPanel panel, Transform head,
            ScenePlacement placement, ColocationCoordinator coordinator)
        {
            const float width = 780f, height = 650f, gap = 12f, headerH = 82f, nextH = 46f;
            float top = headerH + 8f + nextH + 10f;
            float cardW = (width - 3 * gap) / 2f, cardH = (height - top - 2 * gap) / 2f;
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var root = new GameObject(PanelName);
            GameObject content = Canvas(root.transform, width, height, DeskBrass);
            Round(content.GetComponent<Image>(), rounded);
            Panel(content.transform, "Leather", new Rect(5f, 5f, width - 10f, height - 10f), DeskLeather, rounded);

            var setup = root.AddComponent<SetupPanel>();
            MainSceneBuilder.SetObject(setup, "session", session);
            MainSceneBuilder.SetObject(setup, "panel", panel);
            MainSceneBuilder.SetObject(setup, "head", head);
            MainSceneBuilder.SetObject(setup, "content", content);
            MainSceneBuilder.SetObject(setup, "statusText", null);
            var setupSo = new SerializedObject(setup);
            setupSo.FindProperty("distance").floatValue = 0.58f;
            setupSo.ApplyModifiedPropertiesWithoutUndo();
            var hint = Object.FindFirstObjectByType<StatusLabel>(FindObjectsInactive.Include);
            if (hint != null) MainSceneBuilder.SetObject(setup, "hintLabel", hint);

            Text Txt(Transform parent, string name, Rect r, int size, TextAnchor anchor, Color color, bool bold = false)
            {
                var t = Label(parent, name, r, size, anchor);
                t.color = color;
                if (bold) t.fontStyle = FontStyle.Bold;
                return t;
            }

            // Header: manila folder strip with the run codes and a stamp-like state chip.
            var header = Panel(content.transform, "Header", new Rect(14f, 12f, width - 28f, headerH), DeskManila, rounded);
            Txt(header, "Kicker", new Rect(18f, 6f, 460f, 22f), 14, TextAnchor.MiddleLeft, DeskMuted, true).text =
                "CASE CONTROL  -  EXPERIMENTER ONLY   (menu button: close)";
            Text headerText = Txt(header, "Codes", new Rect(18f, 30f, 500f, 44f), 26, TextAnchor.MiddleLeft, DeskInk, true);
            var chip = Panel(header, "Chip", new Rect(width - 28f - 18f - 200f, 18f, 200f, 46f), SetupStatusModel.ColorOf(StatusLight.Ok), rounded);
            ((RectTransform)chip).localRotation = Quaternion.Euler(0f, 0f, -3f);
            Text chipText = Txt(chip, "Text", new Rect(0f, 0f, 200f, 46f), 19, TextAnchor.MiddleCenter, Color.white, true);
            MainSceneBuilder.SetObject(setup, "headerText", headerText);
            MainSceneBuilder.SetObject(setup, "chipText", chipText);
            MainSceneBuilder.SetObject(setup, "chipImage", chip.GetComponent<Image>());

            // NEXT banner: a paper strip with a red "NEXT" tag.
            var next = Panel(content.transform, "Next", new Rect(14f, headerH + 20f, width - 28f, nextH), DeskPaper, rounded);
            var nextTag = Panel(next, "Tag", new Rect(8f, 7f, 74f, nextH - 14f), DeskStamp, rounded);
            Txt(nextTag, "Text", new Rect(0f, 0f, 74f, nextH - 14f), 16, TextAnchor.MiddleCenter, Color.white, true).text = "NEXT";
            Text nextText = Txt(next, "Text", new Rect(94f, 0f, width - 28f - 104f, nextH), 16, TextAnchor.MiddleLeft, DeskInk, true);
            nextText.resizeTextForBestFit = true;   // the longest step (after a run) needs two smaller lines
            nextText.resizeTextMinSize = 11;
            nextText.resizeTextMaxSize = 16;
            MainSceneBuilder.SetObject(setup, "nextStepText", nextText);

            // Cards (case-file paper) with a frame behind each (brass when it is the next step).
            var frames = new Image[4];
            Transform Card(int index, string title, string textField, string lightField, Rect statusRect)
            {
                float x = gap + (index % 2) * (cardW + gap), y = top + (index / 2) * (cardH + gap);
                frames[index] = Panel(content.transform, "Frame " + (index + 1), new Rect(x - 4f, y - 4f, cardW + 8f, cardH + 8f),
                    new Color(0.30f, 0.23f, 0.18f, 1f), rounded).GetComponent<Image>();
                var card = Panel(content.transform, "Card " + (index + 1) + " " + title, new Rect(x, y, cardW, cardH), DeskPaper, rounded);
                var stamp = Panel(card, "Number", new Rect(10f, 8f, 30f, 30f), DeskStamp, knob);
                Txt(stamp, "Text", new Rect(0f, 0f, 30f, 30f), 17, TextAnchor.MiddleCenter, Color.white, true).text = (index + 1).ToString();
                Txt(card, "Title", new Rect(48f, 6f, cardW - 100f, 34f), 17, TextAnchor.MiddleLeft, DeskInk, true).text = title;
                if (lightField != null)
                {
                    var light = Panel(card, "Seal", new Rect(cardW - 36f, 10f, 24f, 24f), SetupStatusModel.ColorOf(StatusLight.Off), knob);
                    MainSceneBuilder.SetObject(setup, lightField, light.GetComponent<Image>());
                }
                if (textField != null)
                {
                    Text status = Txt(card, "Status", statusRect, 14, TextAnchor.UpperLeft, DeskInk);
                    MainSceneBuilder.SetObject(setup, textField, status);
                }
                return card;
            }

            const float bh = 44f;
            GameObject Btn(Transform card, string label, Rect r, UnityAction action, Color color, int size = 15)
            {
                if (action == null) return null;
                var b = Button(card, label, r, action, color);
                Round(b.GetComponent<Image>(), rounded);
                var t = b.GetComponentInChildren<Text>();
                t.fontSize = size;
                t.fontStyle = FontStyle.Bold;
                return b;
            }
            float inner = cardW - 28f;

            // 1 Participant: label | value | [-] [+] or [Switch].
            var who = Card(0, "PARTICIPANT", null, null, default);
            Text Row(Transform card, string label, float y, string field)
            {
                Txt(card, label + "Label", new Rect(14f, y, 104f, bh), 14, TextAnchor.MiddleLeft, DeskMuted, true).text = label;
                Text v = Txt(card, label + "Value", new Rect(118f, y, inner - 104f - 112f, bh), 18, TextAnchor.MiddleLeft, DeskInk, true);
                v.resizeTextForBestFit = true;
                v.resizeTextMinSize = 12;
                v.resizeTextMaxSize = 18;
                MainSceneBuilder.SetObject(setup, field, v);
                return v;
            }
            float r0 = 44f, rs = bh + 4f;   // four rows end 8 px above the card edge
            Row(who, "Pair", r0, "pairValue");
            Btn(who, "-", new Rect(cardW - 14f - 104f, r0, 50f, bh), panel.PreviousPair, NeutralButton, 20);
            Btn(who, "+", new Rect(cardW - 14f - 50f, r0, 50f, bh), panel.NextPair, NeutralButton, 20);
            Row(who, "Participant", r0 + rs, "participantValue");
            Btn(who, "-", new Rect(cardW - 14f - 104f, r0 + rs, 50f, bh), panel.PreviousParticipant, NeutralButton, 20);
            Btn(who, "+", new Rect(cardW - 14f - 50f, r0 + rs, 50f, bh), panel.NextParticipant, NeutralButton, 20);
            Row(who, "Case", r0 + 2 * rs, "caseValue");
            Btn(who, "Switch", new Rect(cardW - 14f - 104f, r0 + 2 * rs, 104f, bh), panel.ToggleCase, NeutralButton);
            Row(who, "Condition", r0 + 3 * rs, "conditionValue");
            Btn(who, "Switch", new Rect(cardW - 14f - 104f, r0 + 3 * rs, 104f, bh), panel.ToggleCondition, NeutralButton);

            // 2 Placement & View.
            float rowA = cardH - 12f - 2 * bh - 8f, rowB = cardH - 12f - bh;
            float w3 = (inner - 16f) / 3f, w2 = (inner - 8f) / 2f;
            var place = Card(1, "PLACEMENT & VIEW", "placementText", "placementLight", new Rect(14f, 44f, inner, rowA - 50f));
            var occlusion = Btn(place, "Occlusion", new Rect(14f, rowA, inner, bh), panel.ToggleOcclusion, NeutralButton);
            var occlusionText = occlusion.GetComponentInChildren<Text>();
            occlusionText.text = "Occlusion: OFF";
            MainSceneBuilder.SetObject(setup, "occlusionLabel", occlusionText);
            Btn(place, "Place here (A)", new Rect(14f, rowB, w3, bh), placement.PlaceSceneHere, NeutralButton, 14);
            Btn(place, "Load saved (B)", new Rect(14f + w3 + 8f, rowB, w3, bh), placement.LoadSavedPlacement, NeutralButton, 14);
            Btn(place, "Manual (X)", new Rect(14f + 2 * (w3 + 8f), rowB, w3, bh), placement.ManualPlacement, NeutralButton, 14);

            // 3 Network & Data (data seal next to the network seal).
            var network = Card(2, "NETWORK & DATA", "networkText", "networkLight", new Rect(14f, 44f, inner, rowB - 50f));
            var dataSeal = Panel(network, "DataSeal", new Rect(cardW - 66f, 10f, 24f, 24f), SetupStatusModel.ColorOf(StatusLight.Off), knob);
            MainSceneBuilder.SetObject(setup, "dataLight", dataSeal.GetComponent<Image>());
            Txt(network, "DataTag", new Rect(cardW - 120f, 8f, 50f, 28f), 12, TextAnchor.MiddleRight, DeskMuted, true).text = "data";
            Btn(network, "Host", new Rect(14f, rowB, w3, bh), coordinator != null ? coordinator.Host : (UnityAction)null, NeutralButton);
            Btn(network, "Join", new Rect(14f + w3 + 8f, rowB, w3, bh), coordinator != null ? coordinator.Join : (UnityAction)null, NeutralButton);
            Btn(network, "Stop network", new Rect(14f + 2 * (w3 + 8f), rowB, w3, bh), coordinator != null ? coordinator.Stop : (UnityAction)null,
                NeutralButton, 14);

            // 4 Run: ARM is the main action; Start now is clearly a fallback; Abort asks twice.
            var run = Card(3, "RUN", "runText", "runLight", new Rect(14f, 44f, inner, rowA - 50f));
            Btn(run, "ARM  -  show the briefing card", new Rect(14f, rowA, inner, bh), setup.Arm, ArmColor, 16);
            Btn(run, "Cancel ARM", new Rect(14f, rowB, w3, bh), setup.Disarm, NeutralButton, 14);
            Btn(run, "Start now (skip briefing)", new Rect(14f + w3 + 8f, rowB, w3, bh), setup.StartNow, StartNowColor, 12);
            var abort = Btn(run, "Abort run", new Rect(14f + 2 * (w3 + 8f), rowB, w3, bh), setup.Abort, AbortColor, 14);
            MainSceneBuilder.SetObject(setup, "abortLabel", abort.GetComponentInChildren<Text>());

            var so = new SerializedObject(setup);
            var framesProp = so.FindProperty("cardFrames");
            framesProp.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++) framesProp.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
            so.ApplyModifiedPropertiesWithoutUndo();

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
