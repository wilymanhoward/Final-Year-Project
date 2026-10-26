using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// FYP > Set Up Briefing Detective (session 6): builds "BriefingDetective" with <see cref="DetectiveBriefing"/>:
    /// the low-poly Inspector Hart assembled from Detective_Body / _Head / _Moustache / _Arm (tools/maya/build_props.py;
    /// offsets = DETECTIVE_*_AT there), a 3D voice AudioSource, a speech bubble above the hat, the line script
    /// (Audio/Briefing/briefing_lines.csv) and every AudioClip in Audio/Briefing (matched by file name: placeholders
    /// from tools/make_briefing_tts.ps1 or the AI voice files). Idempotent: an existing detective is rebuilt.
    /// The participant card's Begin / Replay buttons come from FYP > Set Up Headset UI.
    /// </summary>
    public static class CharacterSetupTool
    {
        private const string RootName = "BriefingDetective";
        public const string AudioFolder = "Assets/_Project/Audio/Briefing";
        public const string ScriptPath = AudioFolder + "/briefing_lines.csv";
        // Keep in sync with DETECTIVE_*_AT in tools/maya/build_props.py.
        private static readonly Vector3 HeadAt = new Vector3(0f, 1.585f, 0.012f);
        private static readonly Vector3 MoustacheAt = new Vector3(0f, 0.074f, 0.124f);   // in head space
        private static readonly Vector3 ArmAt = new Vector3(0.195f, 1.425f, -0.005f);
        private static readonly Vector3 ArmLeftAt = new Vector3(-0.195f, 1.425f, -0.005f);
        private static readonly Vector3 EyesAt = new Vector3(0f, 0.14f, 0.1155f);        // in head space
        private static readonly Vector3 MouthAt = new Vector3(0f, 0.053f, 0.119f);       // in head space
        private static readonly Vector3 ElbowRightAt = new Vector3(0.035f, -0.27f, -0.01f);   // in right shoulder space
        private static readonly Vector3 ElbowLeftAt = new Vector3(-0.04f, -0.275f, -0.025f);  // in left shoulder space
        private static readonly string[] Parts =
        {
            "Detective_Body", "Detective_Head", "Detective_Moustache", "Detective_Arm", "Detective_Forearm", "Detective_ArmL",
            "Detective_ForearmL", "Detective_Eyes", "Detective_Mouth", "Detective_ForearmPoint",
        };

        /// <summary>Batch mode: opens Main.unity and runs <see cref="SetUp"/> (dialogs don't block in batch mode).</summary>
        public static void SetUpBatch()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity");
            SetUp();
        }

        [MenuItem("FYP/Set Up Briefing Detective")]
        public static void SetUp()
        {
            var session = Object.FindFirstObjectByType<SessionManager>();
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            var caseRoot = Object.FindFirstObjectByType<CaseRoot>(FindObjectsInactive.Include);
            if (session == null || rig == null || rig.Head == null || caseRoot == null)
            {
                EditorUtility.DisplayDialog("Briefing detective", "Open Main.unity and run FYP > Set Up Case Root and Placement first.", "OK");
                return;
            }
            AssetDatabase.Refresh();
            var missing = PropAssets.Missing(Parts);
            var script = AssetDatabase.LoadAssetAtPath<TextAsset>(ScriptPath);
            if (missing.Count > 0 || script == null)
            {
                EditorUtility.DisplayDialog("Briefing detective",
                    (missing.Count > 0 ? "Missing meshes (run mayapy tools/maya/build_props.py): " + string.Join(", ", missing) + "\n" : "") +
                    (script == null ? "Missing " + ScriptPath + "\n" : ""), "OK");
                return;
            }

            foreach (var old in Object.FindObjectsByType<DetectiveBriefing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Briefing detective");
            var briefing = root.AddComponent<DetectiveBriefing>();

            var character = new GameObject("Character");
            character.transform.SetParent(root.transform, false);
            PropAssets.AddBlobShadow(character.transform, PropAssets.Mesh("Detective_Body", "_Body").bounds, 1.1f);
            // Rig pivot at the feet: breathing / weight shift move every body part (bubble and voice stay still).
            Transform rigPivot = Pivot(character.transform, "Rig", Vector3.zero);
            var body = PropAssets.AddVisual(rigPivot, "Detective_Body", Vector3.zero, Quaternion.identity, Vector3.one);
            body.name = "Body";
            Transform head = Pivot(rigPivot, "Head", HeadAt);
            PropAssets.AddVisual(head, "Detective_Head", Vector3.zero, Quaternion.identity, Vector3.one);
            Transform moustache = Pivot(head, "Moustache", MoustacheAt);
            PropAssets.AddVisual(moustache, "Detective_Moustache", Vector3.zero, Quaternion.identity, Vector3.one);
            Transform eyes = Pivot(head, "Eyes", EyesAt);
            PropAssets.AddVisual(eyes, "Detective_Eyes", Vector3.zero, Quaternion.identity, Vector3.one);
            Transform mouth = Pivot(head, "Mouth", MouthAt);
            PropAssets.AddVisual(mouth, "Detective_Mouth", Vector3.zero, Quaternion.identity, Vector3.one);
            Transform arm = Pivot(rigPivot, "Arm", ArmAt);
            PropAssets.AddVisual(arm, "Detective_Arm", Vector3.zero, Quaternion.identity, Vector3.one);
            Transform forearm = Pivot(arm, "Forearm", ElbowRightAt);
            var forearmVisual = PropAssets.AddVisual(forearm, "Detective_Forearm", Vector3.zero, Quaternion.identity, Vector3.one);
            Transform leftArm = Pivot(rigPivot, "ArmL", ArmLeftAt);
            PropAssets.AddVisual(leftArm, "Detective_ArmL", Vector3.zero, Quaternion.identity, Vector3.one);
            Transform leftForearm = Pivot(leftArm, "ForearmL", ElbowLeftAt);
            PropAssets.AddVisual(leftForearm, "Detective_ForearmL", Vector3.zero, Quaternion.identity, Vector3.one);

            // Voice: 3D, from the head.
            var voiceGo = new GameObject("Voice", typeof(AudioSource));
            voiceGo.transform.SetParent(character.transform, false);
            voiceGo.transform.localPosition = new Vector3(0f, 1.68f, 0.09f);
            var voice = voiceGo.GetComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 1.5f;
            voice.maxDistance = 12f;
            voice.dopplerLevel = 0f;
            voice.volume = 1f;

            Bubble bubble = BuildBubble(character.transform);

            // One clip per line name. The real voice (e.g. ElevenLabs .mp3/.ogg, or a .wav the user put there) wins over
            // the placeholder .wav from tools/make_briefing_tts.ps1 when both exist with the same name.
            var paths = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            var chosen = paths.GroupBy(System.IO.Path.GetFileNameWithoutExtension)
                .Select(g => g.OrderBy(path => path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase) ? 1 : 0).First())
                .ToList();
            int realVoice = chosen.Count(path => !path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase));
            var clips = chosen.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c != null).OrderBy(c => c.name).ToArray();
            var lines = BriefingScript.Parse(script.text);
            var names = new HashSet<string>(clips.Select(c => c.name));
            var noAudio = lines.Select(l => l.File).Distinct().Where(f => !names.Contains(f)).ToList();

            var so = new SerializedObject(briefing);
            so.FindProperty("session").objectReferenceValue = session;
            so.FindProperty("head").objectReferenceValue = rig.Head;
            so.FindProperty("floorReference").objectReferenceValue = caseRoot.transform;
            so.FindProperty("character").objectReferenceValue = character;
            so.FindProperty("headPart").objectReferenceValue = head;
            so.FindProperty("armPart").objectReferenceValue = arm;
            so.FindProperty("moustache").objectReferenceValue = moustache;
            so.FindProperty("rig").objectReferenceValue = rigPivot;
            so.FindProperty("leftArm").objectReferenceValue = leftArm;
            so.FindProperty("rightForearm").objectReferenceValue = forearm;
            so.FindProperty("rightForearmFilter").objectReferenceValue = forearmVisual.GetComponent<MeshFilter>();
            so.FindProperty("pointForearmMesh").objectReferenceValue = PropAssets.Mesh("Detective_ForearmPoint", "_Body");
            so.FindProperty("leftForearm").objectReferenceValue = leftForearm;
            so.FindProperty("eyes").objectReferenceValue = eyes;
            so.FindProperty("mouth").objectReferenceValue = mouth;
            so.FindProperty("voice").objectReferenceValue = voice;
            so.FindProperty("bubbleText").objectReferenceValue = bubble.Text;
            so.FindProperty("bubbleBody").objectReferenceValue = bubble.Body;
            so.FindProperty("moreIndicator").objectReferenceValue = bubble.More;
            so.FindProperty("script").objectReferenceValue = script;
            var clipArray = so.FindProperty("clips");
            clipArray.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) clipArray.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            character.SetActive(false);

            var scene = session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeObject = root;
            EditorUtility.DisplayDialog("Briefing detective",
                $"Built '{RootName}': {lines.Count} script line(s), {clips.Length} voice clip(s), {realVoice} of them not a .wav " +
                "placeholder (e.g. ElevenLabs .mp3)." +
                (noAudio.Count > 0 ? "\nNo audio yet for: " + string.Join(", ", noAudio) + " (shown as text only)." : "") +
                "\n\nRun FYP > Set Up Headset UI too (Begin briefing / Replay on the card). New voice files: drop them into " +
                AudioFolder + " with the same names, then run this again. Scene saved.", "OK");
        }

        private static Transform Pivot(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        // Game-style speech bubble above the hat (user, session 6): white bubble with a thick dark outline, drop shadow,
        // outlined tail towards the detective, red "INSPECTOR HART" name tag, typewriter text and a bobbing "more" marker
        // (both driven by DetectiveBriefing). Readable from the participant: the character faces them along +Z.
        private struct Bubble
        {
            public Text Text;
            public RectTransform Body;
            public RectTransform More;
        }

        private static readonly Color BubbleInk = new Color(0.10f, 0.10f, 0.14f, 1f);
        private static readonly Color NameTagColor = new Color(0.78f, 0.16f, 0.12f, 1f);

        private static Bubble BuildBubble(Transform character)
        {
            const float width = 900f, height = 280f, border = 9f;
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var go = new GameObject("Bubble", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(character, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width + 60f, height + 120f);
            rect.localScale = Vector3.one * 0.001f;
            rect.localPosition = new Vector3(-0.19f, 2.2f, 0f);   // so the tail tip ends above the head
            rect.localRotation = Quaternion.Euler(0f, 180f, 0f);   // canvas front (-Z) towards the participant

            // Body: everything that pops in, pivot at the tail tip so it grows out of the detective.
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(go.transform, false);
            var body = (RectTransform)bodyGo.transform;
            body.sizeDelta = new Vector2(width, height);
            body.pivot = new Vector2(0.28f, -0.15f);   // the tail tip, below the bottom-left part of the box
            body.anchoredPosition = new Vector2(-width * 0.22f, 30f - height * 0.65f);   // box centred in the canvas

            Image Shape(string name, Vector2 size, Vector2 centre, Color color, Sprite sprite, float rotation = 0f, float ppu = 0.35f)
            {
                var s = new GameObject(name, typeof(RectTransform), typeof(Image));
                s.transform.SetParent(body, false);
                var r = (RectTransform)s.transform;
                r.sizeDelta = size;
                r.anchoredPosition = centre;
                r.localRotation = Quaternion.Euler(0f, 0f, rotation);
                var img = s.GetComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
                if (sprite != null)
                {
                    img.sprite = sprite;
                    img.type = Image.Type.Sliced;
                    img.pixelsPerUnitMultiplier = ppu;
                }
                return img;
            }

            Vector2 centre = Vector2.zero;   // children are anchored at the centre of the body's rect
            Vector2 tail = centre + new Vector2(-width * 0.22f, -height / 2f);
            Shape("Shadow", new Vector2(width, height), centre + new Vector2(12f, -12f), new Color(0f, 0f, 0f, 0.35f), rounded);
            Shape("TailOutline", new Vector2(86f, 86f), tail + new Vector2(0f, -6f), BubbleInk, null, 45f);
            Shape("Outline", new Vector2(width, height), centre, BubbleInk, rounded);
            Shape("TailFill", new Vector2(64f, 64f), tail + new Vector2(0f, 4f), Color.white, null, 45f);
            Shape("Fill", new Vector2(width - 2f * border, height - 2f * border), centre, Color.white, rounded, 0f, 0.38f);

            // Name tag on the top-left corner.
            Vector2 tagCentre = centre + new Vector2(-width / 2f + 170f, height / 2f + 4f);
            Shape("NameTagOutline", new Vector2(300f + 2f * border, 58f + 2f * border), tagCentre, BubbleInk, rounded, 0f, 0.6f);
            Shape("NameTag", new Vector2(300f, 58f), tagCentre, NameTagColor, rounded, 0f, 0.7f);
            var tagText = Label(body, "NameTagText", new Vector2(300f, 58f), tagCentre, 26, TextAnchor.MiddleCenter, Color.white);
            tagText.text = "INSPECTOR HART";
            tagText.fontStyle = FontStyle.Bold;

            var text = Label(body, "Text", new Vector2(width - 90f, height - 70f), centre + new Vector2(0f, -6f), 36, TextAnchor.MiddleLeft, BubbleInk);
            text.supportRichText = true;   // typewriter: untyped letters are transparent rich text
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 24;
            text.resizeTextMaxSize = 36;

            var more = Shape("More", new Vector2(24f, 24f), centre + new Vector2(width / 2f - 40f, -height / 2f + 36f), BubbleInk, null, 45f);
            more.gameObject.SetActive(false);
            return new Bubble { Text = text, Body = body, More = (RectTransform)more.transform };
        }

        private static Text Label(Transform parent, string name, Vector2 size, Vector2 centre, int fontSize, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.sizeDelta = size;
            r.anchoredPosition = centre;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }
    }
}
