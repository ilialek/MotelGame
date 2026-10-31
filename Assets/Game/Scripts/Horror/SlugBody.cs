using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace MotelGame.Horror
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SlugBody : MonoBehaviour
    {
        public float bodyLength = 2.8f;
        public float maximumWidth = .66f, heightRatio = .72f;
        public float swayAmplitude = .065f, swayFrequency = .65f, wavelength = 1.9f;
        public AnimationCurve radiusProfile = new AnimationCurve(
            new Keyframe(0, .35f), new Keyframe(.12f, .68f), new Keyframe(.35f, 1),
            new Keyframe(.55f, .9f), new Keyframe(.76f, .55f), new Keyframe(.92f, .18f), new Keyframe(1, .015f));
        [Range(24, 64)] public int longitudinalRings = 36;
        [Range(6, 16)] public int radialSegments = 10;
        public Vector3[] Centers { get; private set; }
        public Vector3[] Vertices => vertices;
        public Mesh BodyMesh => mesh;
        public static readonly ProfilerMarker CenterlineMarker = new("MimicSlug.Centerline");
        public static readonly ProfilerMarker MeshMarker = new("MimicSlug.MeshDeform");
        Mesh mesh;
        Vector3[] sourceCenters, vertices, normals, frameRight, frameUp, tangents;
        int[] triangles;
        int builtRings, builtSides;
        float widthScale = 1;
        public void Prepare() { if (mesh == null) BuildBuffers(); }
        void BuildBuffers()
        {
            longitudinalRings = Mathf.Clamp(longitudinalRings, 24, 64);
            radialSegments = Mathf.Clamp(radialSegments, 6, 16);
            builtRings = longitudinalRings; builtSides = radialSegments;
            if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            mesh = new Mesh { name = "Mimic continuous dynamic body", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            int stride = builtSides + 1, total = builtRings * stride + 2;
            vertices = new Vector3[total]; normals = new Vector3[total];
            sourceCenters = new Vector3[builtRings]; Centers = new Vector3[builtRings]; frameRight = new Vector3[builtRings]; frameUp = new Vector3[builtRings]; tangents = new Vector3[builtRings];
            var uv = new Vector2[total];
            triangles = new int[((builtRings - 1) * builtSides * 2 + builtSides * 2) * 3];
            int index = 0;
            for (int i = 0; i < builtRings; i++)
            {
                for (int j = 0; j <= builtSides; j++)
                {
                    uv[i * stride + j] = new Vector2(j / (float)builtSides, i / (float)(builtRings - 1));
                    if (i == builtRings - 1 || j == builtSides) continue;
                    int a = i * stride + j, b = a + stride;
                    triangles[index++] = a; triangles[index++] = a + 1; triangles[index++] = b;
                    triangles[index++] = a + 1; triangles[index++] = b + 1; triangles[index++] = b;
                }
            }
            int head = total - 2, tail = total - 1, tailStart = (builtRings - 1) * stride;
            uv[head] = new Vector2(.5f, 0); uv[tail] = new Vector2(.5f, 1);
            for (int j = 0; j < builtSides; j++)
            {
                triangles[index++] = head; triangles[index++] = j + 1; triangles[index++] = j;
                triangles[index++] = tail; triangles[index++] = tailStart + j; triangles[index++] = tailStart + j + 1;
            }
            // The longitudinal axis runs from head to tail (opposite the forward tangent).
            for (int i = 0; i < triangles.Length; i += 3) { int swap = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = swap; }
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        // Geometry from SlugLab; movement now comes exclusively from the authoritative AI trail.
        public void Render(SlugPath path, float distance, float scale = 1)
        {
            Prepare(); widthScale = scale; path.PrepareBody();
            using (CenterlineMarker.Auto())
            {
                for (int i = 0; i < builtRings; i++)
                {
                    var sample = path.EvaluateBody(distance - bodyLength * i / (builtRings - 1));
                    float low = Mathf.Clamp01((-sample.Position.y - 1.8f) / .85f);
                    sourceCenters[i] = sample.Position - Vector3.up * (.19f * low);
                }
                for (int i = 0; i < builtRings; i++)
                {
                    float t = i / (float)(builtRings - 1), behind = bodyLength * t;
                    var tangent = (sourceCenters[Mathf.Max(0, i - 1)] - sourceCenters[Mathf.Min(builtRings - 1, i + 1)]).normalized;
                    var right = Mathf.Abs(tangent.y) < .9f ? Vector3.Cross(Vector3.up, tangent).normalized : path.Evaluate(distance - behind).Right;
                    float low = Mathf.Clamp01((-sourceCenters[i].y - 1.8f) / .85f);
                    float envelope = Mathf.Sin(Mathf.PI * t) * (.6f + .4f * t);
                    float wave = Mathf.Sin(2 * Mathf.PI * (behind / wavelength - distance * swayFrequency));
                    Centers[i] = sourceCenters[i] + right * (wave * swayAmplitude * Mathf.Lerp(.25f, 1, low) * envelope);
                }
                for (int i = 0; i < builtRings; i++)
                {
                    tangents[i] = (Centers[Mathf.Max(0, i - 1)] - Centers[Mathf.Min(builtRings - 1, i + 1)]).normalized;
                    frameRight[i] = i == 0 ? (Mathf.Abs(tangents[i].y) < .9f ? Vector3.Cross(Vector3.up, tangents[i]).normalized : Vector3.ProjectOnPlane(path.EvaluateBody(distance).Right, tangents[i]).normalized) :
                        SlugPath.Transport(frameRight[i - 1], tangents[i - 1], tangents[i]);
                    if (frameRight[i].sqrMagnitude < .5f) frameRight[i] = Vector3.Cross(tangents[i], Vector3.forward).normalized;
                    frameUp[i] = Vector3.Cross(tangents[i], frameRight[i]).normalized;
                }
            }
            using (MeshMarker.Auto()) UpdateMesh();
        }
        public void UpdateMesh()
        {
            int stride = builtSides + 1;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < builtRings; i++)
            {
                float t = i / (float)(builtRings - 1);
                float radius = widthScale * maximumWidth * .5f * Mathf.Clamp(radiusProfile.Evaluate(t), .008f, 1);
                // Compress the cross-section slightly at tight bends instead of folding the
                // inner surface through itself. Straight-body proportions remain unchanged.
                if (i > 0 && i + 1 < builtRings)
                {
                    var before = Centers[i] - Centers[Mathf.Max(0, i - 2)];
                    var after = Centers[Mathf.Min(builtRings - 1, i + 2)] - Centers[i];
                    float halfSin = Mathf.Sqrt(Mathf.Max(0, (1 - Vector3.Dot(before.normalized, after.normalized)) * .5f));
                    if (halfSin > .001f)
                        radius = Mathf.Min(radius, Mathf.Max(maximumWidth * .1f, (before.magnitude + after.magnitude) * .25f / halfSin * .82f));
                }
                for (int j = 0; j <= builtSides; j++)
                {
                    float a = 2 * Mathf.PI * (j % builtSides) / builtSides, sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                    float asymmetry = 1 + .035f * Mathf.Sin(t * 17 + cos * 3) * Mathf.Sin(t * Mathf.PI);
                    float y = sin * heightRatio * (sin < 0 ? .78f : 1);
                    Vector3 world = Centers[i] + radius * asymmetry * (frameRight[i] * cos + frameUp[i] * y);
                    Vector3 local = transform.InverseTransformPoint(world);
                    vertices[i * stride + j] = local; min = Vector3.Min(min, local); max = Vector3.Max(max, local);
                }
            }
            vertices[vertices.Length - 2] = transform.InverseTransformPoint(Centers[0] + tangents[0] * maximumWidth * .09f);
            vertices[vertices.Length - 1] = transform.InverseTransformPoint(Centers[builtRings - 1] - tangents[builtRings - 1] * .015f);
            min = Vector3.Min(min, vertices[vertices.Length - 2]); min = Vector3.Min(min, vertices[vertices.Length - 1]);
            max = Vector3.Max(max, vertices[vertices.Length - 2]); max = Vector3.Max(max, vertices[vertices.Length - 1]);
            System.Array.Clear(normals, 0, normals.Length);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += normal; normals[b] += normal; normals[c] += normal;
            }
            // Weld duplicate UV seam normals; keep UVs separate and geometry coincident.
            for (int i = 0; i < builtRings; i++)
            { int first = i * stride, last = first + builtSides; normals[first] = normals[last] = normals[first] + normals[last]; }
            for (int i = 0; i < normals.Length; i++) normals[i].Normalize();
            mesh.SetVertices(vertices); mesh.SetNormals(normals);
            mesh.bounds = new Bounds((min + max) * .5f, max - min + Vector3.one * .01f);
        }
        void OnDestroy() { if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }
    }
}
