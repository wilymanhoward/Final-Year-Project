using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Case X briefing (user, session 6): the silhouette of the missing award, standing in the empty display case. It is
    /// invisible except while the detective points at the case (<see cref="SetBlinking"/>), when it pulses, so the
    /// participant sees where the award used to be. Built under the display prop by FYP > Build Case Scenes. Tint comes from
    /// the material's _BaseColor; only its alpha is animated (per-renderer property block, no material copies).
    /// </summary>
    public class TrophyGhost : MonoBehaviour
    {
        [SerializeField, Min(0.2f)] private float blinkHz = 1.4f;
        [SerializeField, Range(0f, 1f)] private float minAlpha = 0.08f;
        [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.8f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private Renderer[] _renderers = new Renderer[0];
        private MaterialPropertyBlock _block;
        private bool _blinking;

        public bool IsBlinking => _blinking;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            SetVisible(false);
        }

        /// <summary>Start or stop the blinking; stopped = hidden.</summary>
        public void SetBlinking(bool on)
        {
            _blinking = on;
            if (!on) SetVisible(false);
        }

        private void Update()
        {
            if (!_blinking) return;
            float s = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * blinkHz * 2f * Mathf.PI);
            _block.SetColor(BaseColor, new Color(1f, 1f, 1f, Mathf.Lerp(minAlpha, maxAlpha, s * s)));
            foreach (Renderer r in _renderers)
            {
                r.enabled = true;
                r.SetPropertyBlock(_block);
            }
        }

        private void SetVisible(bool visible)
        {
            foreach (Renderer r in _renderers) r.enabled = visible;
        }
    }
}
