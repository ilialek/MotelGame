using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using MotelGame.Service;

namespace MotelGame.Editor
{
    public static partial class MotelLayoutV4
    {
        static void Render()
        {
            var s=EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                foreach(var t in All<Transform>(s))t.gameObject.layer=30;
                foreach(var l in All<Light>(s))l.cullingMask=1<<30;
                foreach(var t in All<WorldServiceText>(s))t.RefreshMaterial();
                foreach(var c in All<Camera>(s))c.enabled=false;
                var cam=All<Camera>(s).First();cam.scene=s;cam.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(s);cam.cullingMask=1<<30;cam.fieldOfView=70;cam.nearClipPlane=.03f;
                var views=new[]{("lobby",new Vector3(23.4f,1.65f,-12.5f),new Vector3(26.4f,1.2f,-8)),("courtyard",new Vector3(16.8f,1.65f,7.5f),new Vector3(6,1.4f,16)),("room101",new Vector3(14.25f,1.65f,.6f),new Vector3(15,1.1f,4.5f)),("backyard",new Vector3(22,1.65f,29),new Vector3(-1,1.5f,31)),("top",new Vector3(12,65,3),new Vector3(12,0,3))};
                foreach(var v in views)
                {
                    if(v.Item1=="top")
                    {
                        foreach(var t in All<Transform>(s).Where(t=>t.name.StartsWith("Roof")||t.name.StartsWith("Ceiling")||t.name=="Ventilation"))t.gameObject.SetActive(false);
                        var light=new GameObject("PlanLight").AddComponent<Light>();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject,s);light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(90,0,0);light.cullingMask=1<<30;
                        cam.orthographic=true;cam.orthographicSize=34;
                    }
                    cam.transform.position=v.Item2;cam.transform.LookAt(v.Item3);var rt=new RenderTexture(1440,1000,24);rt.Create();var old=RenderTexture.active;cam.targetTexture=rt;
                    try{cam.Render();RenderTexture.active=rt;var img=new Texture2D(1440,1000,TextureFormat.RGB24,false);img.ReadPixels(new Rect(0,0,1440,1000),0,0);img.Apply();File.WriteAllBytes(Reports+"/preview-"+v.Item1+".png",img.EncodeToPNG());Delete(img);}
                    finally{cam.targetTexture=null;RenderTexture.active=old;rt.Release();Delete(rt);}
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(s);}
        }
    }
}
