using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// The message boxes of the FYP setup tools. Normally shown one by one; while <see cref="SetUpEverythingTool"/> runs
    /// they are collected instead, so the one-click setup shows a single summary at the end.
    /// </summary>
    internal static class SetupDialogs
    {
        private static List<string> _collected;

        public static bool Show(string title, string message, string ok)
        {
            if (_collected == null) return EditorUtility.DisplayDialog(title, message, ok);
            _collected.Add(title + ": " + message);
            Debug.Log("[Set Up Everything] " + title + ": " + message);
            return true;
        }

        public static void BeginCollecting() => _collected = new List<string>();

        public static List<string> EndCollecting()
        {
            List<string> c = _collected ?? new List<string>();
            _collected = null;
            return c;
        }
    }
}
