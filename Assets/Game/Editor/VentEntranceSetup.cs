using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Unity.Netcode;
using MotelGame.Horror;
using MotelGame.Service;

namespace MotelGame.Editor
{
    [InitializeOnLoad]
    public static class VentEntranceSetup
    {
        const string Reports = "Documentation/VentEntrances", ScenePath = "Assets/Game/Scenes/Motel_Layout_V4.unity";
        const string MeshFolder = "Assets/Game/Geometry/VentEntrances";
        static double next;
        static Material duct, metal;
        static Transform shell;
        static readonly List<Rect> courtyardCuts = new();
        static VentEntranceSetup() => EditorApplication.update += Poll;
        static T[] All<T>(Scene s)
            where T : Component => s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || File.Exists("Library/MotelServiceRefresh.request"))
                return;
            next = EditorApplication.timeSinceStartup + 1;
            foreach (var action in new[]{"Inspect", "Install", "RestoreSavedReferences", "Validate", "Capture", "Build"})
            {
                string path = "Library/MotelVentEntrance" + action + ".request";
                if (!File.Exists(path))
                    continue;
                File.Delete(path);
                Directory.CreateDirectory(Reports);
                try
                {
                    if (action == "Inspect")
                        Inspect();
                    else if (action == "Install")
                        Install();
                    else if (action == "RestoreSavedReferences")
                        RestoreSavedReferences();
                    else if (action == "Validate")
                        UndergroundSetup.Validate();
                    else if (action == "Capture")
                        Capture();
                    else
                        UndergroundSetup.Build();
                    File.WriteAllText(Reports + "/" + action + ".done", DateTime.Now.ToString("O"));
                }
                catch (Exception e)
                {
                    File.WriteAllText(Reports + "/editor-error.txt", e.ToString());
                    Debug.LogException(e);
                }
            }
        }

        static void Inspect()
        {
            var s = SceneManager.GetActiveScene();
            File.WriteAllLines(Reports + "/inspection.txt", new[]{s.path + " dirty=" + s.isDirty}.Concat(All<Collider>(s).Where(c => c.enabled && c.gameObject.activeInHierarchy && c.bounds.min.y < 1.5f && c.bounds.max.z > 4 && c.bounds.min.z < 20 && c.bounds.max.x > 0 && c.bounds.min.x < 18).Select(c => c.transform.parent?.name + "/" + c.name + " " + c.bounds)));
        }

        static void RestoreSavedReferences()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath||scene.isDirty)throw new Exception("Save user changes before restoring original font references.");
            // ExecuteAlways WorldServiceText assigns HideAndDontSave materials in the Editor.
            // Saving the migrated scene nulls their fallback font reference. Keep these unrelated
            // serialized renderer references byte-identical to the backed-up scene.
            string before=File.ReadAllText(Path.Combine(File.ReadAllText(Reports+"/backup-location.txt").Trim(),ScenePath));
            string current=File.ReadAllText(ScenePath);
            const string pattern=@"--- !u!23 &(\d+)\n.*?(?=--- !u!|\z)";
            var originals=Regex.Matches(before,pattern,RegexOptions.Singleline).Cast<Match>().ToDictionary(m=>m.Groups[1].Value,m=>m.Value);
            const string font="  - {fileID: 10100, guid: 0000000000000000e000000000000000, type: 0}";
            int count=0;
            current=Regex.Replace(current,pattern,m=>
            {
                if(!originals.TryGetValue(m.Groups[1].Value,out var old)||!old.Contains(font))return m.Value;
                string restored=m.Value.Replace("  - {fileID: 0}",font);
                if(restored!=m.Value){if(restored!=old)throw new Exception("Unexpected unrelated renderer change");count++;}
                return restored;
            },RegexOptions.Singleline);
            File.WriteAllText(ScenePath,current);EditorSceneManager.OpenScene(ScenePath);
            File.WriteAllText(Reports+"/font-reference-preservation.txt","Restored original serialized references: "+count);
        }

        static void Capture()
        {
            var s = SceneManager.GetActiveScene();
            if (s.path != ScenePath)
                throw new Exception("Open V4");
            foreach (var text in All<WorldServiceText>(s).Where(t => t.enabled))
            {
                text.enabled = false;
                text.enabled = true;
                text.RefreshMaterial();
            }

            var h = All<VentHatch>(s).Single(g => g.room.RoomNumber == "101");
            var go = new GameObject("TemporaryEntranceCapture");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = .03f;
            cam.farClipPlane = 150;
            cam.fieldOfView = 70;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            var torch = go.AddComponent<Light>();
            torch.type = LightType.Spot;
            torch.range = 14;
            torch.spotAngle = 65;
            torch.innerSpotAngle = 35;
            torch.intensity = 3;
            torch.color = new Color(.88f, .92f, 1);
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().renderingLayers = 3;
            var rt = new RenderTexture(1280, 720, 24);
            rt.Create();
            cam.targetTexture = rt;
            var previous = RenderTexture.active;
            void Shot(string name, Vector3 position, Vector3 aim, bool light)
            {
                torch.enabled = light;
                go.transform.position = position;
                go.transform.LookAt(aim);
                Physics.SyncTransforms();
                cam.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Reports + "/" + name + ".png", image.EncodeToPNG());
                Delete(image);
            }

            try
            {
                h.hinge.localRotation = Quaternion.identity;
                Shot("01-grille-closed", h.transform.TransformPoint(new Vector3(2.05f, .78f, 4.7f)), h.transform.TransformPoint(new Vector3(2.25f, .5f, 6.2f)), true);
                h.hinge.localRotation = Quaternion.Euler(-90, 0, 0);
                Shot("02-grille-open", h.upper.position + Vector3.up * .78f, h.transform.TransformPoint(new Vector3(2.25f, .45f, 7.1f)), true);
                Shot("03-ramp-down", h.route[2].position + Vector3.up * .78f, h.route[4].position + Vector3.up * .4f, true);
                Shot("04-ramp-up", h.route[3].position + Vector3.up * .78f, h.transform.TransformPoint(new Vector3(2.25f, .4f, 6.1f)), true);
                Shot("05-return-flight", h.route[7].position + Vector3.up * .78f, h.route[8].position + Vector3.up * .4f, true);
                Shot("06-courtyard", new Vector3(9, 12, 12), new Vector3(9, 0, 11.7f), false);
            }
            finally
            {
                h.hinge.localRotation = Quaternion.identity;
                RenderTexture.active = previous;
                cam.targetTexture = null;
                rt.Release();
                Delete(rt);
                Delete(go);
                Physics.SyncTransforms();
            }
        }

        static Transform Group(string name, Transform parent, Vector3 p = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = p;
            return t;
        }

        static GameObject Box(string name, Transform parent, Vector3 p, Vector3 size, Material mat, uint layer = 2)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = p;
            g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.renderingLayerMask = layer;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return g;
        }

        static void Delete(UnityEngine.Object o) => UnityEngine.Object.DestroyImmediate(o);
        // Exact vertical end faces make adjacent floor slopes meet without stair lips or gaps.
        static void Prism(string name, Transform parent, float x0, float x1, float z0, float z1, float low0, float low1, float high0, float high1, Material mat, uint layer = 2)
        {
            var v = new[]{new Vector3(x0, low0, z0), new Vector3(x1, low0, z0), new Vector3(x1, low1, z1), new Vector3(x0, low1, z1), new Vector3(x0, high0, z0), new Vector3(x1, high0, z0), new Vector3(x1, high1, z1), new Vector3(x0, high1, z1)};
            int[][] faces = {new[]{0, 3, 2, 1}, new[]{4, 5, 6, 7}, new[]{0, 1, 5, 4}, new[]{3, 7, 6, 2}, new[]{0, 4, 7, 3}, new[]{1, 2, 6, 5}};
            // Reverse the indexed quads below for outward normals on the closed prism.
            var verts = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            foreach (var face in faces)
            {
                int start = verts.Count;
                foreach (int i in face)
                    verts.Add(v[i]);
                float w = Vector3.Distance(v[face[0]], v[face[1]]), h = Vector3.Distance(v[face[1]], v[face[2]]);
                uv.AddRange(new[]{Vector2.zero, new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h)});
                tri.AddRange(new[]{start, start + 2, start + 1, start, start + 3, start + 2});
            }

            var mesh = new Mesh{name = name};
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string asset = MeshFolder + "/" + parent.name + "_" + name + ".asset";
            AssetDatabase.CreateAsset(mesh, asset);
            var g = Group(name, parent).gameObject;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshCollider>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.renderingLayerMask = layer;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static void Flight(Transform root, string prefix, float x, float[] z, float[] y, float[] ceiling)
        {
            for (int i = 0; i < z.Length - 1; i++)
            {
                Prism(prefix + "Floor" + i, root, x - .55f, x + .55f, z[i], z[i + 1], y[i] - .12f, y[i + 1] - .12f, y[i], y[i + 1], duct);
                Prism(prefix + "Roof" + i, root, x - .67f, x + .67f, z[i], z[i + 1], ceiling[i], ceiling[i + 1], ceiling[i] + .12f, ceiling[i + 1] + .12f, duct, 3);
                foreach (int side in new[]{-1, 1})
                {
                    float edge = x + side * .61f;
                    Prism(prefix + "Side" + side + "_" + i, root, edge - .06f, edge + .06f, z[i], z[i + 1], y[i] - .12f, y[i + 1] - .12f, ceiling[i] + .12f, ceiling[i + 1] + .12f, duct, 3);
                }
            }
        }

        static Rect WorldRect(Transform t, Rect r)
        {
            var a = t.TransformPoint(new Vector3(r.xMin, 0, r.yMin));
            var b = t.TransformPoint(new Vector3(r.xMax, 0, r.yMax));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }

        static Rect LocalRect(Transform t, Bounds b)
        {
            var a = t.InverseTransformPoint(b.min);
            var c = t.InverseTransformPoint(b.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.z, c.z), Mathf.Max(a.x, c.x), Mathf.Max(a.z, c.z));
        }

        // Subtract a box only from the existing panel it intersects; retain every unrelated panel.
        static void CutBox(GameObject g, Bounds cut)
        {
            var b = g.GetComponent<Collider>().bounds;
            var lo = Vector3.Max(b.min, cut.min);
            var hi = Vector3.Min(b.max, cut.max);
            if (hi.x - lo.x < .0005f || hi.y - lo.y < .0005f || hi.z - lo.z < .0005f)
                return;
            var r = g.GetComponent<Renderer>();
            var mat = r.sharedMaterial;
            uint layer = r.renderingLayerMask;
            var parent = g.transform.parent;
            string name = g.name;
            void Piece(Vector3 a, Vector3 c)
            {
                var size = c - a;
                if (size.x < .0005f || size.y < .0005f || size.z < .0005f)
                    return;
                var n = Box(name + "_Cut", parent, Vector3.zero, size, mat, layer);
                n.transform.position = (a + c) * .5f;
                n.transform.rotation = Quaternion.identity;
            }

            Piece(b.min, new Vector3(lo.x, b.max.y, b.max.z));
            Piece(new Vector3(hi.x, b.min.y, b.min.z), b.max);
            Piece(new Vector3(lo.x, b.min.y, b.min.z), new Vector3(hi.x, lo.y, b.max.z));
            Piece(new Vector3(lo.x, hi.y, b.min.z), new Vector3(hi.x, b.max.y, b.max.z));
            Piece(new Vector3(lo.x, lo.y, b.min.z), new Vector3(hi.x, hi.y, lo.z));
            Piece(new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, hi.y, b.max.z));
            Delete(g);
        }

        static void FlatUnion(Transform root, string name, List<Rect> rects, float floor, List<Rect> existing, Func<Vector2, bool> port)
        {
            var xs = rects.Concat(existing).SelectMany(r => new[]{r.xMin, r.xMax}).Distinct().OrderBy(x => x).ToArray();
            var zs = rects.Concat(existing).SelectMany(r => new[]{r.yMin, r.yMax}).Distinct().OrderBy(z => z).ToArray();
            bool Contains(IEnumerable<Rect> list, Vector2 p) => list.Any(r => r.Contains(p));
            bool Inside(Vector2 p) => Contains(rects, p) || Contains(existing, p) || port(p);
            for (int i = 0; i < xs.Length - 1; i++)
                for (int j = 0; j < zs.Length - 1; j++)
                {
                    float dx = xs[i + 1] - xs[i], dz = zs[j + 1] - zs[j];
                    if (dx < .001f || dz < .001f)
                        continue;
                    var p = new Vector2((xs[i] + xs[i + 1]) * .5f, (zs[j] + zs[j + 1]) * .5f);
                    if (!Contains(rects, p) || Contains(existing, p))
                        continue;
                    Box(name + "Floor", root, new Vector3(p.x, floor - .06f, p.y), new Vector3(dx, .12f, dz), duct);
                    Box(name + "Roof", root, new Vector3(p.x, floor + 1.31f, p.y), new Vector3(dx, .12f, dz), duct);
                    if (!Inside(new Vector2(xs[i] - .005f, p.y)))
                        Box(name + "WallW", root, new Vector3(xs[i] - .06f, floor + .625f, p.y), new Vector3(.12f, 1.49f, dz + .12f), duct);
                    if (!Inside(new Vector2(xs[i + 1] + .005f, p.y)))
                        Box(name + "WallE", root, new Vector3(xs[i + 1] + .06f, floor + .625f, p.y), new Vector3(.12f, 1.49f, dz + .12f), duct);
                    if (!Inside(new Vector2(p.x, zs[j] - .005f)))
                        Box(name + "WallS", root, new Vector3(p.x, floor + .625f, zs[j] - .06f), new Vector3(dx + .12f, 1.49f, .12f), duct);
                    if (!Inside(new Vector2(p.x, zs[j + 1] + .005f)))
                        Box(name + "WallN", root, new Vector3(p.x, floor + .625f, zs[j + 1] + .06f), new Vector3(dx + .12f, 1.49f, .12f), duct);
                }
        }

        static void WallOpening(ServiceRoom room, Transform roomRoot)
        {
            var wall = GameObject.Find("Motel_Layout_V4/01_Architecture/Room" + room.RoomNumber + "_CourtyardWall").transform;
            var rect = WorldRect(roomRoot, Rect.MinMaxRect(1.7f, 6, 2.8f, 6.2f));
            var cut = new Bounds(new Vector3(rect.center.x, .525f, rect.center.y), new Vector3(rect.width, 1.05f, .24f));
            foreach (var c in wall.GetComponentsInChildren<Collider>().ToArray())
                if (c.name == "Wall")
                    CutBox(c.gameObject, cut);
            // The low vent shares the wall below the window. Raise only its sill, keeping its width/top.
            foreach (var c in wall.GetComponentsInChildren<Transform>().Where(t => t.name == "Glass" || t.name == "Frame").ToArray())
            {
                var p = roomRoot.InverseTransformPoint(c.position);
                var size = c.localScale;
                if (c.name == "Glass" || size.y > .1f)
                {
                    size.y = .95f;
                    p.y = 1.775f;
                }
                else if (p.y < 1)
                {
                    p.y = 1.325f;
                }

                c.position = roomRoot.TransformPoint(p);
                c.localScale = size;
            }

            var paint = wall.GetComponentsInChildren<Renderer>().First(r => r.name.StartsWith("Wall")).sharedMaterial;
            // Fill the former window sill region, except the new opening.
            var patch = Group("VentWindowSill", roomRoot);
            Box("Lintel", patch, new Vector3(2.45f, 1.175f, 6.1f), new Vector3(.7f, .25f, .2f), paint, 1);
            Box("Sill", patch, new Vector3(3.8f, 1.075f, 6.1f), new Vector3(2, .45f, .2f), paint, 1);
            // Non-colliding trim must not visually cross the vent aperture either.
            foreach (var t in wall.GetComponentsInChildren<Transform>().Where(t => t.name == "Baseboard").ToArray())
            {
                var p = roomRoot.InverseTransformPoint(t.position);
                if (p.x + t.localScale.x * .5f > 1.7f && p.x - t.localScale.x * .5f < 2.8f)
                    Delete(t.gameObject);
            }

            var curtain = roomRoot.GetComponentsInChildren<Transform>().Single(t => t.name == "ClosedWindowCurtain");
            curtain.localPosition = new Vector3(3.45f, 1.775f, 6.02f);
            curtain.localScale = new Vector3(2.74f, .95f, .04f);
        }

        static void Entrance(VentHatch h)
        {
            var room = h.room;
            var roomRoot = room.transform.parent.parent;
            string number = room.RoomNumber;
            var oldCenter = h.transform.position;
            var floorRoot = GameObject.Find("Motel_Layout_V4/01_Architecture/Room" + number).transform;
            floorRoot.Find("Floor").gameObject.SetActive(true);
            foreach (var t in floorRoot.GetComponentsInChildren<Transform>(true).Where(t => t.name == "FloorAroundHatch").ToArray())
                Delete(t.gameObject);
            foreach (Transform child in h.transform.Cast<Transform>().ToArray())
                Delete(child.gameObject);
            if (h.TryGetComponent<NavMeshObstacle>(out var oldObstacle))
                Delete(oldObstacle);
            Box("FormerHatchRoofPatch_" + number, shell, new Vector3(oldCenter.x, -1.74f, oldCenter.z), new Vector3(1.4f, .12f, 1.4f), duct);
            h.name = "WallVent_" + number;
            h.transform.position = roomRoot.position;
            h.transform.rotation = roomRoot.rotation;
            WallOpening(room, roomRoot);
            room.Trash.Single(t => t.name == "Trash_2").transform.position = roomRoot.TransformPoint(new Vector3(.85f, .09f, 4.35f));
            float r = number == "101" || number == "104" ? .65f : 3.85f;
            float midY = -.68f * (7.35f - 6.3f) / 2.25f;
            Flight(h.transform, "Entrance", 2.25f, new[]{6.1f, 6.3f, 7.35f, 8.55f, 10.35f}, new[]{0f, 0f, midY, -.68f, -1.5f}, new[]{1.15f, 1.15f, 1.15f, .57f, -.25f});
            Flight(h.transform, "Return", r, new[]{6.3f, 10.35f}, new[]{-3.1f, -1.5f}, new[]{-1.85f, -.25f});
            FlatUnion(h.transform, "Turn", new List<Rect>{Rect.MinMaxRect(Mathf.Min(r, 2.25f) - .55f, 10.35f, Mathf.Max(r, 2.25f) + .55f, 11.45f)}, -1.5f, new List<Rect>(), p => p.y < 10.35f && (Mathf.Abs(p.x - 2.25f) < .55f || Mathf.Abs(p.x - r) < .55f));
            // Add slope edges to the landing tessellation so only the web between flights gets a wall.
            var turnWall = h.GetComponentsInChildren<Transform>().Where(t => t.name == "TurnWallS").ToArray();
            foreach (var t in turnWall)
                Delete(t.gameObject);
            Box("TurnWeb", h.transform, new Vector3((r + 2.25f) * .5f, -.875f, 10.29f), new Vector3(.5f, 1.49f, .12f), duct);
            var flats = new List<Rect>{Rect.MinMaxRect(r - .55f, 4.05f, r + .55f, 6.3f), Rect.MinMaxRect(Mathf.Min(r, 1.6f) - .55f, 4.05f, Mathf.Max(r, 1.6f) + .55f, 5.15f)};
            var existing = shell.GetComponentsInChildren<Collider>().Where(c => c.name == "Floor").Select(c => LocalRect(h.transform, c.bounds)).ToList();
            foreach (var rect in flats)
            {
                var world = WorldRect(h.transform, rect);
                var cut = new Bounds(new Vector3(world.center.x, -2.45f, world.center.y), new Vector3(world.width, 1.6f, world.height));
                foreach (var c in shell.GetComponentsInChildren<Collider>().Where(c => c.name.StartsWith("Wall")).ToArray())
                    CutBox(c.gameObject, cut);
            }

            FlatUnion(h.transform, "Connection", flats, -3.1f, existing, p => p.y > 6.3f && Mathf.Abs(p.x - r) < .55f);
            h.upper = Group("RoomApproach", h.transform, new Vector3(2.1f, .025f, 5.2f));
            h.lower = Group("TunnelConnection", h.transform, new Vector3(1.6f, -3.075f, 4.6f));
            var points = new[]{h.upper.localPosition, new Vector3(2.25f, .025f, 6.3f), new Vector3(2.25f, midY + .025f, 7.35f), new Vector3(2.25f, -.655f, 8.55f), new Vector3(2.25f, -1.475f, 10.35f), new Vector3(2.25f, -1.475f, 10.9f), new Vector3(r, -1.475f, 10.9f), new Vector3(r, -1.475f, 10.35f), new Vector3(r, -3.075f, 6.3f), new Vector3(r, -3.075f, 4.6f), h.lower.localPosition};
            h.route = points.Select((p, i) => Group("Route_" + i, h.transform, p)).ToArray();
            var node = All<VentNode>(SceneManager.GetActiveScene()).Single(n => n.underground && n.room == room);
            node.landing = h.upper;
            node.mouth = Group("GrilleMouth", h.transform, new Vector3(2.25f, .5f, 6.1f));
            h.hinge = Group("GrilleHinge", h.transform, new Vector3(2.25f, 1.05f, 6.23f));
            h.leaf = Box("HingedGrille", h.hinge, new Vector3(0, -.52f, 0), new Vector3(1.08f, 1.03f, .045f), metal, 3).GetComponent<BoxCollider>();
            for (int i = 0; i < 9; i++)
            {
                var l = Box("Louvre", h.hinge, new Vector3(0, -.09f - i * .105f, -.04f), new Vector3(.98f, .065f, .04f), metal, 3);
                Delete(l.GetComponent<Collider>());
                l.transform.localRotation = Quaternion.Euler(-20, 0, 0);
            }

            foreach (float x in new[]{1.665f, 2.835f})
                Box("GrilleFrame", h.transform, new Vector3(x, .54f, 6.08f), new Vector3(.07f, 1.08f, .24f), metal, 3);
            Box("GrilleFrameTop", h.transform, new Vector3(2.25f, 1.09f, 6.08f), new Vector3(1.24f, .08f, .24f), metal, 3);
            float housingEnd = 8.55f + (1.37f - .68f) / (.82f / 1.8f);
            courtyardCuts.Add(WorldRect(h.transform, Rect.MinMaxRect(1.58f, 6.1f, 2.92f, housingEnd)));
            var blocker = Group("ServiceHousingNavObstacle", h.transform, new Vector3(2.25f, .5f, (6.2f + housingEnd) * .5f)).gameObject.AddComponent<NavMeshObstacle>();
            blocker.shape = NavMeshObstacleShape.Box;
            blocker.size = new Vector3(1.34f, 2, housingEnd - 6.2f);
            blocker.carving = true;
            blocker.carveOnlyStationary = true;
            EditorUtility.SetDirty(h);
            EditorUtility.SetDirty(node);
        }

        static void Courtyard()
        {
            var floor = GameObject.Find("Motel_Layout_V4/01_Architecture/Courtyard/Floor");
            var b = floor.GetComponent<Collider>().bounds;
            var mat = floor.GetComponent<Renderer>().sharedMaterial;
            var parent = floor.transform.parent;
            floor.SetActive(false);
            var xs = courtyardCuts.SelectMany(r => new[]{r.xMin, r.xMax}).Append(b.min.x).Append(b.max.x).Distinct().OrderBy(x => x).ToArray();
            var zs = courtyardCuts.SelectMany(r => new[]{r.yMin, r.yMax}).Append(b.min.z).Append(b.max.z).Distinct().OrderBy(z => z).ToArray();
            for (int i = 0; i < xs.Length - 1; i++)
                for (int j = 0; j < zs.Length - 1; j++)
                {
                    var p = new Vector2((xs[i] + xs[i + 1]) * .5f, (zs[j] + zs[j + 1]) * .5f);
                    if (courtyardCuts.Any(r => r.Contains(p)))
                        continue;
                    Box("FloorAroundServiceHousing", parent, new Vector3(p.x, -.04f, p.y), new Vector3(xs[i + 1] - xs[i], .08f, zs[j + 1] - zs[j]), mat, 1);
                }
        }

        [MenuItem("Motel/Underground/Install wall entrances")]
        public static void Install()
        {
            var s = SceneManager.GetActiveScene();
            if (s.path != ScenePath || s.isDirty)
                throw new Exception("Open and save V4 before migration.");
            var hatches = All<VentHatch>(s);
            if (hatches.Length != 6 || hatches.Any(h => h.name.StartsWith("WallVent")))
                throw new Exception("Expected six original hatches; migration is intentionally one-shot.");
            Directory.CreateDirectory(MeshFolder);
            AssetDatabase.Refresh();
            duct = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Underground/DuctSteel.mat");
            metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/PizzaDoggy/MegaPack/Materials/metal_mp_6.mat");
            shell = GameObject.Find("Motel_Layout_V4/05_Underground/TunnelShell").transform;
            courtyardCuts.Clear();
            foreach (var h in hatches)
                Entrance(h);
            Courtyard();
            Physics.SyncTransforms();
            foreach (var no in All<NetworkObject>(s))
                typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(no, null);
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
            AssetDatabase.SaveAssets();
            UndergroundSetup.Validate();
        }

        [MenuItem("Motel/Underground/Validate wall entrances")]
        public static void Validate() => UndergroundSetup.Validate();
        public static void ValidateRoutes(Scene s, List<string> lines)
        {
            if(All<VentHatch>(s).Any(h=>h.transform.Find("StraightRampFloor0")!=null)){GameplayRefinementSetup.ValidateRoutes(s,lines);return;}
            void Check(bool ok, string text) => lines.Add((ok ? "PASS " : "FAIL ") + text);
            var hatches = All<VentHatch>(s);
            Check(hatches.Length == 6, "Six wall grilles");
            foreach (var h in hatches)
            {
                if (h.hinge == null || h.leaf == null || h.route == null || h.route.Length < 2)
                {
                    Check(false, "Unconfigured grille " + h.name);
                    continue;
                }

                h.hinge.localRotation = Quaternion.Euler(-90, 0, 0);
                Physics.SyncTransforms();
                var hits = new HashSet<string>();
                float length = 0;
                for (int j = 0; j < h.route.Length - 1; j++)
                {
                    var a = h.route[j].position;
                    var b = h.route[j + 1].position;
                    length += Vector3.Distance(a, b);
                    int samples = Mathf.CeilToInt(Vector3.Distance(a, b) / .08f);
                    for (int i = 0; i <= samples; i++)
                    {
                        var p = Vector3.Lerp(a, b, (float)i / samples);
                        foreach (var c in Physics.OverlapCapsule(p + Vector3.up * .32f, p + Vector3.up * .6f, .285f, ~0, QueryTriggerInteraction.Ignore))
                            hits.Add(c.transform.parent?.name + "/" + c.name);
                        Check(Physics.Raycast(p + Vector3.up * .15f, Vector3.down, .3f, ~0, QueryTriggerInteraction.Ignore), "Ramp support " + h.room.RoomNumber + " " + j + "/" + i);
                    }
                }

                Check(hits.Count == 0, "Crouched wall/ramp clearance " + h.room.RoomNumber + " " + string.Join("; ", hits));
                Check(Physics.CheckCapsule(h.route[1].position + Vector3.up * .32f, h.route[1].position + Vector3.up * 1.48f, .28f, ~0, QueryTriggerInteraction.Ignore), "Standing cannot enter " + h.room.RoomNumber);
                Check(!h.GetComponentsInChildren<Transform>().Any(t => t.name.Contains("Ladder") || t.name.Contains("Handle") || t.name.Contains("Shaft")), "No ladder/button/shaft " + h.room.RoomNumber);
                var nav = new NavMeshPath();
                Check(NavMesh.CalculatePath(h.room.DoorOutside.position, h.room.GuestIdle.position, 1 | (1 << h.room.NavigationArea), nav) && nav.status == NavMeshPathStatus.PathComplete, "Guest route preserved " + h.room.RoomNumber);
                h.hinge.localRotation = Quaternion.identity;
                lines.Add("INFO Room " + h.room.RoomNumber + " approach-to-network route " + length.ToString("F2") + "m; width 1.10m; aperture 1.05m; slope 16.82/24.49/21.55deg");
            }

            Physics.SyncTransforms();
            Directory.CreateDirectory(Reports);
            File.WriteAllLines(Reports + "/geometry-validation.txt", lines);
        }
    }
}
