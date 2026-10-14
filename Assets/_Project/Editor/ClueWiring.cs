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

        public static void EnsureResetter(Clue clue, CaseRoot caseRoot, SessionManager session)
        {
            var resetter = clue.GetComponent<ClueResetter>();
            if (resetter == null) resetter = clue.gameObject.AddComponent<ClueResetter>();
            MainSceneBuilder.SetObject(resetter, "caseRoot", caseRoot);
            MainSceneBuilder.SetObject(resetter, "session", session);
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

            // Canvas units = mm. Clues with inspect text get a 240 x 130 mm panel: name + text on top, a 140 x 56 mm
            // button (>= 4 cm) below; others just the button.
            bool withText = !string.IsNullOrEmpty(clue.InspectText);
            var content = new GameObject("Content", typeof(RectTransform), typeof(Canvas));
            content.transform.SetParent(root.transform, false);
            content.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var contentRect = (RectTransform)content.transform;
            contentRect.sizeDelta = withText ? new Vector2(240f, 130f) : new Vector2(140f, 56f);
            contentRect.localScale = Vector3.one * 0.001f;

            Text inspect = null;
            if (withText)
            {
                var bg = new GameObject("Inspect", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(content.transform, false);
                var bgRect = (RectTransform)bg.transform;
                bgRect.anchorMin = new Vector2(0f, 0.46f);
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
                var bgImage = bg.GetComponent<Image>();
                bgImage.color = new Color(0.97f, 0.95f, 0.88f, 0.95f);
                bgImage.raycastTarget = false;
                var inspectGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                inspectGo.transform.SetParent(bg.transform, false);
                var inspectRect = (RectTransform)inspectGo.transform;
                Stretch(inspectRect);
                inspectRect.offsetMin = new Vector2(6f, 3f);
                inspectRect.offsetMax = new Vector2(-6f, -3f);
                inspect = inspectGo.GetComponent<Text>();
                inspect.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                inspect.fontSize = 13;
                inspect.alignment = TextAnchor.MiddleCenter;
                inspect.color = new Color(0.1f, 0.1f, 0.1f);
                inspect.horizontalOverflow = HorizontalWrapMode.Wrap;
                inspect.verticalOverflow = VerticalWrapMode.Truncate;
                inspect.raycastTarget = false;
                inspect.text = "";
            }

            var buttonGo = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(content.transform, false);
            var buttonRect = (RectTransform)buttonGo.transform;
            if (withText)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 0f);
                buttonRect.anchorMax = new Vector2(0.5f, 0f);
                buttonRect.pivot = new Vector2(0.5f, 0f);
                buttonRect.sizeDelta = new Vector2(140f, 56f);
                buttonRect.anchoredPosition = Vector2.zero;
            }
            else Stretch(buttonRect);
            buttonGo.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 0.85f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(buttonGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(6f, 4f);
            textRect.offsetMax = new Vector2(-6f, -4f);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;   // ~2 cm, readable at arm's length
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = "Tag as Evidence";

            var button = buttonGo.GetComponent<Button>();
            UnityEventTools.AddVoidPersistentListener(button.onClick, clue.ToggleTagged);

            QuickActionsAPI.AddPokeCanvasInteraction(content);

            var tag = root.AddComponent<ClueTagButton>();
            MainSceneBuilder.SetObject(tag, "clue", clue);
            MainSceneBuilder.SetObject(tag, "content", content);
            MainSceneBuilder.SetObject(tag, "label", text);
            if (inspect != null) MainSceneBuilder.SetObject(tag, "inspectLabel", inspect);
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
