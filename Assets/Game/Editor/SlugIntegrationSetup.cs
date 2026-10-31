using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Horror;

namespace MotelGame.Editor
{
    public static class SlugIntegrationSetup
    {
        const string ScenePath = "Assets/Game/Scenes/Motel_Layout_V4.unity";
        const string PrefabPath = "Assets/Game/Prefabs/SlugMimic.prefab";
        public const string Output = "Builds/SlugIntegration";
        static void Set(UnityEngine.Object o, string name, UnityEngine.Object value)
        { var s = new SerializedObject(o); s.FindProperty(name).objectReferenceValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
        public static void Install()
        {
            if (!Application.dataPath.EndsWith("/MotelGame-SlugIntegration/Assets"))
                throw new InvalidOperationException("Install only in the isolated integration worktree.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var hunt = UnityEngine.Object.FindFirstObjectByType<MimicHunt>();
            if (hunt.GetComponent<MimicSlug>() != null) throw new InvalidOperationException("Already installed");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/SlugMimic/Mimic mottled skin.mat");
            if (material == null || material.mainTexture == null) throw new Exception("Missing Slug material/texture");
            var visual = new GameObject("SlugMimic");
            var body = visual.AddComponent<SlugBody>();
            var renderer = visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.renderingLayerMask = 2;
            var prefab = PrefabUtility.SaveAsPrefabAsset(visual, PrefabPath);
            UnityEngine.Object.DestroyImmediate(visual);
            visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hunt.transform);
            body = visual.GetComponent<SlugBody>(); visual.SetActive(false);
            var old = new SerializedObject(hunt).FindProperty("creature").objectReferenceValue as GameObject;
            old.SetActive(false); // Retain the old scene asset, disabled, for local comparison/rollback.
            UnityEngine.Object.DestroyImmediate(hunt.Body);
            var hitObject = new GameObject("SlugHitbox"); hitObject.transform.SetParent(hunt.transform, false);
            var collider = hitObject.AddComponent<CapsuleCollider>(); collider.direction = 2; collider.radius = .24f;
            collider.height = 1.08f; collider.enabled = false;
            var slug = hunt.gameObject.AddComponent<MimicSlug>();
            Set(slug, "hunt", hunt); Set(slug, "body", body); Set(slug, "hitbox", collider);
            Set(hunt, "slug", slug); Set(hunt, "creature", visual); Set(hunt, "body", collider);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Validate();
        }
        public static void Validate()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene(ScenePath);
            var hunt = UnityEngine.Object.FindFirstObjectByType<MimicHunt>();
            var slug = hunt.GetComponent<MimicSlug>();
            if (slug == null || slug.Body == null || !hunt.ConfigurationValid()) throw new Exception("Slug references/configuration");
            if (!(hunt.Body is CapsuleCollider) || hunt.Body.transform.parent != hunt.transform) throw new Exception("Authoritative front-body hitbox");
            if (slug.Body.GetComponent<Renderer>().sharedMaterial.mainTexture == null) throw new Exception("Missing skin texture");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            if (objects.Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)) throw new Exception("Missing scripts");
            // Existing architectural, registration, service, navigation and network identity checks.
            var reports = new[]{"Documentation/LevelM16/scene-validation.txt", "Documentation/LevelM16/navmesh-grid.txt", "Documentation/LevelM16/sightlines.txt"};
            MotelLayoutV4.Validate();
            var report = File.ReadAllText(reports[0]);
            File.WriteAllText(Output + "/scene-validation.txt", report);
            if (report.Split('\n').Any(l => l.StartsWith("FAIL"))) throw new Exception("Existing scene checks failed");
            SlugMeshValidation.Validate(slug.Body, Output);
            Debug.Log("SLUG_INTEGRATION_VALIDATION_PASS");
        }
        public static void Build()
        {
            Validate();
            if (new DriveInfo(Directory.GetCurrentDirectory()).AvailableFreeSpace < 1500000000L) throw new Exception("Insufficient build space");
            string buildPath = Output + "/Candidate-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "/MotelGame.app";
            var settings = Directory.GetFiles("ProjectSettings").Concat(Directory.GetFiles("Assets/Settings", "*.asset")).ToDictionary(p => p, File.ReadAllBytes);
            try
            {
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[]{ScenePath},
                    locationPathName = buildPath, target = BuildTarget.StandaloneOSX, options = BuildOptions.Development });
                if (result.summary.result != BuildResult.Succeeded) throw new Exception("Slug build: " + result.summary.result);
                File.WriteAllText(Output + "/build-result.txt", result.summary.result + " " + result.summary.totalSize);
                File.WriteAllText(Output + "/latest-build-path.txt", Path.GetFullPath(buildPath));
            }
            finally { foreach (var file in settings) if (!File.ReadAllBytes(file.Key).SequenceEqual(file.Value)) File.WriteAllBytes(file.Key, file.Value); }

        }
    }
}
