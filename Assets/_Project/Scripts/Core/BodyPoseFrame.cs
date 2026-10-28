using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>One hand of a <see cref="BodyPoseFrame"/>: wrist pose (CaseRoot-local), hand scale, finger joints.</summary>
    public sealed class HandFrame
    {
        public bool Tracked;
        public Vector3 WristPosition;
        public Quaternion WristRotation = Quaternion.identity;
        public float Scale = 1f;

        /// <summary>Local rotations of the hand model's joints, in the hand visual's joint order.</summary>
        public readonly List<Quaternion> Joints = new List<Quaternion>();
    }

    /// <summary>
    /// The team partner's body for the partner marker (user, session 6): head (CaseRoot-local pose) and both
    /// hands (wrist pose + every finger joint), sent ~15 times a second. Compact binary (~0.5 KB): floats for
    /// positions, rotations as 4 shorts. Pure C#, unit tested; the networking layer sends the bytes.
    /// </summary>
    public sealed class BodyPoseFrame
    {
        private const byte Version = 1;
        public const int MaxJoints = 40;
        public const int MaxBytes = 2048;

        public bool HasHead;
        public Vector3 HeadPosition;
        public Quaternion HeadRotation = Quaternion.identity;
        public readonly HandFrame Left = new HandFrame();
        public readonly HandFrame Right = new HandFrame();

        private readonly MemoryStream _out = new MemoryStream(MaxBytes);
        private BinaryWriter _writer;

        /// <summary>Encodes the frame. The returned buffer is reused: send its first <paramref name="length"/> bytes now.</summary>
        public byte[] Write(out int length)
        {
            if (_writer == null) _writer = new BinaryWriter(_out);
            _out.Position = 0;
            var w = _writer;
            w.Write(Version);
            w.Write((byte)((HasHead ? 1 : 0) | (Left.Tracked ? 2 : 0) | (Right.Tracked ? 4 : 0)));
            if (HasHead) { WriteVector(w, HeadPosition); WriteRotation(w, HeadRotation); }
            if (Left.Tracked) WriteHand(w, Left);
            if (Right.Tracked) WriteHand(w, Right);
            w.Flush();
            length = (int)_out.Position;
            return _out.GetBuffer();
        }

        /// <summary>Decodes into this frame. False (frame unchanged in meaning) if the bytes are malformed.</summary>
        public bool TryRead(byte[] data, int length)
        {
            if (data == null || length < 2 || length > MaxBytes || length > data.Length) return false;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(data, 0, length, false)))
                {
                    if (r.ReadByte() != Version) return false;
                    byte flags = r.ReadByte();
                    HasHead = (flags & 1) != 0;
                    if (HasHead) { HeadPosition = ReadVector(r); HeadRotation = ReadRotation(r); }
                    Left.Tracked = (flags & 2) != 0;
                    Right.Tracked = (flags & 4) != 0;
                    if (Left.Tracked && !ReadHand(r, Left)) return false;
                    if (Right.Tracked && !ReadHand(r, Right)) return false;
                    return IsFinite(HeadPosition) && IsFinite(Left.WristPosition) && IsFinite(Right.WristPosition);
                }
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        /// <summary>
        /// Turns the whole body 180 degrees about the vertical line through <paramref name="pivot"/> (CaseRoot-local), so a
        /// copy of yourself stands in front of you, facing you (debug "show me as partner"). Finger joints are local and stay.
        /// </summary>
        public void TurnAround(Vector3 pivot)
        {
            if (HasHead) { HeadPosition = Turn(HeadPosition, pivot); HeadRotation = HalfTurn * HeadRotation; }
            TurnHand(Left, pivot);
            TurnHand(Right, pivot);
        }

        private static void TurnHand(HandFrame h, Vector3 pivot)
        {
            if (!h.Tracked) return;
            h.WristPosition = Turn(h.WristPosition, pivot);
            h.WristRotation = HalfTurn * h.WristRotation;
        }

        private static readonly Quaternion HalfTurn = Quaternion.Euler(0f, 180f, 0f);
        private static Vector3 Turn(Vector3 p, Vector3 pivot) => new Vector3(2f * pivot.x - p.x, p.y, 2f * pivot.z - p.z);

        private static void WriteHand(BinaryWriter w, HandFrame h)
        {
            WriteVector(w, h.WristPosition);
            WriteRotation(w, h.WristRotation);
            w.Write(h.Scale);
            int n = Math.Min(h.Joints.Count, MaxJoints);
            w.Write((byte)n);
            for (int i = 0; i < n; i++) WriteRotation(w, h.Joints[i]);
        }

        private static bool ReadHand(BinaryReader r, HandFrame h)
        {
            h.WristPosition = ReadVector(r);
            h.WristRotation = ReadRotation(r);
            h.Scale = r.ReadSingle();
            int n = r.ReadByte();
            if (n > MaxJoints || float.IsNaN(h.Scale) || h.Scale <= 0f || h.Scale > 3f) return false;
            h.Joints.Clear();
            for (int i = 0; i < n; i++) h.Joints.Add(ReadRotation(r));
            return true;
        }

        private static void WriteVector(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static Vector3 ReadVector(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteRotation(BinaryWriter w, Quaternion q)
        {
            q = Normalized(q);
            w.Write(ToShort(q.x)); w.Write(ToShort(q.y)); w.Write(ToShort(q.z)); w.Write(ToShort(q.w));
        }

        private static Quaternion ReadRotation(BinaryReader r) =>
            Normalized(new Quaternion(r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f));

        private static short ToShort(float v) => (short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32767f);

        private static Quaternion Normalized(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m < 1e-6f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
