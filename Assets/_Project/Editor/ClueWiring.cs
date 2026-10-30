using System.Text;
using FYP.Detective.Meta;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Shared, idempotent wiring for clue objects (used by the FYP clue menu tools). Running any step twice
    /// re-uses what is there instead of adding duplicates.
    /// Meta APIs (Interaction SDK 207, Library/PackageCache/com.meta.xr.sdk.interaction):
    ///   InteractableUnityEventWrapper (Runtime/Scripts/Unity/InteractableUnityEventWrapper.cs): serialized
    ///     `_interactableView`, UnityEvents `WhenSelect` / `WhenUnselect`
    ///   HandGrabInteractable, GrabInteractable, PokeInteractable (all IInteractableView)
    ///   QuickActionsAPI.AddPokeCanvasInteraction (Editor/QuickActions/Scripts/QuickActionsAPI.cs)
    /// </summary>
    internal static class ClueWiring
    {
        private const string DefaultClueId = "clue";

        /// <summary>Adds/updates a Clue on <paramref name="go"/>. A default id becomes the object name.</summary>
        public static Clue EnsureClue(GameObject go, SessionManager session)
        {
            var clue = go.GetComponent<Clue>();
            if (clue == null) clue = go.AddComponent<Clue>();
            var so = new SerializedObject(clue);
            var id = so.FindProperty("clueId");
            if (string.IsNullOrEmpty(id.stringValue) || id.stringValue == DefaultClueId) id.stringValue = ToId(go.name);
            var display = so.FindProperty("displayName");
            if (string.IsNullOrEmpty(display.stringValue) || display.stringValue == "Clue") display.stringValue = go.name;
            so.FindProperty("session").objectReferenceValue = session;
            so.ApplyModifiedPropertiesWithoutUndo();
            return clue;
        }

        /// <summary>Wraps every grab interactable under the clue: WhenSelect → MarkFound + BeginHold, WhenUnselect → EndHold.</summary>
        public static int WireGrabInteractables(Clue clue)
        {
            int count = 0;
            foreach (var interactable in clue.GetComponentsInChildren<HandGrabInteractable>(true))
            {
                WireSelect(interactable, clue, grab: true);
                count++;
            }
            foreach (var interactable in clue.GetComponentsInChildren<GrabInteractable>(true))
            {
                WireSelect(interactable, clue, grab: true);
                count++;
            }
            return count;
        }

        /// <summary>One InteractableUnityEventWrapper per interactable, wired to the clue.</summary>
        public static void WireSelect(MonoBehaviour interactable, Clue clue, bool grab)
        {
            var wrapper = FindWrapper(interactable);
            if (wrapper == null)
            {
                wrapper = interactable.gameObject.AddComponent<InteractableUnityEventWrapper>();
                var so = new SerializedObject(wrapper);
                so.FindProperty("_interactableView").objectReferenceValue = interactable;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AddVoidOnce(wrapper.WhenSelect, clue, clue.MarkFound, nameof(Clue.MarkFound));
            if (grab)
            {
                AddVoidOnce(wrapper.WhenSelect, clue, clue.BeginHold, nameof(Clue.BeginHold));
                AddVoidOnce(wrapper.WhenUnselect, clue, clue.EndHold, nameof(Clue.EndHold));
            }
            EditorUtility.SetDirty(wrapper);
        }

        /// <summary>Every interaction wrapper under the clue: WhenHover → highlight.Hover, WhenUnhover → Unhover.</summary>
        public static void WireHover(Clue clue, ClueHighlight highlight)
        {
            foreach (var wrapper in clue.GetComponentsInChildren<InteractableUnityEventWrapper>(true))
            {
                AddVoidOnce(wrapper.WhenHover, highlight, highlight.Hover, nameof(ClueHighlight.Hover));
                AddVoidOnce(wrapper.WhenUnhover, highlight, highlight.Unhover, nameof(ClueHighlight.Unhover));
                EditorUtility.SetDirty(wrapper);
            }
        }

        public static void EnsureResetter(Clue clue, CaseRoot caseRoot, SessionManager session)
        {
            var resetter = clue.GetComponent<ClueResetter>();
            if (resetter == null) resetter = clue.gameObject.AddComponent<ClueResetter>();
            MainSceneBuilder.SetObject(resetter, "caseRoot", caseRoot);
            MainSceneBuilder.SetObject(resetter, "session", session);
            EnsureGravity(clue, session);
        }

        public const string PhysicsMaterialPath = "Assets/_Project/Art/Materials/Props/CluePhysics.physicMaterial";

        /// <summary>
        /// Gravity for a grabbable clue (see <see cref="ClueGravity"/>): the Rigidbody starts kinematic with gravity on,
        /// interpolated, continuous speculative collision (thin paper, 4 mm), damped and with a no-bounce,
        /// high-friction material so a dropped clue doesn't roll or slide; the Grabbable's throw-on-release is off
        /// (Grabbable `_throwWhenUnselected`, Runtime/Scripts/Interaction/Grabbable/Grabbable.cs; `_kinematicWhileSelected`
        /// stays on, so the Interaction SDK keeps it kinematic while held).
        /// </summary>
        public static void EnsureGravity(Clue clue, SessionManager session)
        {
            var body = clue.GetComponent<Rigidbody>();
            if (body == null) body = clue.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = true;
            body.mass = 0.2f;
            body.linearDamping = 0.5f;
            body.angularDamping = 4f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var material = CluePhysicsMaterial();
            foreach (var c in clue.GetComponents<Collider>()) if (!c.isTrigger) c.sharedMaterial = material;

            foreach (var grabbable in clue.GetComponentsInChildren<Grabbable>(true))
            {
                var so = new SerializedObject(grabbable);
                so.FindProperty("_rigidbody").objectReferenceValue = body;   // GrabWizard relies on Grabbable.Reset() for this
                so.FindProperty("_throwWhenUnselected").boolValue = false;
                so.FindProperty("_kinematicWhileSelected").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var gravity = clue.GetComponent<ClueGravity>();
            if (gravity == null) gravity = clue.gameObject.AddComponent<ClueGravity>();
            MainSceneBuilder.SetObject(gravity, "session", session);
        }

        private static PhysicsMaterial CluePhysicsMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMaterialPath);
            if (material == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PhysicsMaterialPath));
                material = new PhysicsMaterial("CluePhysics");
                AssetDatabase.CreateAsset(material, PhysicsMaterialPath);
            }
            material.bounciness = 0f;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            material.dynamicFriction = 0.9f;
            material.staticFriction = 1f;
            material.frictionCombine = PhysicsMaterialCombine.Maximum;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// For a static clue (e.g. writing on a wall): a transparent poke canvas over the object's front face
        /// (local -Z side, like a Unity Quad); poking it selects the PokeInteractable → Clue.MarkFound.
        /// </summary>
        public static bool EnsurePokeSurface(Clue clue)
        {
            const string surfaceName = "PokeSurface";
            var existing = clue.transform.Find(surfaceName);
            GameObject surface;
            if (existing != null)
            {
                surface = existing.gameObject;
            }
            else
            {
                var mesh = clue.GetComponent<MeshFilter>();
                if (mesh == null || mesh.sharedMesh == null) return false;
                Bounds b = mesh.sharedMesh.bounds;   // object-local units

                surface = new GameObject(surfaceName, typeof(RectTransform), typeof(Canvas), typeof(Image));
                surface.transform.SetParent(clue.transform, false);
                surface.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)surface.transform;
                rect.sizeDelta = new Vector2(b.size.x, b.size.y);
                rect.localScale = Vector3.one;
                rect.localPosition = new Vector3(b.center.x, b.center.y, b.min.z - 0.002f);
                rect.localRotation = Quaternion.identity;
                surface.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);   // invisible, still a poke target
            }

            if (surface.GetComponentInChildren<PokeInteractable>(true) == null)
                QuickActionsAPI.AddPokeCanvasInteraction(surface);
            var poke = surface.GetComponentInChildren<PokeInteractable>(true);
            if (poke == null) return false;
            WireSelect(poke, clue, grab: false);
            return true;
        }

        /// <summary>A pokeable "Tag as Evidence" button that follows the clue (sibling object, so it isn't scaled with it).</summary>
        // Evidence tag colours: the participant card's case-file palette (HeadsetUiTool).
        private static readonly Color TagManila = new Color(0.80f, 0.66f, 0.42f, 1f);
        private static readonly Color TagManilaDark = new Color(0.60f, 0.47f, 0.27f, 1f);
        private static readonly Color TagSheet = new Color(0.98f, 0.96f, 0.89f, 1f);
        private static readonly Color TagInk = new Color(0.13f, 0.12f, 0.14f, 1f);
        private static readonly Color TagStampRed = new Color(0.76f, 0.10f, 0.08f, 1f);
        private static readonly Color TagBrass = new Color(0.72f, 0.56f, 0.28f, 1f);

        // "CASE X" / "CASE Y" from the clue id's prefix (only the case letter; the rest of the id stays hidden).
        private static string CaseLetter(Clue clue)
        {
            string id = clue.ClueId ?? "";
            return id.Length > 1 && id[1] == '_' ? "CASE " + id[0] : "";
        }

        public static ClueTagButton EnsureTagButton(Clue clue, Transform head)
        {
            foreach (var b in Object.FindObjectsByType<ClueTagButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b.Clue == clue)
                {
                    MainSceneBuilder.SetObject(b, "head", head);
                    return b;
                }
            }

            var root = new GameObject("TagButton - " + clue.name);
            root.transform.SetParent(clue.transform.parent, false);
            root.transform.position = clue.transform.position + Vector3.up * 0.2f;

            // Evidence tag (user, session 6: "polished, suits the design of the app"): a manila evidence tag with a brass
            // eyelet, red EVIDENCE header + case letter, a typed slip (name, red rule, description) and a red
            // rubber-stamp button (>= 4 cm); once tagged the button turns ink blue and a TAGGED stamp lands on the slip
            // (ClueTagButton). Canvas units = mm; colours and helpers of the participant card (HeadsetUiTool). The clue id
            // is never shown: it would tell key evidence from red herrings.
            bool withText = !string.IsNullOrEmpty(clue.InspectText);
            const float width = 250f, headerH = 46f, buttonH = 66f;
            float slipH = withText ? 86f : 34f;
            float height = headerH + slipH + buttonH + 8f, tagW = width - 6f, tagH = height - 8f;
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            GameObject content = HeadsetUiTool.Canvas(root.transform, width, height, new Color(0f, 0f, 0f, 0f));
            Transform c = content.transform;

            HeadsetUiTool.Panel(c, "Shadow", new Rect(6f, 8f, tagW, tagH), new Color(0f, 0f, 0f, 0.35f), rounded);
            HeadsetUiTool.Panel(c, "Tag", new Rect(0f, 0f, tagW, tagH), TagManila, rounded);
            var eyelet = HeadsetUiTool.Panel(c, "Eyelet", new Rect(12f, 9f, 28f, 28f), TagBrass, knob);
            HeadsetUiTool.Panel(eyelet, "Hole", new Rect(7f, 7f, 14f, 14f), new Color(0.10f, 0.08f, 0.06f, 0.9f), knob);

            Text header = HeadsetUiTool.Label(c, "Header", new Rect(48f, 7f, 150f, 32f), 22, TextAnchor.MiddleLeft);
            header.text = "EVIDENCE";
            header.fontStyle = FontStyle.Bold;
            header.color = TagStampRed;
            Text caseLabel = HeadsetUiTool.Label(c, "Case", new Rect(tagW - 104f, 9f, 92f, 28f), 13, TextAnchor.MiddleRight);
            caseLabel.text = CaseLetter(clue);
            caseLabel.fontStyle = FontStyle.Bold;
            caseLabel.color = TagManilaDark;

            var slip = HeadsetUiTool.Panel(c, "Slip", new Rect(12f, headerH, tagW - 24f, slipH), TagSheet, null);
            ((RectTransform)slip).localRotation = Quaternion.Euler(0f, 0f, -0.6f);
            float inner = tagW - 44f;
            Text nameLabel = HeadsetUiTool.Label(slip, "Name", new Rect(10f, 4f, inner, 24f), 17, TextAnchor.MiddleLeft);
            nameLabel.text = clue.DisplayName;
            nameLabel.fontStyle = FontStyle.Bold;
            nameLabel.color = TagInk;
            nameLabel.resizeTextForBestFit = true;
            nameLabel.resizeTextMinSize = 12;
            nameLabel.resizeTextMaxSize = 17;
            HeadsetUiTool.Panel(slip, "Rule", new Rect(10f, 29f, inner, 2f), TagStampRed, null);
            Text inspect = null;
            if (withText)
            {
                inspect = HeadsetUiTool.Label(slip, "Text", new Rect(10f, 35f, inner, slipH - 39f), 13, TextAnchor.UpperLeft);
                inspect.color = TagInk;
                inspect.resizeTextForBestFit = true;   // long notes shrink a little instead of being cut off
                inspect.resizeTextMinSize = 10;
                inspect.resizeTextMaxSize = 13;
                inspect.text = "";
            }

            // TAGGED stamp on the slip (shown by ClueTagButton while tagged).
            var stamp = HeadsetUiTool.Panel(slip, "TaggedStamp", new Rect(inner - 92f, slipH - 40f, 104f, 34f),
                new Color(TagStampRed.r, TagStampRed.g, TagStampRed.b, 0.12f), rounded);
            ((RectTransform)stamp).localRotation = Quaternion.Euler(0f, 0f, 10f);
            Text stampText = HeadsetUiTool.Label(stamp, "Text", new Rect(0f, 0f, 104f, 34f), 20, TextAnchor.MiddleCenter);
            stampText.text = "TAGGED";
            stampText.fontStyle = FontStyle.Bold;
            stampText.color = new Color(TagStampRed.r, TagStampRed.g, TagStampRed.b, 0.85f);
            stamp.gameObject.SetActive(false);

            GameObject buttonGo = HeadsetUiTool.Button(c, "TAG AS EVIDENCE", new Rect((tagW - 196f) / 2f, tagH - 58f, 196f, 50f),
                clue.ToggleTagged, TagStampRed);
            var buttonImage = buttonGo.GetComponent<Image>();
            HeadsetUiTool.Round(buttonImage, rounded);
            Text text = buttonGo.GetComponentInChildren<Text>();
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 17;
            text.lineSpacing = 0.9f;

            QuickActionsAPI.AddPokeCanvasInteraction(content);

            var tag = root.AddComponent<ClueTagButton>();
            MainSceneBuilder.SetObject(tag, "clue", clue);
            MainSceneBuilder.SetObject(tag, "content", content);
            MainSceneBuilder.SetObject(tag, "label", text);
            MainSceneBuilder.SetObject(tag, "nameLabel", nameLabel);
            if (inspect != null) MainSceneBuilder.SetObject(tag, "inspectLabel", inspect);
            MainSceneBuilder.SetObject(tag, "buttonImage", buttonImage);
            MainSceneBuilder.SetObject(tag, "taggedStamp", stamp.gameObject);
            MainSceneBuilder.SetObject(tag, "head", head);
            return tag;
        }

        /// <summary>Adds HeadsetPresenceMonitor (headset off = paused) to the session object if it is missing.</summary>
        public static void EnsureHeadsetPresenceMonitor(SessionManager session)
        {
            var monitor = session.GetComponent<HeadsetPresenceMonitor>();
            if (monitor == null) monitor = session.gameObject.AddComponent<HeadsetPresenceMonitor>();
            MainSceneBuilder.SetObject(monitor, "session", session);
        }

        /// <summary>Adds HandTrackingMonitor to the session object if it is missing.</summary>
        public static void EnsureHandTrackingMonitor(SessionManager session, PlayerRig rig)
        {
            var monitor = session.GetComponent<HandTrackingMonitor>();
            if (monitor == null) monitor = session.gameObject.AddComponent<HandTrackingMonitor>();
            MainSceneBuilder.SetObject(monitor, "rig", rig);
            if (rig.LeftHand != null) MainSceneBuilder.SetObject(monitor, "leftHand", rig.LeftHand.GetComponentInChildren<OVRHand>(true));
            if (rig.RightHand != null) MainSceneBuilder.SetObject(monitor, "rightHand", rig.RightHand.GetComponentInChildren<OVRHand>(true));
        }

        private static InteractableUnityEventWrapper FindWrapper(MonoBehaviour interactable)
        {
            foreach (var w in interactable.GetComponents<InteractableUnityEventWrapper>())
            {
                var view = new SerializedObject(w).FindProperty("_interactableView").objectReferenceValue;
                if (view == interactable) return w;
            }
            return null;
        }

        private static void AddVoidOnce(UnityEvent evt, Object target, UnityAction call, string methodName)
        {
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (evt.GetPersistentTarget(i) == target && evt.GetPersistentMethodName(i) == methodName) return;
            }
            UnityEventTools.AddVoidPersistentListener(evt, call);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>"Bloody Knife (1)" → "Bloody_Knife_1": letters, digits, '_' and '-' only.</summary>
        public static string ToId(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ' && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            return sb.Length == 0 ? DefaultClueId : sb.ToString().TrimEnd('_');
        }
    }
}
