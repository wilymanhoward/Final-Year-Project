using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Android builds: allows plain HTTP (android:usesCleartextTraffic="true") so <see cref="DataUploader"/> can send the
    /// run files to the laptop receiver on the local network (user decision, session 6). Unity does this itself for
    /// <c>PlayerSettings.insecureHttpOption = AlwaysAllowed</c> (set by FYP > Set Up Networking); this step makes sure the
    /// generated Gradle manifests (unityLibrary + launcher) carry it too, since the project has a custom
    /// Plugins/Android/AndroidManifest.xml that Meta's tools may regenerate. Runs after the other manifest patches.
    /// Only active while insecureHttpOption is AlwaysAllowed.
    /// </summary>
    public class CleartextHttpBuildStep : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed) return;
            foreach (string manifest in new[]
                     {
                         Path.Combine(path, "src", "main", "AndroidManifest.xml"),
                         Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"),
                     })
            {
                if (File.Exists(manifest)) Patch(manifest);
            }
        }

        private static void Patch(string manifestPath)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(manifestPath);
            if (!(doc.SelectSingleNode("/manifest/application") is XmlElement app)) return;
            if (app.GetAttribute("usesCleartextTraffic", AndroidNs) == "true") return;
            app.SetAttribute("usesCleartextTraffic", AndroidNs, "true");
            doc.Save(manifestPath);
            Debug.Log("[CleartextHttpBuildStep] usesCleartextTraffic=true in " + manifestPath);
        }
    }
}
