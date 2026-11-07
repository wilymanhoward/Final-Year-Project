using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// FYP > Set Up Crime Replay Cast: reimports Art/Characters with <see cref="ReplayCharacterImport"/> and writes
    /// Resources/ReplayCast.asset (the three characters and one Mixamo motion per <see cref="GhostPose"/>), which
    /// CrimeReplay loads at run time. Idempotent; part of FYP > Set Up Everything.
    /// </summary>
    public static class ReplayCastTool
    {
        private const string AssetPath = "Assets/_Project/Resources/ReplayCast.asset";

        // Motion file (Art/Characters/Animations/<name>.fbx) per pose. Lying has none: it keeps the fall's last frame.
        private static readonly Dictionary<GhostPose, string> MotionFiles = new Dictionary<GhostPose, string>
        {
            { GhostPose.Stand, "Idle" }, { GhostPose.Walk, "WalkCareful" }, { GhostPose.WalkCarry, "WalkHolding" },
            { GhostPose.Run, "Run" }, { GhostPose.Work, "ButtonPush" }, { GhostPose.Pour, "Pouring" },
            { GhostPose.Drink, "SitDrinking" }, { GhostPose.Reach, "PickUp" }, { GhostPose.PutDown, "PutDown" },
            { GhostPose.Carry, "Idle" }, { GhostPose.Smash, "SmashDown" }, { GhostPose.Stab, "Stab" },
            { GhostPose.Struggle, "FistFight" }, { GhostPose.Argue, "Arguing" }, { GhostPose.HitReact, "HitStomach" },
            { GhostPose.Kneel, "KneelDown" }, { GhostPose.Sit, "SitIdle" }, { GhostPose.SitTyping, "SitTyping" },
            { GhostPose.SitToStand, "SitToStand" }, { GhostPose.Dizzy, "Dizzy" },
            { GhostPose.Collapse, "PoisonCollapse" },
            { GhostPose.FallBack, "FallBackDeath" },
        };

        [MenuItem("FYP/Set Up Crime Replay Cast")]
        public static void SetUp()
        {
            AssetDatabase.ImportAsset(ReplayCharacterImport.Folder.TrimEnd('/'),
                ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            var missing = new List<string>();
            GameObject Model(string file)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(ReplayCharacterImport.ModelFolder + file + ".fbx");
                if (go == null) missing.Add("Models/" + file + ".fbx");
                return go;
            }
            AnimationClip Clip(string file)
            {
                AnimationClip c = AssetDatabase.LoadAllAssetsAtPath(ReplayCharacterImport.AnimationFolder + file + ".fbx")
                    .OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview__"));
                if (c == null) missing.Add("Animations/" + file + ".fbx");
                return c;
            }

            var cast = AssetDatabase.LoadAssetAtPath<ReplayCast>(AssetPath);
            if (cast == null)
            {
                cast = ScriptableObject.CreateInstance<ReplayCast>();
                AssetDatabase.CreateAsset(cast, AssetPath);
            }
            cast.woman = Model("Women_Suit");
            cast.manSuit = Model("Men_Suit");
            cast.manCasual = Model("Men_Casual2");
            cast.walkFemale = Clip("WalkFemale");
            cast.motions = MotionFiles.Select(p => new ReplayCast.Motion { pose = p.Key, clip = Clip(p.Value) }).ToArray();
            EditorUtility.SetDirty(cast);
            AssetDatabase.SaveAssets();

            SetupDialogs.Show("Crime replay cast", missing.Count == 0
                ? $"ReplayCast ready: 3 characters, {cast.motions.Length} motions + the women's walk ({AssetPath})."
                : "ReplayCast written, but these files are missing in Art/Characters:\n" + string.Join("\n", missing.Distinct()), "OK");
        }
    }
}
