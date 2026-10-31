using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using MotelGame.Horror;

namespace MotelGame.Editor
{
    public static class SlugMeshValidation
    {
        static SlugPath Fixture(Vector3[] points)
        {
            var path = new SlugPath();
            var knots = (SlugKnot[])typeof(SlugPath).GetField("knots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(path);
            float distance = 0; Vector3 forward = (points[1] - points[0]).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < points.Length; i++)
            {
                if (i > 0)
                {
                    var next = (points[i] - points[i - 1]).normalized;
                    distance += Vector3.Distance(points[i], points[i - 1]); right = SlugPath.Transport(right, forward, next); forward = next;
                }
                knots[i] = new SlugKnot { Position = points[i], Right = right, Distance = distance, Time = distance };
            }
            typeof(SlugPath).GetProperty("Count").SetValue(path, points.Length); return path;
        }
        public static void Validate(SlugBody body, string output)
        {
            var paths = new[]{
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(6,-2.65f,0)}),
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(0,-2.65f,0),new Vector3(0,-2.65f,6)}),
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(0,-2.65f,0),new Vector3(0,-1.3f,0),new Vector3(4,-1.3f,0)}),
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(0,-2.65f,0),new Vector3(-6,-2.65f,0)}),
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(0,-2.65f,0),new Vector3(-.1f,-2.65f,0),new Vector3(0,-2.65f,0),new Vector3(-6,-2.65f,0)})};
            int poses = 0; float maxGap = 0;
            foreach (var path in paths)
                for (float d = body.bodyLength + .1f; d < path.End; d += .035f)
                {
                    body.Render(path, d); poses++;
                    var vertices = body.Vertices; int stride = body.radialSegments + 1;
                    foreach (var v in vertices)
                        if (!float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z)) throw new Exception("Non-finite Slug vertex");
                    for (int i = 0; i < body.longitudinalRings; i++)
                    {
                        if (Vector3.Distance(vertices[i * stride], vertices[i * stride + body.radialSegments]) > .00001f) throw new Exception("Open UV seam");
                        if (i > 0) maxGap = Mathf.Max(maxGap, Vector3.Distance(body.Centers[i], body.Centers[i - 1]));
                    }
                    if (maxGap > .15f) throw new Exception("Disconnected centerline: " + maxGap);
                }
            var densePoints = new Vector3[SlugPath.Capacity];
            for (int i = 0; i < densePoints.Length; i++) densePoints[i] = i < 48 ?
                new Vector3((i - 48) * .07f, -2.65f, 0) : new Vector3(0, -2.65f, (i - 48) * .07f);
            var performancePath = Fixture(densePoints);
            for (int i = 0; i < 100; i++) body.Render(performancePath, 5.8f + i * .001f);
            var watch = new Stopwatch(); long allocated = GC.GetAllocatedBytesForCurrentThread(); watch.Start();
            for (int i = 0; i < 2400; i++) body.Render(performancePath, 5.8f + (i % 100) * .001f);
            watch.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            File.WriteAllText(output + "/mesh-validation.txt", "PASS continuous capped mesh / finite vertices / welded seam / straight, turn, vertical, reversal\n" +
                "poses=" + poses + " vertices=" + body.BodyMesh.vertexCount + " triangles=" + body.BodyMesh.triangles.Length / 3 +
                " maxCenterGap=" + maxGap + "\n2400 Render calls with full 96-knot trail: mean_us=" + watch.Elapsed.TotalMilliseconds * 1000 / 2400 + " managed_bytes=" + allocated + "\n");
            if (allocated != 0) throw new Exception("Slug deformation allocates " + allocated);
        }
        public static void CaptureMotel()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Game/Scenes/Motel_Layout_V4.unity");
            var hunt = UnityEngine.Object.FindFirstObjectByType<MimicHunt>();
            var body = hunt.GetComponent<MimicSlug>().Body; body.gameObject.SetActive(true);
            VentNode hole = null;
            foreach (var n in hunt.Nodes) if (n.hiddenAnchor != null) { hole = n; break; }
            Vector3 at = hole.transform.position, bend = hole.hiddenBend.position, anchor = hole.hiddenAnchor.position;
            Vector3 approach = (at - hole.neighbours[0].transform.position).normalized;
            Vector3 hidden = (anchor - bend).normalized;
            var path = Fixture(new[]{at - approach * 6, at, bend, anchor + hidden * 4});
            var cameraObject = new GameObject("SlugMotelValidationCamera"); var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox; camera.nearClipPlane = .02f; camera.farClipPlane = 60; camera.fieldOfView = 65;
            cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camera.transform.position = at - approach * 2.4f + Vector3.up * .3f;
            camera.transform.LookAt(at + Vector3.up * .35f);
            var light = new GameObject("ValidationFlashlight").AddComponent<Light>(); light.type = LightType.Spot;
            light.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation); light.range = 9; light.spotAngle = 90; light.intensity = 6;
            light.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().renderingLayers = 2;
            var texture = new RenderTexture(1200,800,24); camera.targetTexture = texture;
            float vertical = Vector3.Distance(at,bend);
            var poses = new[]{5.7f,6.7f,6 + vertical + .35f,6 + vertical + body.bodyLength + Vector3.Distance(anchor,bend) + .75f};
            for (int i = 0; i < poses.Length; i++)
            {
                body.Render(path,poses[i]); camera.Render(); RenderTexture.active = texture;
                var png = new Texture2D(1200,800,TextureFormat.RGB24,false); png.ReadPixels(new Rect(0,0,1200,800),0,0); png.Apply();
                File.WriteAllBytes(SlugIntegrationSetup.Output + "/motel-ingress-" + i + ".png",png.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(png);
            }
            body.gameObject.SetActive(false); camera.Render(); RenderTexture.active = texture;
            var empty = new Texture2D(1200,800,TextureFormat.RGB24,false); empty.ReadPixels(new Rect(0,0,1200,800),0,0); empty.Apply();
            File.WriteAllBytes(SlugIntegrationSetup.Output + "/motel-empty.png",empty.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(empty);
            RenderTexture.active = null; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(light.gameObject);
        }
        public static void Capture()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Game/Scenes/Motel_Layout_V4.unity");
            var body = UnityEngine.Object.FindFirstObjectByType<MimicHunt>().GetComponent<MimicSlug>().Body;
            body.gameObject.SetActive(true); body.gameObject.layer = 31;
            var cameraObject = new GameObject("SlugValidationCamera"); var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.065f,.075f,.08f);
            camera.cullingMask = 1 << 31; camera.nearClipPlane = .02f; camera.farClipPlane = 50;
            camera.fieldOfView = 40;
            var light = new GameObject("SlugValidationLight").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 2; light.transform.rotation = Quaternion.Euler(45,-40,0);
            light.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().renderingLayers = 2;
            var texture = new RenderTexture(1200, 800, 24); camera.targetTexture = texture;
            var shapes = new[]{
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(6,-2.65f,0)}),
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(0,-2.65f,0),new Vector3(0,-2.65f,6)}),
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(0,-2.65f,0),new Vector3(0,-1.3f,0),new Vector3(4,-1.3f,0)}),
                Fixture(new[]{new Vector3(-6,-2.65f,0),new Vector3(0,-2.65f,0),new Vector3(-6,-2.65f,0)})};
            var names = new[]{"straight","corner","vertical","reverse"};
            Directory.CreateDirectory(SlugIntegrationSetup.Output);
            for (int i = 0; i < shapes.Length; i++)
            {
                body.Render(shapes[i], i == 2 ? 7.7f : 7.2f);
                var center = body.GetComponent<Renderer>().bounds.center;
                camera.transform.position = center + new Vector3(2.7f, 2.7f, -4.5f); camera.transform.LookAt(center);
                camera.Render(); RenderTexture.active = texture;
                var png = new Texture2D(1200,800,TextureFormat.RGB24,false); png.ReadPixels(new Rect(0,0,1200,800),0,0); png.Apply();
                File.WriteAllBytes(SlugIntegrationSetup.Output + "/" + names[i] + ".png", png.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(png);
            }
            RenderTexture.active = null; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(light.gameObject);
        }
    }
}
