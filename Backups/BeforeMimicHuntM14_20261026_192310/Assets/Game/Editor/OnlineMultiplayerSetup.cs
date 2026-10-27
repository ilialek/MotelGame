using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Networking;

namespace MotelGame.Editor
{
    // Explicit requests only. No automatic scene changes on domain reload.
    [InitializeOnLoad]
    public static class OnlineMultiplayerSetup
    {
        public const string Reports = "Documentation/OnlineMultiplayerM13";
        static double nextPoll;
        static OnlineMultiplayerSetup() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (BuildPipeline.isBuildingPlayer || File.Exists("Library/MotelServiceRefresh.request") || EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            try
            {
                if (File.Exists("Library/MotelOnlineSetup.request"))
                { File.Delete("Library/MotelOnlineSetup.request"); Install(); }
                if (File.Exists("Library/MotelOnlineWindowsBuild.request"))
                { File.Delete("Library/MotelOnlineWindowsBuild.request"); PlaytestBuild.BuildWindows(); }
                if (File.Exists("Library/MotelOnlineCandidate.request"))
                { File.Delete("Library/MotelOnlineCandidate.request"); PlaytestBuild.BuildTo("Builds/Playtest/Candidate"); }
                if (File.Exists("Library/MotelOnlineBuild.request"))
                { File.Delete("Library/MotelOnlineBuild.request"); PlaytestBuild.Build(); }
            }
            catch (Exception e) { Directory.CreateDirectory(Reports); File.WriteAllText(Reports + "/editor-error.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/Motel/Online/Configure Current Scene")]
        public static void Install()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save or discard your scene changes before configuring online play.");
            if (scene.path != MultipleGuestsSetup.ScenePath) scene = EditorSceneManager.OpenScene(MultipleGuestsSetup.ScenePath);
            string original = File.ReadAllText(scene.path);
            var local = UnityEngine.Object.FindFirstObjectByType<DevelopmentSession>();
            if (local == null || UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None).Length != 1)
                throw new InvalidOperationException("Expected the existing single NetworkSession.");
            if (local.GetComponent<OnlineSessionController>() == null) local.gameObject.AddComponent<OnlineSessionController>();
            if (local.GetComponent<OnlineSessionMenu>() == null) local.gameObject.AddComponent<OnlineSessionMenu>();
            // Approval owns version checks so rejected clients receive a readable reason.
            local.Manager.NetworkConfig.ClientConnectionBufferTimeout = 30;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // Unity may fill existing empty TextMesh renderer materials while saving unrelated components.
            // Preserve every original renderer record: M13 does not edit presentation.
            string saved = File.ReadAllText(scene.path);
            const string pattern = @"(?ms)^--- !u!(?:23|137) &\d+\n.*?(?=^--- !u!|\z)";
            var renderers = Regex.Matches(original, pattern).Cast<Match>().ToDictionary(m => m.Value.Split('\n')[0], m => m.Value);
            saved = Regex.Replace(saved, pattern, m => renderers.TryGetValue(m.Value.Split('\n')[0], out string record) ? record : m.Value);
            File.WriteAllText(scene.path, saved);
            Validate();
        }
        [MenuItem("Tools/Motel/Online/Validate Configuration")]
        public static void Validate()
        {
            var local = UnityEngine.Object.FindFirstObjectByType<DevelopmentSession>();
            if (local == null || local.GetComponent<OnlineSessionController>() == null || local.GetComponent<OnlineSessionMenu>() == null ||
                !local.Manager.NetworkConfig.ConnectionApproval || local.Manager.NetworkConfig.PlayerPrefab == null ||
                UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None).Length != 1)
                throw new InvalidOperationException("Incomplete online scene configuration.");
            Directory.CreateDirectory(Reports);
            File.WriteAllText(Reports + "/setup-validation.txt", "PASS: single existing NetworkManager, approval, registered player prefab, online controller/menu.\n" + DateTime.UtcNow.ToString("O"));
        }
    }
}
