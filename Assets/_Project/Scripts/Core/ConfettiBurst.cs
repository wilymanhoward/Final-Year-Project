using System;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Confetti for a solved case (supervisor, 2026-11-05: "celebrate when correct"): small paper pieces shot up and
    /// outwards, then fluttering down slowly (drag, side-to-side flutter, spin) and lying still on the floor; they shrink
    /// away at the end of <see cref="Lifetime"/>. Pure, deterministic (seeded), unit tested; VerdictCelebration draws it.
    /// </summary>
    public sealed class ConfettiBurst
    {
        public const float Lifetime = 6f;
        private const float Gravity = -3.0f, Drag = 1.6f, Flutter = 0.9f;

        public readonly Vector3[] Position;
        public readonly Quaternion[] Rotation;
        public readonly Color32[] Colour;
        public float Age { get; private set; }
        /// <summary>1 while flying and resting, shrinking to 0 over the last second.</summary>
        public float Scale => Mathf.Clamp01(Lifetime - Age);
        public bool Alive => Age < Lifetime;

        private readonly Vector3[] _velocity;
        private readonly Vector3[] _spinAxis;
        private readonly float[] _spinSpeed, _phase;
        private readonly float _floorY;

        private static readonly Color32[] Palette =
        {
            new Color32(232, 184, 48, 255), new Color32(255, 220, 110, 255), new Color32(214, 60, 52, 255),
            new Color32(40, 160, 150, 255), new Color32(245, 245, 240, 255), new Color32(70, 110, 200, 255),
        };

        public ConfettiBurst(int count, Vector3 centre, float floorY, int seed)
        {
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            Position = new Vector3[count];
            Rotation = new Quaternion[count];
            Colour = new Color32[count];
            _velocity = new Vector3[count];
            _spinAxis = new Vector3[count];
            _spinSpeed = new float[count];
            _phase = new float[count];
            _floorY = floorY;
            for (int i = 0; i < count; i++)
            {
                float a = R() * Mathf.PI * 2f, r = R() * 0.35f;
                Position[i] = centre + new Vector3(Mathf.Cos(a) * r, R() * 0.2f, Mathf.Sin(a) * r);
                float speed = 0.6f + R() * 1.4f;
                _velocity[i] = new Vector3(Mathf.Cos(a) * speed, 1.8f + R() * 1.8f, Mathf.Sin(a) * speed);
                _spinAxis[i] = new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f).normalized;
                if (_spinAxis[i] == Vector3.zero) _spinAxis[i] = Vector3.up;
                _spinSpeed[i] = 200f + R() * 500f;
                _phase[i] = R() * 10f;
                Rotation[i] = Quaternion.Euler(R() * 360f, R() * 360f, R() * 360f);
                Colour[i] = Palette[i % Palette.Length];
            }
        }

        /// <summary>Advances by <paramref name="dt"/> seconds. False once the burst is over.</summary>
        public bool Step(float dt)
        {
            dt = Math.Min(dt, 0.1f);
            Age += dt;
            float drag = Math.Max(0f, 1f - Drag * dt);
            for (int i = 0; i < Position.Length; i++)
            {
                if (Position[i].y <= _floorY + 0.003f && _velocity[i].y <= 0f)
                {
                    Position[i].y = _floorY + 0.003f;   // landed: lies still
                    _velocity[i] = Vector3.zero;
                    continue;
                }
                Vector3 v = _velocity[i];
                v.y += Gravity * dt;
                v *= drag;
                v.x += Mathf.Sin(Age * 6f + _phase[i]) * Flutter * dt;
                v.z += Mathf.Cos(Age * 5f + _phase[i]) * Flutter * dt;
                _velocity[i] = v;
                Position[i] += v * dt;
                Rotation[i] = Quaternion.AngleAxis(_spinSpeed[i] * dt, _spinAxis[i]) * Rotation[i];
            }
            return Alive;
        }
    }
}
