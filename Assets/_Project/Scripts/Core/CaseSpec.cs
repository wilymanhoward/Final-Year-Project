using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>Height band of a clue (docs/cases.md): floor, table or eye level.</summary>
    public enum HeightBand
    {
        Floor = 0,
        Table = 1,
        Eye = 2,
    }

    /// <summary>Clue role in the shared case structure: K1 time, K2 access, K3 trace, K4 motive, R1-R3 red herrings.</summary>
    public enum ClueSlot
    {
        K1 = 0, K2 = 1, K3 = 2, K4 = 3,
        R1 = 4, R2 = 5, R3 = 6,
    }

    /// <summary>Room constants shared by the case specs, the validator and the greybox generator.</summary>
    public static class PlayArea
    {
        /// <summary>Half of the playable square: 5 x 5 m (user, session 6; was 4 x 4 m). Furniture stands near its edges.</summary>
        public const float HalfSize = 2.5f;

        public const float FloorHeight = 0.03f;
        public const float TableHeight = 0.75f;
        public const float EyeHeight = 1.5f;

        public static float Height(HeightBand band) =>
            band == HeightBand.Eye ? EyeHeight : band == HeightBand.Table ? TableHeight : FloorHeight;

        public static bool IsKey(ClueSlot slot) => slot <= ClueSlot.K4;
    }

    /// <summary>A suspect card on the evidence board (not a clue, not taggable).</summary>
    public sealed class SuspectSpec
    {
        public string Id;
        public string Name;
        public string Role;
        public string Alibi;
        public bool IsCulprit;
    }

    /// <summary>
    /// One clue. Position is CaseRoot-local (x right, z towards the front wall), metres. <see cref="FacingYaw"/> is
    /// its natural side: where its readable side faces and where people stand to read or take it (degrees from +z
    /// towards +x); static clues are flat and poked, the others are grabbed.
    /// </summary>
    public sealed class ClueSpec
    {
        public ClueSlot Slot;
        public string Id;
        public string ObjectName;
        public string InspectText;
        public HeightBand Height;
        public float X;
        public float Z;
        public float FacingYaw;
        public bool Grabbable;
        /// <summary>Red herrings: id of the innocent suspect it points at (null for key evidence).</summary>
        public string PointsAtSuspectId;
    }

    /// <summary>Invisible exclusion zone (rectangle on the floor, yaw in degrees) with a visible floor marker.</summary>
    public sealed class ZoneSpec
    {
        public string Id;
        public string Name;
        public float X;
        public float Z;
        public float Width;
        public float Depth;
        public float Yaw;
    }

    /// <summary>
    /// Furniture: centre x/z and footprint (axis-aligned, after turning), height, and <see cref="Yaw"/> = the direction
    /// its front faces (degrees from +z towards +x). Used for the validator (standing spots can't be inside furniture)
    /// and by the scene generator (where the mesh goes and which way it faces).
    /// </summary>
    public sealed class PropSpec
    {
        /// <summary>Role shared by both cases ("display", "desk", "office", "stand", "door"), used by the mirror rule.</summary>
        public string Role;
        public string Name;
        public float X;
        public float Z;
        public float Width;
        public float Depth;
        public float Height;
        public float Yaw;
    }

    /// <summary>One study case: text content (docs/cases.md) plus its greybox layout.</summary>
    public sealed class CaseSpec
    {
        public CaseId Case;
        public string Title;
        public string Briefing;
        public List<SuspectSpec> Suspects = new List<SuspectSpec>();
        public List<ClueSpec> Clues = new List<ClueSpec>();
        public List<ZoneSpec> Zones = new List<ZoneSpec>();
        public List<PropSpec> Props = new List<PropSpec>();
        public float BoardX;
        public float BoardZ;
        public float BoardYaw;

        public SuspectSpec Culprit => Suspects.Find(s => s.IsCulprit);
        public ClueSpec Clue(ClueSlot slot) => Clues.Find(c => c.Slot == slot);
        public PropSpec Prop(string role) => Props.Find(p => p.Role == role);
    }
}
