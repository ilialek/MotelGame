using UnityEngine;

namespace MotelGame.Characters
{
    // Presentation only: the existing controller / NGO transform owns world movement.
    // Both owners and observers measure that movement locally; no animation RPCs.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class CharacterVisuals : MonoBehaviour
    {
        [SerializeField] Transform motionRoot;
        [SerializeField] GameObject[] appearances;
        [SerializeField, Min(0)] int appearanceIndex;
        [SerializeField, Min(.1f)] float walkSpeed = 4;
        [SerializeField] bool canRun = true;
        [SerializeField, Min(.1f)] float runSpeed = 6;
        // Measured approximate stride speeds of the selected clips at a 1.8 m scale.
        [SerializeField, Min(.1f)] float clipWalkSpeed = 2.1f;
        [SerializeField, Min(.1f)] float clipRunSpeed = 4.5f;
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int WalkRateId = Animator.StringToHash("WalkRate");
        static readonly int RunRateId = Animator.StringToHash("RunRate");
        Animator animator;
        Vector3 previousPosition;
        bool initialized, visible = true;
        float smoothedSpeed;
        public Animator Animator => animator;
        public float MovementSpeed => smoothedSpeed;
        public GameObject Appearance => appearances[Mathf.Clamp(appearanceIndex,0,appearances.Length-1)];
        public bool BodyVisible
        {
            get
            {
                foreach(var r in Appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(r.enabled && r.gameObject.activeInHierarchy) return true;
                return false;
            }
        }
        void Awake() => SelectAppearance();
        void OnEnable()
        {
            if(animator==null)SelectAppearance();
            previousPosition=motionRoot.position;initialized=false;smoothedSpeed=0;
            animator.SetFloat(SpeedId,0);
        }
        public void SelectAppearance()
        {
            appearanceIndex=Mathf.Clamp(appearanceIndex,0,appearances.Length-1);
            for(int i=0;i<appearances.Length;i++)appearances[i].SetActive(i==appearanceIndex);
            animator=Appearance.GetComponent<Animator>();
            animator.applyRootMotion=false;
            SetVisible(visible);
        }
        public void ApplyGuestAppearance(int index, Color color)
        {
            if(appearanceIndex!=index||animator==null){appearanceIndex=index;SelectAppearance();}
            foreach(var renderer in Appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                for(int i=0;i<renderer.sharedMaterials.Length;i++)
                    if(renderer.sharedMaterials[i]!=null&&renderer.sharedMaterials[i].name.EndsWith("_Shirt"))
                    {var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);renderer.SetPropertyBlock(block,i);}
        }
        public void SetVisible(bool value)
        {
            visible=value;
            // The towel is separate geometry, managed by TowelCarrier ownership.
            foreach(var a in appearances)
                foreach(var r in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))r.enabled=value;
        }
        void Update()
        {
            Vector3 delta=motionRoot.position-previousPosition;delta.y=0;previousPosition=motionRoot.position;
            float dt=Time.deltaTime;
            // Ignore initial placement, late-join corrections and new guest arrival teleports.
            float speed=initialized && dt>0 && delta.magnitude<=Mathf.Max(1,runSpeed*dt*2)
                ? delta.magnitude/dt : 0;
            initialized=true;
            speed=Mathf.Min(speed,canRun?runSpeed:walkSpeed);
            smoothedSpeed=Mathf.Lerp(smoothedSpeed,speed,1-Mathf.Exp(-dt/.08f));
            if(smoothedSpeed<.035f)smoothedSpeed=0;
            animator.SetFloat(SpeedId,smoothedSpeed);
            animator.SetFloat(WalkRateId,Mathf.Clamp(smoothedSpeed/clipWalkSpeed,.25f,3.5f));
            animator.SetFloat(RunRateId,Mathf.Clamp(smoothedSpeed/clipRunSpeed,.25f,2.5f));
        }
        public bool ConfigurationValid() => motionRoot!=null && appearances!=null && appearances.Length>0 &&
            System.Array.TrueForAll(appearances,a=>a!=null && a.GetComponent<Animator>()!=null &&
                a.GetComponent<Animator>().avatar!=null && a.GetComponent<Animator>().avatar.isValid &&
                a.GetComponent<Animator>().runtimeAnimatorController!=null &&
                a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length>0);
    }
}
