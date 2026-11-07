using System.Collections.Generic;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>What a replay character is doing; each pose plays one Mixamo motion (ReplayCast).</summary>
    public enum GhostPose
    {
        Stand, Walk, WalkCarry, Run, Work, Pour, Drink, Reach, PutDown, Carry, Smash, Stab, Struggle, Argue, HitReact,
        Kneel, Sit, SitTyping, SitToStand, Dizzy, Collapse, FallBack,
        /// <summary>Stays as the last fall left him.</summary>
        Lying,
        Hidden,
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
        /// <summary>The culprit's right hand is guided onto this clue between ReachFrom and ReachTo (fractions of the beat),
        /// e.g. the keypad: the motion alone presses at chest height (user, 2026-11-06: "the hand was too low").</summary>
        public string ReachClueId;
        public float ReachFrom, ReachTo;
    }

    /// <summary>
    /// The crime replay after the verdict (supervisor + user, 2026-11-05, Phase 2): per case the beats that match the
    /// detective's reveal lines (X_reveal_01..05 / Y_reveal_01..05). Pure data + evaluation, unit tested; CrimeReplay
    /// plays it with the ReplayCast characters.
    /// </summary>
    public static class RevealScript
    {
        public static List<RevealBeat> For(CaseId id) => id == CaseId.X ? CaseX() : CaseY();

        private static GhostKey K(float t, float x, float z, float yaw, GhostPose pose) => new GhostKey(t, x, z, yaw, pose);

        // Case X "Last Coffee at Kite" (docs/cases.md): Rizal works on the front-desk chair (where it stands now, CaseSpecs
        // XChair*). Grace comes in from the rain at 21:35, signs in, makes the coffee with her pills, puts it down for him;
        // he drinks, gets up, staggers and falls onto the taped outline. At 21:42 she keys her code, takes the award,
        // smashes the glass, kneels to hide her mug under the stand and leaves carrying the award.
        private static List<RevealBeat> CaseX()
        {
            float cx = CaseSpecs.XChairX, cz = CaseSpecs.XChairZ, cyaw = CaseSpecs.XChairYaw;
            // Poisoned, not knocked out (user, 2026-11-07: the boxing knock-out looked like a fight): he sways (Mixamo
            // "Dizzy Idle") and slumps over backwards clutching his chest ("Death From Standing Idle"). Measured at 1.72 m:
            // the hips end 0.89 m behind and 0.34 m left of where he stood, the head 169 deg right of his facing. Standing
            // here, turned fallYaw, he lands on the taped outline (hips at (0.24, 1.33), head towards -x).
            const float fallX = 1.05f, fallZ = 0.83f, fallYaw = 101.0f;
            const float signX = 1.36f, signZ = 0.45f, coffeeX = 1.48f, coffeeZ = 0.26f;

            var b1 = new RevealBeat { Line = "X_reveal_01", TimeLabel = "21:35" };   // in from the rain, signs in
            b1.Culprit.Add(K(0.00f, 1.62f, -2.05f, 0f, GhostPose.Stand));
            b1.Culprit.Add(K(0.10f, 1.62f, -2.05f, 0f, GhostPose.Stand));
            b1.Culprit.Add(K(0.60f, signX, signZ, 90f, GhostPose.Work));
            b1.Culprit.Add(K(1.00f, signX, signZ, 90f, GhostPose.Work));
            b1.Victim.Add(K(0.00f, cx, cz, cyaw, GhostPose.SitTyping));
            b1.Glows.Add(new ClueGlow(0.65f, "X_K2"));
            b1.ReachClueId = "X_K2";   // the pen on the sign-in sheet
            b1.ReachFrom = 0.62f;
            b1.ReachTo = 0.98f;

            var b2 = new RevealBeat { Line = "X_reveal_02" };   // the coffee, pills from her BEST MANAGER mug
            b2.Culprit.Add(K(0.00f, signX, signZ, 90f, GhostPose.Work));
            b2.Culprit.Add(K(0.22f, coffeeX, coffeeZ, 90f, GhostPose.Pour));
            b2.Culprit.Add(K(1.00f, coffeeX, coffeeZ, 90f, GhostPose.Pour));
            b2.Victim.Add(K(0.00f, cx, cz, cyaw, GhostPose.SitTyping));
            b2.Glows.Add(new ClueGlow(0.45f, "X_K3"));

            var b3 = new RevealBeat { Line = "X_reveal_03" };   // he drinks, gets up, falls
            b3.Culprit.Add(K(0.00f, coffeeX, coffeeZ, 90f, GhostPose.Pour));
            b3.Culprit.Add(K(0.12f, 1.35f, 0.82f, 20f, GhostPose.PutDown));
            b3.Culprit.Add(K(0.30f, 1.35f, 0.82f, 20f, GhostPose.PutDown));
            b3.Culprit.Add(K(0.40f, 1.50f, 0.35f, -40f, GhostPose.Stand));
            b3.Culprit.Add(K(1.00f, 1.50f, 0.35f, -40f, GhostPose.Stand));
            // The line is ~7 s: the collapse may finish at the start of the next beat (Lying lets a fall play out).
            b3.Victim.Add(K(0.00f, cx, cz, cyaw, GhostPose.SitTyping));
            b3.Victim.Add(K(0.05f, cx, cz, cyaw, GhostPose.Drink));
            b3.Victim.Add(K(0.30f, cx, cz, cyaw, GhostPose.SitToStand));
            b3.Victim.Add(K(0.60f, cx, cz, cyaw, GhostPose.SitToStand));
            b3.Victim.Add(K(0.70f, fallX, fallZ, fallYaw, GhostPose.Dizzy));
            b3.Victim.Add(K(0.80f, fallX, fallZ, fallYaw, GhostPose.Collapse));
            b3.Victim.Add(K(1.00f, fallX, fallZ, fallYaw, GhostPose.Collapse));

            var b4 = new RevealBeat { Line = "X_reveal_04", TimeLabel = "21:42" };   // her code on the keypad, the award
            b4.Culprit.Add(K(0.00f, 1.50f, 0.35f, -40f, GhostPose.Stand));
            b4.Culprit.Add(K(0.10f, 0.60f, 0.40f, -90f, GhostPose.Stand));
            b4.Culprit.Add(K(0.22f, -0.25f, 0.90f, 0f, GhostPose.Stand));
            b4.Culprit.Add(K(0.32f, -0.22f, 1.78f, -90f, GhostPose.Work));
            b4.Culprit.Add(K(0.60f, -0.22f, 1.78f, -90f, GhostPose.Work));
            b4.Culprit.Add(K(0.70f, -0.75f, 1.40f, 0f, GhostPose.Reach));
            b4.Culprit.Add(K(0.92f, -0.75f, 1.40f, 0f, GhostPose.Carry));
            b4.Culprit.Add(K(1.00f, -0.75f, 1.40f, 0f, GhostPose.Carry));
            b4.Victim.Add(K(0.00f, fallX, fallZ, fallYaw, GhostPose.Lying));
            b4.Glows.Add(new ClueGlow(0.36f, "X_K1"));
            b4.ReachClueId = "X_K1";
            b4.ReachFrom = 0.32f;
            b4.ReachTo = 0.60f;

            var b5 = new RevealBeat { Line = "X_reveal_05" };   // smashes the glass, hides the mug, leaves; the motive
            b5.Culprit.Add(K(0.00f, -0.75f, 1.40f, 0f, GhostPose.Smash));
            b5.Culprit.Add(K(0.24f, -0.75f, 1.40f, 0f, GhostPose.Smash));
            b5.Culprit.Add(K(0.30f, -0.69f, 1.22f, 0f, GhostPose.Kneel));
            b5.Culprit.Add(K(0.50f, -0.69f, 1.22f, 0f, GhostPose.Kneel));
            b5.Culprit.Add(K(0.58f, -0.69f, 1.22f, 140f, GhostPose.Carry));
            b5.Culprit.Add(K(0.66f, 0.00f, 0.30f, 140f, GhostPose.Carry));
            b5.Culprit.Add(K(0.80f, 1.35f, -1.55f, 150f, GhostPose.Carry));
            b5.Culprit.Add(K(0.90f, 1.62f, -2.05f, 180f, GhostPose.Carry));
            b5.Culprit.Add(K(1.00f, 1.62f, -2.05f, 180f, GhostPose.Carry));
            b5.Victim.Add(K(0.00f, fallX, fallZ, fallYaw, GhostPose.Lying));
            b5.Glows.Add(new ClueGlow(0.38f, "X_K3"));
            b5.Glows.Add(new ClueGlow(0.88f, "X_K4"));

            return new List<RevealBeat> { b1, b2, b3, b4, b5 };
        }

        // Case Y "Death in the Melaka Room" (docs/cases.md): Farid signs the key out at 20:05; the Datuk waits in the room
        // (he had emailed Farid to meet at eight). They argue and fight - the vase goes over, the clock stops at 8:17.
        // Farid takes the keris from the plinth and stabs him; the Datuk falls back where the taped outline is (head
        // towards -x), tearing a scrap off Farid's scarf. Farid runs along the bloody footprints to the entrance.
        private static List<RevealBeat> CaseY()
        {
            // Measured Mixamo "Falling Back Death" (1.72 m): hips end 0.79 m behind and 0.23 m right, the head 0.64 m further.
            // Standing here, turned datukYaw, the Datuk lands on the taped outline (hips at (-0.16, 1.25), head towards -x).
            const float datukX = 0.66f, datukZ = 1.22f, datukYaw = 108.1f;
            // Farid faces him from 0.6 m in front.
            const float faridX = 1.23f, faridZ = 1.03f, faridYaw = -71.9f;

            var b1 = new RevealBeat { Line = "Y_reveal_01", TimeLabel = "20:05" };   // signs the key out
            b1.Culprit.Add(K(0.00f, -1.62f, -2.05f, 0f, GhostPose.Stand));
            b1.Culprit.Add(K(0.10f, -1.62f, -2.05f, 0f, GhostPose.Stand));
            b1.Culprit.Add(K(0.55f, 1.36f, 0.56f, 90f, GhostPose.Work));
            b1.Culprit.Add(K(0.80f, 1.36f, 0.56f, 90f, GhostPose.Reach));
            b1.Culprit.Add(K(1.00f, 1.36f, 0.56f, 90f, GhostPose.Reach));
            b1.Victim.Add(K(0.00f, datukX, datukZ, datukYaw, GhostPose.Hidden));
            b1.Glows.Add(new ClueGlow(0.6f, "Y_K2"));
            b1.ReachClueId = "Y_K2";   // signing the logbook
            b1.ReachFrom = 0.55f;
            b1.ReachTo = 0.80f;

            var b2 = new RevealBeat { Line = "Y_reveal_02" };   // the meeting he asked for
            b2.Culprit.Add(K(0.00f, 1.36f, 0.56f, 90f, GhostPose.Stand));
            b2.Culprit.Add(K(0.40f, faridX, faridZ, faridYaw, GhostPose.Stand));
            b2.Culprit.Add(K(1.00f, faridX, faridZ, faridYaw, GhostPose.Stand));
            b2.Victim.Add(K(0.00f, datukX, datukZ, datukYaw, GhostPose.Stand));
            b2.Victim.Add(K(1.00f, datukX, datukZ, datukYaw, GhostPose.Stand));
            b2.Glows.Add(new ClueGlow(0.3f, "Y_K4"));

            var b3 = new RevealBeat { Line = "Y_reveal_03", TimeLabel = "20:17" };   // argument, fight, vase, clock
            b3.Culprit.Add(K(0.00f, faridX, faridZ, faridYaw, GhostPose.Argue));
            b3.Culprit.Add(K(0.45f, faridX, faridZ, faridYaw, GhostPose.Struggle));
            b3.Culprit.Add(K(1.00f, faridX, faridZ, faridYaw, GhostPose.Struggle));
            b3.Victim.Add(K(0.00f, datukX, datukZ, datukYaw, GhostPose.Argue));
            b3.Victim.Add(K(0.45f, datukX, datukZ, datukYaw, GhostPose.Struggle));
            b3.Victim.Add(K(1.00f, datukX, datukZ, datukYaw, GhostPose.Struggle));
            b3.Glows.Add(new ClueGlow(0.75f, "Y_K1"));

            var b4 = new RevealBeat { Line = "Y_reveal_04" };   // the keris from the plinth, the stab
            b4.Culprit.Add(K(0.00f, faridX, faridZ, faridYaw, GhostPose.Stand));
            b4.Culprit.Add(K(0.15f, 0.94f, 1.58f, 0f, GhostPose.Reach));
            b4.Culprit.Add(K(0.35f, 0.94f, 1.58f, 0f, GhostPose.Reach));
            b4.Culprit.Add(K(0.48f, faridX, faridZ, faridYaw, GhostPose.Stab));
            b4.Culprit.Add(K(1.00f, faridX, faridZ, faridYaw, GhostPose.Stab));
            b4.Victim.Add(K(0.00f, datukX, datukZ, datukYaw, GhostPose.Stand));
            b4.Victim.Add(K(0.52f, datukX, datukZ, datukYaw, GhostPose.HitReact));
            b4.Victim.Add(K(0.70f, datukX, datukZ, datukYaw, GhostPose.FallBack));
            b4.Victim.Add(K(1.00f, datukX, datukZ, datukYaw, GhostPose.FallBack));

            var b5 = new RevealBeat { Line = "Y_reveal_05" };   // the torn scarf, the escape along the footprints
            b5.Culprit.Add(K(0.00f, faridX, faridZ, faridYaw, GhostPose.Stand));
            b5.Culprit.Add(K(0.25f, faridX, faridZ, faridYaw, GhostPose.Stand));
            b5.Culprit.Add(K(0.40f, 0.30f, 0.50f, -120f, GhostPose.Run));
            b5.Culprit.Add(K(0.55f, -0.90f, 0.60f, -150f, GhostPose.Run));
            b5.Culprit.Add(K(0.72f, -1.35f, -0.60f, 180f, GhostPose.Run));
            b5.Culprit.Add(K(0.88f, -1.62f, -2.05f, 180f, GhostPose.Run));
            b5.Culprit.Add(K(1.00f, -1.62f, -2.05f, 180f, GhostPose.Stand));
            b5.Victim.Add(K(0.00f, datukX, datukZ, datukYaw, GhostPose.Lying));
            b5.Glows.Add(new ClueGlow(0.15f, "Y_K3"));

            return new List<RevealBeat> { b1, b2, b3, b4, b5 };
        }

        /// <summary>Smooth step 0..1 (eases in and out).</summary>
        private static float Ease(float u) => u * u * (3f - 2f * u);

        /// <summary>
        /// Where a ghost is at fraction <paramref name="t"/> of a beat: position eased between keys; while it moves between
        /// two different spots it walks (runs / carries if either key does) facing the way it goes, otherwise it holds the
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
            float raw = span > 1e-5f ? Mathf.Clamp01((t - a.T) / span) : 1f;
            float dx0 = b.X - a.X, dz0 = b.Z - a.Z;
            // Walking goes at a constant speed (the steps can match it); turning on the spot eases.
            float u = dx0 * dx0 + dz0 * dz0 > 0.05f * 0.05f ? raw : Ease(raw);
            x = Mathf.Lerp(a.X, b.X, u);
            z = Mathf.Lerp(a.Z, b.Z, u);
            float dx = b.X - a.X, dz = b.Z - a.Z;
            bool moving = dx * dx + dz * dz > 0.05f * 0.05f && u < 1f;
            if (moving)
            {
                pose = a.Pose == GhostPose.Run || b.Pose == GhostPose.Run ? GhostPose.Run
                    : a.Pose == GhostPose.Carry || b.Pose == GhostPose.Carry ? GhostPose.WalkCarry : GhostPose.Walk;
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
