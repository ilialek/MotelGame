using UnityEngine;
using MotelGame.Service;

namespace MotelGame.Horror
{
    public sealed class VentSurveillance : MonoBehaviour
    {
        public Camera[] cameras;
        public ReceptionComputer computer;
        RenderTexture[] feeds;
        int selected;
        float lastViewed;
        public int Selected => selected;
        public RenderTexture CurrentFeed => feeds == null ? null : feeds[selected];
        void Awake()
        {
            feeds = new RenderTexture[cameras.Length];
            for (int i = 0; i < cameras.Length; i++)
            {
                feeds[i] = new RenderTexture(384, 256, 16)
                {name = "Vent CCTV " + i, filterMode = FilterMode.Point};
                cameras[i].targetTexture = feeds[i];
                cameras[i].enabled = false;
            }
        }

        void Update()
        {
            for (int i = 0; i < cameras.Length; i++)
                cameras[i].enabled = computer.Focused && Time.unscaledTime - lastViewed < .3f && i == selected;
        }

        public void Select(int index)
        {
            selected = Mathf.Clamp(index, 0, cameras.Length - 1);
            lastViewed = Time.unscaledTime;
        }

        public void DrawFeeds(float width, float height)
        {
            lastViewed = Time.unscaledTime;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < cameras.Length; i++)
                if (GUILayout.Button("VENT CAM " + (i + 1)))
                    Select(i);
            GUILayout.EndHorizontal();
            var rect = GUILayoutUtility.GetRect(width, Mathf.Max(140, height - 45));
            GUI.DrawTexture(rect, feeds[selected], ScaleMode.ScaleToFit, false);
            GUI.color = new Color(0, 0, 0, .18f);
            for (float y = rect.y; y < rect.yMax; y += 4)
                GUI.DrawTexture(new Rect(rect.x, y, rect.width, 1), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8, rect.y + 8, 280, 22), "CAM " + (selected + 1) + "  ·  " + System.DateTime.Now.ToString("HH:mm:ss"));
            GUILayout.Label("Fixed junction view. Other branches are outside the camera's field of view.");
        }

        void OnDestroy()
        {
            if (feeds == null)
                return;
            foreach (var rt in feeds)
            {
                rt.Release();
                Destroy(rt);
            }
        }
    }
}
