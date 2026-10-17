using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// Gives every uGUI Graphic on a world-space canvas that uses the default UI material the depth-occlusion UI
    /// material (FYP/OccludedUI), once at start, so a real person in front of a panel (setup panel, participant card,
    /// evidence board, tag buttons, labels) isn't drawn over by it. Runs at runtime rather than in the Editor so it
    /// works whichever FYP tool (re)built the panels last. Graphics with their own material are left alone.
    /// Added by FYP > Set Up Occlusion.
    /// </summary>
    public class UiOcclusion : MonoBehaviour
    {
        [SerializeField] private Material material;

        /// <summary>Number of Graphics switched to the occlusion material at start.</summary>
        public int AppliedCount { get; private set; }

        private void Start()
        {
            if (material == null) return;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.WorldSpace) continue;
                foreach (var graphic in canvas.GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic.material != graphic.defaultMaterial) continue;
                    graphic.material = material;
                    AppliedCount++;
                }
            }
            Debug.Log($"[UiOcclusion] Depth occlusion material on {AppliedCount} UI graphic(s)");
        }
    }
}
