using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Damped spring for natural character motion (session 6, "less robotic"): a value chases its target with
    /// acceleration, a little overshoot and settling instead of moving at once. <see cref="Frequency"/> = how fast (Hz),
    /// <see cref="Damping"/> &lt; 1 overshoots slightly, 1 = no overshoot. <see cref="Kick"/> adds a velocity impulse
    /// (e.g. a nod on a speech beat). Semi-implicit Euler with sub-steps, stable for frame times up to 0.1 s. Pure.
    /// </summary>
    public struct Spring
    {
        public float Value;
        public float Velocity;
        public float Frequency;
        public float Damping;

        public Spring(float frequency, float damping, float value = 0f)
        {
            Frequency = frequency;
            Damping = damping;
            Value = value;
            Velocity = 0f;
        }

        public void Kick(float velocity) => Velocity += velocity;

        public float Step(float target, float dt)
        {
            if (dt <= 0f) return Value;
            float w = 2f * Mathf.PI * Frequency;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.01f), 1, 10);
            float h = Mathf.Min(dt, 0.1f) / steps;
            for (int i = 0; i < steps; i++)
            {
                float accel = w * w * (target - Value) - 2f * Damping * w * Velocity;
                Velocity += accel * h;
                Value += Velocity * h;
            }
            return Value;
        }
    }
}
