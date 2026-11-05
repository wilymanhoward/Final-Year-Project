using System.Collections.Generic;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>What a ghost figure is doing (CrimeReplay turns it into body-part rotations).</summary>
    public enum GhostPose
    {
        Stand, Walk, WalkCarry, Work, Drink, Reach, Carry, Smash, Stab, Struggle, Lying, Hidden,
    }

    /// <summary>One key of a ghost's track: at fraction <see cref="T"/> (0..1) of the beat it is at (X, Z), turned Yaw.</summary>
    public struct GhostKey
    {
        public float T, X, Z, Yaw;
        public GhostPose Pose;
        public GhostKey(float t, float x, float z, float yaw, GhostPose pose) { T = t; X = x; Z = z; Yaw = yaw; Pose = pose; }
    }

    /// <summary>A clue that lights up at fraction <see cref="T"/> of a beat (the moment the evidence was made).</summary>
    public struct ClueGlow
    {
        public float T;
        public string ClueId;
        public ClueGlow(float t, string clueId) { T = t; ClueId = clueId; }
    }

    /// <summary>
    /// One narrated beat of the crime replay: the detective's line (<see cref="Line"/> = file in briefing_lines.csv) and
    /// what the ghosts do while he says it. Positions are CaseRoot-local; a ghost without keys keeps its last state.
    /// </summary>
    public sealed class RevealBeat
    {
        public string Line;
        public string TimeLabel;   // shown above the culprit at the start of the beat, e.g. "21:35" (null = none)
        public readonly List<GhostKey> Culprit = new List<GhostKey>();
        public readonly List<GhostKey> Victim = new List<GhostKey>();
        public readonly List<ClueGlow> Glows = new List<ClueGlow>();
    }

    /// <summary>
    /// The crime replay after the verdict (supervisor + user, 2026-11-05, Phase 2): per case the beats that match the
    /// detective's reveal lines (X_reveal_01..05 / Y_reveal_01..05). Pure data + evaluation, unit tested; CrimeReplay
    /// plays it. Case Y follows in Phase 3.
    /// </summary>
    public static class RevealScript
    {
        public static List<RevealBeat> For(CaseId id) => id == CaseId.X ? CaseX() : new List<RevealBeat>();

        // Case X "Last Coffee at Kite" (docs/cases.md): Grace comes in at 21:35, poisons Rizal's coffee at the front desk,
        // he slides off his chair; at 21:42 she opens the case with her code, takes the award, smashes the glass, hides her
        // mug under the stand and leaves. Spots from CaseSpecs (door, sign-in sheet, coffee machine at the desk's south end,
        // keypad K1 read from its +x side, the display front, the body outline).
        private static List<RevealBeat> CaseX()
        {
            const float deskX = 1.36f;
            var b1 = new RevealBeat { Line = "X_reveal_01", TimeLabel = "21:35" };   // comes in from the rain, signs in
            b1.Culprit.Add(new GhostKey(0.00f, 1.62f, -2.05f, 0f, GhostPose.Stand));
            b1.Culprit.Add(new GhostKey(0.12f, 1.62f, -2.05f, 0f, GhostPose.Stand));
            b1.Culprit.Add(new GhostKey(0.60f, deskX, 0.45f, 90f, GhostPose.Work));
            b1.Culprit.Add(new GhostKey(1.00f, deskX, 0.45f, 90f, GhostPose.Work));
            b1.Victim.Add(new GhostKey(0.00f, 1.30f, 0.90f, 90f, GhostPose.Work));
            b1.Glows.Add(new ClueGlow(0.65f, "X_K2"));

            var b2 = new RevealBeat { Line = "X_reveal_02" };   // makes the coffee, pills in her BEST MANAGER mug
            b2.Culprit.Add(new GhostKey(0.00f, deskX, 0.45f, 90f, GhostPose.Work));
            b2.Culprit.Add(new GhostKey(0.25f, 1.48f, 0.26f, 90f, GhostPose.Work));
            b2.Culprit.Add(new GhostKey(1.00f, 1.48f, 0.26f, 90f, GhostPose.Work));
            b2.Victim.Add(new GhostKey(0.00f, 1.30f, 0.90f, 90f, GhostPose.Work));
            b2.Glows.Add(new ClueGlow(0.45f, "X_K3"));

            var b3 = new RevealBeat { Line = "X_reveal_03" };   // he drinks it, slides off his chair
            b3.Culprit.Add(new GhostKey(0.00f, 1.48f, 0.26f, 90f, GhostPose.Work));
            b3.Culprit.Add(new GhostKey(0.12f, 1.22f, 0.58f, 20f, GhostPose.Reach));
            b3.Culprit.Add(new GhostKey(0.30f, 1.22f, 0.58f, 20f, GhostPose.Reach));
            b3.Culprit.Add(new GhostKey(0.40f, 1.45f, 0.30f, 0f, GhostPose.Stand));
            b3.Culprit.Add(new GhostKey(1.00f, 1.45f, 0.30f, -30f, GhostPose.Stand));
            b3.Victim.Add(new GhostKey(0.00f, 1.30f, 0.90f, 90f, GhostPose.Work));
            b3.Victim.Add(new GhostKey(0.30f, 1.30f, 0.90f, 200f, GhostPose.Drink));
            b3.Victim.Add(new GhostKey(0.58f, 1.30f, 0.90f, 200f, GhostPose.Drink));
            b3.Victim.Add(new GhostKey(0.68f, 1.12f, 1.12f, -60f, GhostPose.Lying));
            b3.Victim.Add(new GhostKey(0.80f, 1.00f, 1.33f, -90f, GhostPose.Lying));
            b3.Victim.Add(new GhostKey(1.00f, 1.00f, 1.33f, -90f, GhostPose.Lying));

            var b4 = new RevealBeat { Line = "X_reveal_04", TimeLabel = "21:42" };   // keypad code, takes the award
            b4.Culprit.Add(new GhostKey(0.00f, 1.45f, 0.30f, -30f, GhostPose.Stand));
            b4.Culprit.Add(new GhostKey(0.10f, 0.60f, 0.40f, -90f, GhostPose.Stand));
            b4.Culprit.Add(new GhostKey(0.22f, -0.25f, 0.90f, 0f, GhostPose.Stand));
            b4.Culprit.Add(new GhostKey(0.32f, -0.22f, 1.78f, -90f, GhostPose.Work));
            b4.Culprit.Add(new GhostKey(0.60f, -0.22f, 1.78f, -90f, GhostPose.Work));
            b4.Culprit.Add(new GhostKey(0.70f, -0.75f, 1.40f, 0f, GhostPose.Reach));
            b4.Culprit.Add(new GhostKey(0.86f, -0.75f, 1.40f, 0f, GhostPose.Reach));
            b4.Culprit.Add(new GhostKey(0.92f, -0.75f, 1.40f, 0f, GhostPose.Carry));
            b4.Culprit.Add(new GhostKey(1.00f, -0.75f, 1.40f, 0f, GhostPose.Carry));
            b4.Victim.Add(new GhostKey(0.00f, 1.00f, 1.33f, -90f, GhostPose.Lying));
            b4.Glows.Add(new ClueGlow(0.36f, "X_K1"));

            var b5 = new RevealBeat { Line = "X_reveal_05" };   // smashes the glass, hides the mug, leaves; the motive
            b5.Culprit.Add(new GhostKey(0.00f, -0.75f, 1.40f, 0f, GhostPose.Smash));
            b5.Culprit.Add(new GhostKey(0.22f, -0.75f, 1.40f, 0f, GhostPose.Smash));
            b5.Culprit.Add(new GhostKey(0.30f, -0.69f, 1.22f, 0f, GhostPose.Reach));
            b5.Culprit.Add(new GhostKey(0.45f, -0.69f, 1.22f, 0f, GhostPose.Reach));
            b5.Culprit.Add(new GhostKey(0.58f, 0.00f, 0.30f, 140f, GhostPose.Carry));
            b5.Culprit.Add(new GhostKey(0.76f, 1.35f, -1.55f, 150f, GhostPose.Carry));
            b5.Culprit.Add(new GhostKey(0.88f, 1.62f, -2.05f, 180f, GhostPose.Carry));
            b5.Culprit.Add(new GhostKey(1.00f, 1.62f, -2.05f, 180f, GhostPose.Carry));
            b5.Victim.Add(new GhostKey(0.00f, 1.00f, 1.33f, -90f, GhostPose.Lying));
            b5.Glows.Add(new ClueGlow(0.33f, "X_K3"));
            b5.Glows.Add(new ClueGlow(0.85f, "X_K4"));

            return new List<RevealBeat> { b1, b2, b3, b4, b5 };
        }

        /// <summary>Smooth step 0..1 (eases in and out).</summary>
        private static float Ease(float u) => u * u * (3f - 2f * u);

        /// <summary>
        /// Where a ghost is at fraction <paramref name="t"/> of a beat: position eased between keys; while it moves between
        /// two different spots it walks (carrying if either key carries) facing the way it goes, otherwise it holds the
        /// pose of the key it came from, turning towards the next key's yaw. False if the track is empty.
        /// </summary>
        public static bool Evaluate(List<GhostKey> keys, float t, out float x, out float z, out float yaw, out GhostPose pose)
        {
            x = z = yaw = 0f;
            pose = GhostPose.Hidden;
            if (keys == null || keys.Count == 0) return false;
            GhostKey a = keys[0], b = keys[0];
            if (t <= keys[0].T) { a = b = keys[0]; }
            else if (t >= keys[keys.Count - 1].T) { a = b = keys[keys.Count - 1]; }
            else
            {
                for (int i = 0; i < keys.Count - 1; i++)
                {
                    if (t >= keys[i].T && t < keys[i + 1].T) { a = keys[i]; b = keys[i + 1]; break; }
                }
            }
            float span = b.T - a.T;
            float u = span > 1e-5f ? Ease(Mathf.Clamp01((t - a.T) / span)) : 1f;
            x = Mathf.Lerp(a.X, b.X, u);
            z = Mathf.Lerp(a.Z, b.Z, u);
            float dx = b.X - a.X, dz = b.Z - a.Z;
            bool moving = dx * dx + dz * dz > 0.05f * 0.05f && u < 1f;
            bool lying = a.Pose == GhostPose.Lying && b.Pose == GhostPose.Lying;
            if (moving && !lying)
            {
                pose = a.Pose == GhostPose.Carry || b.Pose == GhostPose.Carry ? GhostPose.WalkCarry : GhostPose.Walk;
                yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            }
            else
            {
                pose = u >= 1f ? b.Pose : a.Pose;
                yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, u);
            }
            return true;
        }
    }
}
