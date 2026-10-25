using UnityEngine;

namespace MotelGame.Service
{
    // Legacy TextMesh's default font shader ignores scene depth. Use an explicit URP
    // depth-tested material, keeping the dynamic font atlas current after glyph rebuilds.
    [ExecuteAlways, RequireComponent(typeof(TextMesh))]
    public sealed class WorldServiceText : MonoBehaviour
    {
        [SerializeField] Shader textShader;
        Material material;
        TextMesh label;
        public void RefreshMaterial()
        {
            label = GetComponent<TextMesh>();
            if (textShader == null || label.font == null) return;
            if (material == null)
            {
                material = new Material(label.font.material) { shader = textShader, hideFlags = HideFlags.HideAndDontSave };
                GetComponent<Renderer>().sharedMaterial = material;
            }
            material.mainTexture = label.font.material.mainTexture;
        }
        void LateUpdate() => RefreshMaterial();
        void OnDisable()
        {
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
                material = null;
            }
        }
    }
}
