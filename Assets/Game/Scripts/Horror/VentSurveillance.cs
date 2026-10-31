using UnityEngine;
using MotelGame.Service;

namespace MotelGame.Horror
{
    public sealed class VentSurveillance : MonoBehaviour
    {
        public Camera[] cameras;
        public string[] cameraLabels;
        public ReceptionComputer computer;
        public ReceptionComputer monitoringStation;
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
                if(i<3){cameras[i].enabled=false;continue;}
                feeds[i] = new RenderTexture(384, 256, 16)
                {name = "Vent CCTV " + i, filterMode = FilterMode.Point};
                cameras[i].targetTexture = feeds[i];
                cameras[i].enabled = false;
            }
        }

        void Update()
        {
            for (int i = 0; i < cameras.Length; i++)
                cameras[i].enabled = i>=3 && (computer.Focused || (monitoringStation!=null && monitoringStation.Focused)) && Time.unscaledTime - lastViewed < .3f && i == selected;
        }

        public void Select(int index)
        {
            selected = Mathf.Clamp(index, 0, cameras.Length - 1);
            lastViewed = Time.unscaledTime;
        }

        public void DrawParking(float width,float height)=>DrawRange(width,height,3,cameras.Length-3,true);
        public void DrawVents(float width,float height)
        {
            lastViewed=0;
            var label=new GUIStyle(GUI.skin.label){wordWrap=true,fontSize=17};label.normal.textColor=new Color(.8f,.86f,.77f);
            GUILayout.Label("UNDERGROUND / MOTION DETECTION",label);
            GUILayout.Space(18);GUILayout.Label(MimicHunt.Instance.ActivityText,label);
            GUILayout.Space(18);GUILayout.Label("A — south wing   |   B — connecting tunnels   |   C — north wing",label);
            GUILayout.Label("Movement signals are not identification. Staff movement and environmental vibration can also trigger a sector. No underground cameras.",label);
            GUILayout.FlexibleSpace();
        }
        public void DrawFeeds(float width,float height)=>DrawVents(width,height);
        void DrawRange(float width,float height,int first,int count,bool parking)
        {
            lastViewed=Time.unscaledTime;
            if(selected<first||selected>=first+count)Select(first);
            string[] labels=new string[count];for(int i=0;i<count;i++)labels[i]=parking?"P"+(i+1).ToString("00"):"Vent "+(i+1);
            Select(first+GUILayout.SelectionGrid(selected-first,labels,Mathf.Min(7,count),ComputerUiStyles.Button,GUILayout.Height(28)));
            var rect=GUILayoutUtility.GetRect(width,Mathf.Max(150,height-58));GUI.DrawTexture(rect,feeds[selected],ScaleMode.ScaleToFit,false);
            GUI.color=new Color(0,0,0,.12f);for(float y=rect.y;y<rect.yMax;y+=4)GUI.DrawTexture(new Rect(rect.x,y,rect.width,1),Texture2D.whiteTexture);GUI.color=Color.white;
            GUI.Label(new Rect(rect.x+8,rect.y+8,320,24),(parking?"LIVE / PARKING P"+(selected-2).ToString("00"):"LIVE / VENT "+(selected+1))+"  "+System.DateTime.Now.ToString("HH:mm:ss"));
            GUILayout.Label(parking?"Read the vehicle plate. Enter it manually in RoadWatch.":"Fixed ventilation cameras.");
        }

        void OnDestroy()
        {
            if (feeds == null)
                return;
            foreach (var rt in feeds)
            {
                if(rt==null)continue;
                rt.Release();
                Destroy(rt);
            }
        }
    }
}
