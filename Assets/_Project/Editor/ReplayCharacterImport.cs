using System.IO;
using UnityEditor;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Import settings for the crime-replay characters (user, 2026-11-06): Quaternius low-poly people (CC0, the packs'
    /// "Humanoid Rig" versions) in Art/Characters/Models and Mixamo motions ("FBX", without skin, 30 fps) in
    /// Art/Characters/Animations. Everything is Humanoid, so every Mixamo motion plays on every character. Motions are
    /// imported in place: root rotation, height and position are baked into the pose (CrimeReplay moves the character),
    /// so a fall or a kneel still goes down and forward. Loop clips are listed in <see cref="Loops"/>. No materials: the
    /// ghost material replaces them.
    /// </summary>
    public class ReplayCharacterImport : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Art/Characters/";
        public const string ModelFolder = Folder + "Models/";
        public const string AnimationFolder = Folder + "Animations/";

        /// <summary>Motions that repeat while held; the rest play once and keep their last frame.</summary>
        public static readonly string[] Loops =
            { "Idle", "WalkFemale", "WalkCareful", "WalkHolding", "Run", "SitIdle", "SitTyping", "SitDrinking", "ButtonPush",
              "Pouring", "Arguing", "FistFight", "Dizzy" };

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var m = (ModelImporter)assetImporter;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importCameras = false;
            m.importLights = false;
            m.importVisibility = false;
            m.importBlendShapes = false;
            m.addCollider = false;
            m.animationType = ModelImporterAnimationType.Human;
            m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            m.importAnimation = assetPath.StartsWith(AnimationFolder);
            m.optimizeGameObjects = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(AnimationFolder)) return;
            var m = (ModelImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            bool loop = System.Array.IndexOf(Loops, name) >= 0;
            ModelImporterClipAnimation[] clips = m.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = name;
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;      // bake rotation into the pose, keep the original facing
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;       // bake height: sitting / falling / kneeling still go down
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;    // in place: CrimeReplay moves the character
                c.keepOriginalPositionXZ = true;
                if (name == "SitDrinking")
                {
                    // Mixamo's "Having a seat and drinking" walks 2.5 m and sits down first (seated from 4.1 s, measured
                    // 2026-11-06): keep only the seated part. The body stays where he sat in the original motion; CrimeReplay
                    // pulls it back under the root (DrinkOffset).
                    c.firstFrame = 130;
                    c.lastFrame = 456;
                    // He turned round to sit, so the original facing is backwards: take facing and position from the
                    // seated body instead (centred on the root, facing the chair like the other sitting motions).
                    c.keepOriginalOrientation = false;
                    c.keepOriginalPositionXZ = false;
                }
                if (name == "SitToStand") c.lockRootPositionXZ = false;   // root motion: he ends 0.44 m forward, standing
            }
            m.clipAnimations = clips;
        }
    }
}
