using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// FYP > Debug: Export UI Sounds (WAV): writes every <see cref="SoundSynth"/> sound to Logs/UiSounds/*.wav (gitignored)
    /// so they can be listened to on the PC. Also runs in batch mode (-executeMethod ...UiSoundExport.Export).
    /// </summary>
    public static class UiSoundExport
    {
        [MenuItem("FYP/Debug: Export UI Sounds (WAV)")]
        public static void Export()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "UiSounds");
            Directory.CreateDirectory(dir);
            foreach (UiSound s in Enum.GetValues(typeof(UiSound)))
                WriteWav(Path.Combine(dir, s + ".wav"), SoundSynth.Make(s), SoundSynth.SampleRate);
            Debug.Log("[UiSoundExport] Wrote the UI sounds to " + dir);
        }

        private static void WriteWav(string path, float[] samples, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (float v in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32767f));
            }
        }
    }
}
