using UnityEngine;

namespace MotelGame.Registration
{
    // Public vehicle appearance is replicated by its guest; GuestVehicleJourney owns travel.
    public sealed class GuestVehicleView : MonoBehaviour
    {
        public GameObject[] bodies;
        public Renderer[] paint;
        public TextMesh[] plates;
        MaterialPropertyBlock colors;
        uint lastVisit; Vector3 lastPaint; string lastPlate; byte lastKind;
        bool applied;
        public void Show(bool value){if(gameObject.activeSelf!=value)gameObject.SetActive(value);}
        public void Apply(GuestRegistrationState state,bool visible)
        {
            Show(visible);if(!visible)return;
            if(applied&&lastVisit==state.Visit&&lastPaint==state.Paint&&lastPlate==state.ActualPlate.ToString()&&lastKind==state.Vehicle)return;
            for(int i=0;i<bodies.Length;i++)bodies[i].SetActive(i==state.Vehicle);
            colors??=new MaterialPropertyBlock();colors.SetColor("_BaseColor",new Color(state.Paint.x,state.Paint.y,state.Paint.z));colors.SetColor("_Color",new Color(state.Paint.x,state.Paint.y,state.Paint.z));
            colors.SetColor("_EmissionColor",new Color(state.Paint.x,state.Paint.y,state.Paint.z)*.08f);
            foreach(var r in paint)r.SetPropertyBlock(colors);
            foreach(var plate in plates)plate.text=state.ActualPlate.ToString();
            lastVisit=state.Visit;lastPaint=state.Paint;lastPlate=state.ActualPlate.ToString();lastKind=state.Vehicle;applied=true;
        }
    }
}
