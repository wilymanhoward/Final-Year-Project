using Oculus.Interaction.Editor.QuickActions;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Phase C2: builds the fixed, pokeable evidence board (EvidenceBoard + EvidenceBoardView) under CaseRoot.
    /// Idempotent: an existing board is rebuilt in the same place (so it can be moved in the scene and keeps that
    /// pose). Default pose = the case specs' spot (back of the room, facing the start, centre 1.30 m high).
    /// Look (session 6, user: "like an actual evidence board"): a wall-hung cork board in a wooden frame (mesh
    /// "EvidenceBoard" from tools/maya/build_props.py, always visible) with the run's cards in front of it on a
    /// transparent canvas (units = mm, 1100 x 740): 4 suspect Polaroids (photo silhouette, name / role / alibi,
    /// pushpin, "PRIME SUSPECT" stamp when chosen), 8 pinned index cards for the tagged clues ("PINNED" stamp), red
    /// strings from the chosen suspect to the chosen evidence, masking-tape headers, the summary on a notepad and a
    /// stamp-style Submit with Yes/No. The logic (EvidenceBoardModel / EvidenceBoardView / CaseState) is unchanged.
    /// Poke: QuickActionsAPI.AddPokeCanvasInteraction (Interaction SDK 207, Editor/QuickActions/Scripts/QuickActionsAPI.cs).
    /// Round shapes use Unity's built-in "UI/Skin/Knob.psd" sprite (AssetDatabase.GetBuiltinExtraResource).
    /// </summary>
    public static class EvidenceBoardTool
    {
        private const string BoardName = "EvidenceBoard";
        public const string BoardMesh = "EvidenceBoard";
        private const int SuspectSlots = 4;   // docs/cases.md: 4 suspect cards (name, role, alibi)
        private const int EvidenceSlots = 8;
        // Default = the case specs' board spot: back of the room, centre 1.30 m high (board 0.82-1.78 m), readable
        // side facing the start.
        internal static readonly Vector3 DefaultLocalPosition = new Vector3(CaseSpecs.X.BoardX, 1.30f, CaseSpecs.X.BoardZ);
        internal static readonly Quaternion DefaultLocalRotation = CaseSceneTool.FacingRotation(CaseSpecs.X.BoardYaw);

        private const float Width = 1100f, Height = 740f;
        private static readonly Color Ink = new Color(0.12f, 0.12f, 0.16f, 1f);
        private static readonly Color Tape = new Color(0.93f, 0.88f, 0.62f, 0.95f);
        private static readonly Color Polaroid = new Color(0.97f, 0.96f, 0.92f, 1f);
        private static readonly Color IndexCard = new Color(0.98f, 0.95f, 0.84f, 1f);
        private static readonly Color Selected = new Color(1f, 0.86f, 0.42f, 1f);
        private static readonly Color StampRed = new Color(0.78f, 0.08f, 0.08f, 1f);
        private static readonly Color StringRed = new Color(0.80f, 0.06f, 0.06f, 1f);
        private static readonly Color SubmitColor = new Color(0.62f, 0.12f, 0.10f, 1f);
        private static readonly Color DisabledColor = new Color(0.45f, 0.42f, 0.40f, 1f);
        private static readonly Color YesColor = new Color(0.12f, 0.42f, 0.22f, 1f);
        private static readonly Color NoColor = new Color(0.40f, 0.40f, 0.42f, 1f);
        private static readonly Color[] PhotoTints =
        {
            new Color(0.30f, 0.36f, 0.42f), new Color(0.38f, 0.32f, 0.29f), new Color(0.29f, 0.38f, 0.33f), new Color(0.37f, 0.32f, 0.40f),
        };
        private static readonly Color[] PinColors =
        {
            new Color(0.85f, 0.10f, 0.10f), new Color(0.15f, 0.35f, 0.85f), new Color(0.20f, 0.65f, 0.25f), new Color(0.95f, 0.80f, 0.10f),
        };
        private static readonly float[] SuspectTilt = { -2.5f, 1.5f, -1f, 2.5f };
        private static readonly float[] EvidenceTilt = { 1.5f, -1f, 2f, -2f, -1.5f, 1f, -2.5f, 1.5f };

        [MenuItem("FYP/Set Up Evidence Board")]
        public static void SetUp()
        {
            var session = Object.FindFirstObjectByType<SessionManager>();
            var caseRoot = Object.FindFirstObjectByType<CaseRoot>(FindObjectsInactive.Include);
            if (session == null || caseRoot == null)
            {
                EditorUtility.DisplayDialog("Evidence board", "Open Main.unity and run FYP > Set Up Case Root and Placement first.", "OK");
                return;
            }

            Vector3 localPos = DefaultLocalPosition;
            Quaternion localRot = DefaultLocalRotation;
            var existing = Object.FindFirstObjectByType<EvidenceBoardView>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (existing.transform.parent == caseRoot.transform)
                {
                    localPos = existing.transform.localPosition;
                    localRot = existing.transform.localRotation;
                }
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var root = new GameObject(BoardName);
            root.transform.SetParent(caseRoot.transform, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;
            var board = root.AddComponent<EvidenceBoard>();
            MainSceneBuilder.SetObject(board, "session", session);
            var view = root.AddComponent<EvidenceBoardView>();

            // The physical cork board (always visible; the cards below only during a run).
            bool hasMesh = PropAssets.Missing(new[] { BoardMesh }).Count == 0;
            if (hasMesh) PropAssets.AddVisual(root.transform, BoardMesh, Vector3.zero, Quaternion.identity, Vector3.one);

            GameObject content = HeadsetUiTool.Canvas(root.transform, Width, Height, new Color(0f, 0f, 0f, 0f));
            Transform c = content.transform;
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            TapeLabel(c, "Title", new Rect(410f, 8f, 280f, 46f), -1.5f, "CASE BOARD", 26);
            TapeLabel(c, "SuspectsHeader", new Rect(28f, 16f, 300f, 38f), 2f, "SUSPECTS - pick one", 17);

            // Suspects: Polaroids along the top.
            var suspects = new GameObject[SuspectSlots];
            var suspectPins = new RectTransform[SuspectSlots];
            for (int i = 0; i < SuspectSlots; i++)
            {
                var r = new Rect(40f + i * 262f, 72f, 240f, 305f);
                suspects[i] = Card(c, "Suspect " + (i + 1), r, SuspectTilt[i], Polaroid);
                UnityEventTools.AddIntPersistentListener(suspects[i].GetComponent<Button>().onClick, view.PressSuspect, i);
                Transform t = suspects[i].transform;
                CardText(t, new Rect(14f, 168f, 212f, 130f), 13, TextAnchor.UpperLeft);
                Img(t, "Photo", new Rect(14f, 30f, 212f, 130f), PhotoTints[i], null);
                Img(t, "Head", new Rect(94f, 50f, 52f, 60f), new Color(0.13f, 0.14f, 0.16f), knob);
                Img(t, "Shoulders", new Rect(58f, 104f, 124f, 56f), new Color(0.13f, 0.14f, 0.16f), knob);
                Img(t, "Shadow", new Rect(4f, 302f, 236f, 6f), new Color(0f, 0f, 0f, 0.18f), null);
                suspectPins[i] = Img(t, "Pin", new Rect(111f, -7f, 18f, 18f), PinColors[0], knob);
                Stamp(t, new Rect(6f, 70f, 228f, 56f), -12f, "PRIME SUSPECT", 24);
            }

            // Evidence: pinned index cards (the run's tagged clues), 4 x 2.
            TapeLabel(c, "EvidenceHeader", new Rect(28f, 392f, 470f, 38f), -1.5f, "EVIDENCE - your tagged clues, pin the ones that prove it", 15);
            var evidence = new GameObject[EvidenceSlots];
            var evidencePins = new RectTransform[EvidenceSlots];
            for (int i = 0; i < EvidenceSlots; i++)
            {
                int col = i % 4, row = i / 4;
                var r = new Rect(40f + col * 204f, 444f + row * 112f, 190f, 96f);
                evidence[i] = Card(c, "Evidence " + (i + 1), r, EvidenceTilt[i], IndexCard);
                UnityEventTools.AddIntPersistentListener(evidence[i].GetComponent<Button>().onClick, view.PressEvidence, i);
                Transform t = evidence[i].transform;
                CardText(t, new Rect(10f, 26f, 170f, 66f), 15, TextAnchor.MiddleCenter);
                Img(t, "Rule", new Rect(8f, 20f, 174f, 2f), new Color(0.85f, 0.35f, 0.35f), null);
                Img(t, "Shadow", new Rect(4f, 94f, 186f, 5f), new Color(0f, 0f, 0f, 0.18f), null);
                evidencePins[i] = Img(t, "Pin", new Rect(86f, -7f, 18f, 18f), PinColors[(i + 1) % PinColors.Length], knob);
                Stamp(t, new Rect(92f, 58f, 96f, 32f), -10f, "PINNED", 18);
            }

            // Red strings (positioned by EvidenceBoardView), above the cards.
            var strings = new RectTransform[EvidenceSlots];
            for (int i = 0; i < EvidenceSlots; i++)
            {
                var go = new GameObject("String " + (i + 1), typeof(RectTransform), typeof(Image));
                go.transform.SetParent(c, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                var img = go.GetComponent<Image>();
                img.color = StringRed;
                img.raycastTarget = false;
                go.SetActive(false);
                strings[i] = rt;
            }

            // Notepad with the selection summary, Submit (a red stamp) and the confirmation.
            var pad = Img(c, "Notepad", new Rect(868f, 392f, 204f, 180f), new Color(0.99f, 0.96f, 0.70f), null);
            pad.localRotation = Quaternion.Euler(0f, 0f, 1.5f);
            Img(pad, "Binding", new Rect(0f, 0f, 204f, 14f), new Color(0.70f, 0.18f, 0.16f), null);
            Text summary = HeadsetUiTool.Label(pad, "Summary", new Rect(12f, 22f, 180f, 152f), 13, TextAnchor.UpperLeft);
            summary.color = Ink;

            GameObject submit = HeadsetUiTool.Button(c, "Submit answer", new Rect(868f, 592f, 204f, 84f), view.PressSubmit, SubmitColor);
            var submitText = submit.GetComponentInChildren<Text>();
            submitText.text = "SUBMIT\nANSWER";
            submitText.fontSize = 20;
            submitText.fontStyle = FontStyle.Bold;

            var confirm = Img(c, "Confirm", new Rect(856f, 562f, 228f, 170f), Polaroid, null).gameObject;
            var question = HeadsetUiTool.Label(confirm.transform, "Question", new Rect(10f, 8f, 208f, 56f), 15, TextAnchor.MiddleCenter);
            question.text = "Submit this answer?\nIt ends the case.";
            question.color = Ink;
            HeadsetUiTool.Button(confirm.transform, "Yes, submit", new Rect(8f, 76f, 102f, 84f), view.ConfirmYes, YesColor);
            HeadsetUiTool.Button(confirm.transform, "No", new Rect(118f, 76f, 102f, 84f), view.ConfirmNo, NoColor);
            confirm.SetActive(false);

            var so = new SerializedObject(view);
            so.FindProperty("board").objectReferenceValue = board;
            so.FindProperty("content").objectReferenceValue = content;
            so.FindProperty("summary").objectReferenceValue = summary;
            so.FindProperty("submitButton").objectReferenceValue = submit;
            so.FindProperty("confirmGroup").objectReferenceValue = confirm;
            so.FindProperty("selectedColor").colorValue = Selected;
            so.FindProperty("submitColor").colorValue = SubmitColor;
            so.FindProperty("disabledColor").colorValue = DisabledColor;
            SetArray(so.FindProperty("suspectButtons"), suspects);
            SetArray(so.FindProperty("evidenceButtons"), evidence);
            SetArray(so.FindProperty("suspectPins"), suspectPins);
            SetArray(so.FindProperty("evidencePins"), evidencePins);
            SetArray(so.FindProperty("strings"), strings);
            so.ApplyModifiedPropertiesWithoutUndo();

            QuickActionsAPI.AddPokeCanvasInteraction(content);

            var scene = session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeObject = root;
            EditorUtility.DisplayDialog("Evidence board",
                "Built the evidence board under CaseRoot (" + (existing != null ? "kept its pose" : "default pose: back of the room, facing the start") +
                "). Move 'EvidenceBoard' in the scene if needed - rerunning keeps its pose (Build Case Scenes resets it to the spec spot).\n" +
                (hasMesh ? "" : "WARNING: EvidenceBoard.fbx missing - run mayapy tools/maya/build_props.py for the cork board.\n") +
                "Scene saved. Rebuild to apply.", "OK");
        }

        // ---- Pieces (top-left based rects in mm, like HeadsetUiTool) ----

        /// <summary>A pokeable paper card: Image + Button, its first child "Text" is the label the view fills in.</summary>
        private static GameObject Card(Transform parent, string name, Rect r, float tilt, Color paper)
        {
            GameObject card = HeadsetUiTool.Button(parent, name, r, null, paper);
            card.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            return card;
        }

        private static void CardText(Transform card, Rect r, int size, TextAnchor anchor)
        {
            var text = card.GetComponentInChildren<Text>(true);   // the label made by HeadsetUiTool.Button (first child)
            HeadsetUiTool.Place((RectTransform)text.transform, r);
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Ink;
            text.text = "";
        }

        private static RectTransform Img(Transform parent, string name, Rect r, Color color, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            HeadsetUiTool.Place(rt, r);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return rt;
        }

        private static void TapeLabel(Transform parent, string name, Rect r, float tilt, string text, int size)
        {
            var tape = Img(parent, name, r, Tape, null);
            tape.localRotation = Quaternion.Euler(0f, 0f, tilt);
            var label = HeadsetUiTool.Label(tape, "Text", new Rect(0f, 0f, r.width, r.height), size, TextAnchor.MiddleCenter);
            label.text = text;
            label.color = Ink;
            label.fontStyle = FontStyle.Bold;
        }

        /// <summary>Red rubber-stamp text, shown by the view only while the card is chosen.</summary>
        private static void Stamp(Transform card, Rect r, float tilt, string text, int size)
        {
            var stamp = new GameObject("Stamp", typeof(RectTransform));
            stamp.transform.SetParent(card, false);
            var rt = (RectTransform)stamp.transform;
            HeadsetUiTool.Place(rt, r);
            rt.localRotation = Quaternion.Euler(0f, 0f, tilt);
            Img(stamp.transform, "Frame", new Rect(0f, 0f, r.width, r.height), new Color(StampRed.r, StampRed.g, StampRed.b, 0.15f), null);
            var label = HeadsetUiTool.Label(stamp.transform, "Text", new Rect(0f, 0f, r.width, r.height), size, TextAnchor.MiddleCenter);
            label.text = text;
            label.color = StampRed;
            label.fontStyle = FontStyle.Bold;
            stamp.SetActive(false);
        }

        private static void SetArray<T>(SerializedProperty array, T[] items) where T : Object
        {
            array.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
    }
}
