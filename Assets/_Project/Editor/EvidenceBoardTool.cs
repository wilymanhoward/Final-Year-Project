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
    /// pose). Default pose: 1.3 m to the left of the placement mark, facing the centre, centre 1.35 m high.
    /// Canvas units = mm; 4 suspect cards (name, role, alibi), 8 evidence slots (2 columns), Submit with Yes/No.
    /// Poke: QuickActionsAPI.AddPokeCanvasInteraction (Interaction SDK 207, Editor/QuickActions/Scripts/QuickActionsAPI.cs).
    /// </summary>
    public static class EvidenceBoardTool
    {
        private const string BoardName = "EvidenceBoard";
        private const int SuspectSlots = 4;   // docs/cases.md: 4 suspect cards (name, role, alibi)
        private const int EvidenceSlots = 8;
        // Default = the case specs' board spot: back wall, centre 1.35 m high, readable side facing the start.
        internal static readonly Vector3 DefaultLocalPosition = new Vector3(CaseSpecs.X.BoardX, 1.35f, CaseSpecs.X.BoardZ);
        internal static readonly Quaternion DefaultLocalRotation = CaseSceneTool.FacingRotation(CaseSpecs.X.BoardYaw);
        private static readonly Color Background = new Color(0.05f, 0.06f, 0.08f, 0.92f);
        private static readonly Color SlotColor = new Color(0.16f, 0.20f, 0.27f, 1f);
        private static readonly Color SubmitColor = new Color(0.10f, 0.45f, 0.25f, 1f);
        private static readonly Color NoColor = new Color(0.45f, 0.15f, 0.15f, 1f);

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

            const float width = 1000f, height = 680f, pad = 16f, slotH = 56f, gap = 8f, cardH = 104f;
            GameObject content = HeadsetUiTool.Canvas(root.transform, width, height, Background);
            Text title = HeadsetUiTool.Label(content.transform, "Title", new Rect(pad, pad, width - 2 * pad, 44f), 28, TextAnchor.MiddleCenter);
            title.text = "Evidence board";
            title.fontStyle = FontStyle.Bold;

            // Suspects: one column on the left.
            float top = pad + 52f;
            const float suspectW = 440f;
            HeadsetUiTool.Label(content.transform, "SuspectsHeader", new Rect(pad, top, suspectW, 32f), 18, TextAnchor.MiddleLeft).text = "Suspect (choose one)";
            var suspects = new GameObject[SuspectSlots];
            for (int i = 0; i < SuspectSlots; i++)
            {
                var r = new Rect(pad, top + 40f + i * (cardH + gap), suspectW, cardH);
                suspects[i] = HeadsetUiTool.Button(content.transform, "Suspect " + (i + 1), r, null, SlotColor);
                var cardText = suspects[i].GetComponentInChildren<Text>();
                cardText.fontSize = 14;
                cardText.alignment = TextAnchor.MiddleLeft;
                HeadsetUiTool.Place((RectTransform)cardText.transform, new Rect(10f, 4f, suspectW - 20f, cardH - 8f));
                UnityEventTools.AddIntPersistentListener(suspects[i].GetComponent<Button>().onClick, view.PressSuspect, i);
            }

            // Evidence: two columns on the right.
            float evidenceX = pad + suspectW + 32f;
            float evidenceW = (width - evidenceX - pad - gap) / 2f;
            HeadsetUiTool.Label(content.transform, "EvidenceHeader", new Rect(evidenceX, top, 2 * evidenceW + gap, 32f), 18, TextAnchor.MiddleLeft)
                .text = "Evidence (your tagged clues - choose the ones that prove it)";
            var evidence = new GameObject[EvidenceSlots];
            for (int i = 0; i < EvidenceSlots; i++)
            {
                int col = i % 2, row = i / 2;
                var r = new Rect(evidenceX + col * (evidenceW + gap), top + 40f + row * (slotH + gap), evidenceW, slotH);
                evidence[i] = HeadsetUiTool.Button(content.transform, "Evidence " + (i + 1), r, null, SlotColor);
                UnityEventTools.AddIntPersistentListener(evidence[i].GetComponent<Button>().onClick, view.PressEvidence, i);
            }

            // Bottom: summary on the left, Submit (or the confirmation) on the right.
            float bottom = height - pad - 72f;
            Text summary = HeadsetUiTool.Label(content.transform, "Summary", new Rect(pad, bottom - 20f, width - 2 * pad - 300f, 92f), 16, TextAnchor.UpperLeft);
            GameObject submit = HeadsetUiTool.Button(content.transform, "Submit answer", new Rect(width - pad - 280f, bottom, 280f, 72f), view.PressSubmit, SubmitColor);

            var confirm = new GameObject("Confirm", typeof(RectTransform));
            confirm.transform.SetParent(content.transform, false);
            HeadsetUiTool.Place((RectTransform)confirm.transform, new Rect(width - pad - 300f, bottom - 40f, 300f, 112f));
            HeadsetUiTool.Label(confirm.transform, "Question", new Rect(0f, 0f, 300f, 36f), 17, TextAnchor.MiddleCenter).text = "Submit this answer? It ends the case.";
            HeadsetUiTool.Button(confirm.transform, "Yes, submit", new Rect(0f, 40f, 146f, 72f), view.ConfirmYes, SubmitColor);
            HeadsetUiTool.Button(confirm.transform, "No", new Rect(154f, 40f, 146f, 72f), view.ConfirmNo, NoColor);
            confirm.SetActive(false);

            var so = new SerializedObject(view);
            so.FindProperty("board").objectReferenceValue = board;
            so.FindProperty("content").objectReferenceValue = content;
            so.FindProperty("summary").objectReferenceValue = summary;
            so.FindProperty("submitButton").objectReferenceValue = submit;
            so.FindProperty("confirmGroup").objectReferenceValue = confirm;
            SetArray(so.FindProperty("suspectButtons"), suspects);
            SetArray(so.FindProperty("evidenceButtons"), evidence);
            so.ApplyModifiedPropertiesWithoutUndo();

            QuickActionsAPI.AddPokeCanvasInteraction(content);

            var scene = session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeObject = root;
            EditorUtility.DisplayDialog("Evidence board",
                "Built the evidence board under CaseRoot (" + (existing != null ? "kept its pose" : "default pose: 1.3 m left, facing the centre") +
                "). Move 'EvidenceBoard' in the scene if needed - rerunning keeps its pose.\nScene saved. Rebuild to apply.", "OK");
        }

        private static void SetArray(SerializedProperty array, GameObject[] items)
        {
            array.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
    }
}
