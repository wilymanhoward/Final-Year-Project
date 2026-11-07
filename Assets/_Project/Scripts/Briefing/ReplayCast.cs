using System;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// The characters and motions of the crime replay (user, 2026-11-06): Quaternius low-poly people (CC0) and Mixamo
    /// motions, both Humanoid. Built by FYP > Set Up Crime Replay Cast into Resources/ReplayCast.asset, so CrimeReplay
    /// (which creates itself at startup) can load it without scene setup.
    /// </summary>
    [CreateAssetMenu(menuName = "FYP/Replay Cast")]
    public class ReplayCast : ScriptableObject
    {
        /// <summary>One motion per pose.</summary>
        [Serializable]
        public struct Motion
        {
            public GhostPose pose;
            public AnimationClip clip;
        }

        [Tooltip("Case X culprit (Grace).")] public GameObject woman;
        [Tooltip("Case X victim (Rizal) and Case Y victim (the Datuk).")] public GameObject manSuit;
        [Tooltip("Case Y culprit (Farid).")] public GameObject manCasual;
        [Tooltip("The women's walk; the men use the Walk motion.")] public AnimationClip walkFemale;
        public Motion[] motions = new Motion[0];

        /// <summary>The motion for a pose (null if none).</summary>
        public AnimationClip Clip(GhostPose pose)
        {
            foreach (var m in motions) if (m.pose == pose) return m.clip;
            return null;
        }

        /// <summary>Character of the culprit / victim of a case.</summary>
        public GameObject Culprit(CaseId id) => id == CaseId.X ? woman : manCasual;
        public GameObject Victim(CaseId id) => manSuit;
    }
}
