using UnityEngine;
using MotelGame.Networking;
using MotelGame.Service;
namespace MotelGame.Registration
{
    // Drawn by the owning player's screen, never inside a computer's GUI group / display texture.
    public sealed class GuestDialogueSubtitles : MonoBehaviour
    {
        NetworkPlayer player;
        GUIStyle style;
        void Awake()=>player=GetComponent<NetworkPlayer>();
        void OnGUI()
        {
            var desk=RegistrationDesk.Instance;
            if(player==null||!player.IsSpawned||!player.IsOwner||desk==null)return;
            RegisteredGuest speaker=null;GuestStatement latest=default;
            double now=player.NetworkManager.ServerTime.Time;
            foreach(var guest in desk.Reception.Guests)
            {
                var record=guest.GetComponent<RegisteredGuest>();
                if(!record.IsSpawned||!record.LatestSpeech(out var line)||now<line.SpokenAt||now>=line.SpokenAt+RegisteredGuest.SpeechDuration(line))continue;
                if(Vector3.Distance(transform.position,guest.transform.position)>14)continue;
                if(speaker==null||line.SpokenAt>latest.SpokenAt){speaker=record;latest=line;}
            }
            if(speaker==null)return;
            if(style==null){style=new GUIStyle(GUI.skin.label){fontSize=17,wordWrap=true,alignment=TextAnchor.MiddleCenter,padding=new RectOffset(12,12,5,5)};style.normal.textColor=Color.white;}
            var matrix=GUI.matrix;var color=GUI.color;int depth=GUI.depth;
            try
            {
                GUI.depth=-100;GUI.color=Color.white;
                float scale=Mathf.Min(Screen.width/900f,Screen.height/600f);GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
                float w=Screen.width/scale,h=Screen.height/scale,width=Mathf.Min(w-32,1000);
                string text=now-latest.SpokenAt<1.4?"YOU: "+latest.Prompt:speaker.PublicName.ToUpperInvariant()+": "+latest.Answer;
                float height=style.CalcHeight(new GUIContent(text),width);
                var rect=new Rect((w-width)*.5f,h-height-10,width,height);
                GUI.color=new Color(0,0,0,.86f);GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=Color.white;GUI.Label(rect,text,style);
            }
            finally{GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;}
        }
    }
}
