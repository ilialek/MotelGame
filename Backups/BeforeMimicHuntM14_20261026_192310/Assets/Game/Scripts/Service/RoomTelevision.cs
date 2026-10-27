using UnityEngine;
namespace MotelGame.Service
{
    // Presentation derives from the same global request snapshot, including late join/reset.
    public sealed class RoomTelevision : MonoBehaviour
    {
        [SerializeField] ServiceRoom room;
        [SerializeField] GuestServiceRequests requests;
        [SerializeField] TelevisionControl[] controls;
        [SerializeField] TextMesh screenText;
        [SerializeField] Renderer screen;
        MaterialPropertyBlock colors;
        public ServiceRoom Room=>room;
        public GuestServiceRequests Requests=>requests;
        public TelevisionControl[] Controls=>controls;
        public bool Broken=>requests.IsSpawned&&requests.Room==room&&requests.State.Kind==GuestRequestKind.RepairTelevision&&
            (requests.Phase==GuestRequestPhase.Ringing||requests.Phase==GuestRequestPhase.PendingDelivery);
        public string DisplayText=>screenText.text;
        void Update()
        {
            bool fault=Broken;bool power=fault&&requests.State.RepairStep>0;
            screenText.text=fault?(power?"NO SIGNAL\nRESET TUNING":"NO POWER\nCHECK CABLE"):"MOTEL TV\nCHANNEL 03";
            colors??=new MaterialPropertyBlock();
            Color c=fault?Color.Lerp(new Color(.03f,.04f,.035f),new Color(.2f,.22f,.2f),Mathf.PingPong(Time.unscaledTime*7,1)):new Color(.1f,.23f,.19f);
            colors.SetColor("_BaseColor",c);screen.SetPropertyBlock(colors);
        }
        public bool ConfigurationValid()=>room!=null&&requests!=null&&controls!=null&&controls.Length==2&&
            controls[0]!=null&&controls[1]!=null&&controls[0].Television==this&&controls[1].Television==this&&
            controls[0].Step==0&&controls[1].Step==1&&controls[0].TargetCollider!=null&&controls[1].TargetCollider!=null&&screenText!=null&&screen!=null;
    }
}
