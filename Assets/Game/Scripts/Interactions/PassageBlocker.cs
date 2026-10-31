using UnityEngine;
using Unity.Netcode;
using MotelGame.Service;

namespace MotelGame.Interactions
{
    // A fixed doorway collider, independent of the animated interaction trigger.
    // Every peer checks its owner-predicted player before enabling collision.
    internal sealed class PassageBlocker
    {
        readonly BoxCollider collider;
        readonly NetworkManager manager;
        public bool Enabled => collider.enabled;
        public PassageBlocker(Transform root, BoxCollider leaf, NetworkManager network)
        {
            manager=network;
            var g=new GameObject("ClosedPassageBlocker");g.transform.SetParent(root,false);
            g.transform.SetPositionAndRotation(leaf.transform.TransformPoint(leaf.center),leaf.transform.rotation);
            collider=g.AddComponent<BoxCollider>();
            collider.size=Vector3.Scale(leaf.size,leaf.transform.lossyScale);
            collider.enabled=false;
            leaf.isTrigger=true;
        }
        public void SetClosed(bool closed)
        {
            if(!closed){collider.enabled=false;return;}
            if(collider.enabled)return;
            if(Occupied())return;
            collider.enabled=true;
        }
        public bool CanBlock=>collider.enabled||!Occupied();
        bool Occupied()
        {
            // Replicated remote controllers are disabled. Include their logical capsules too.
            foreach(var obj in manager.SpawnManager.SpawnedObjectsList)
            {
                if(!obj.IsPlayerObject||!obj.TryGetComponent<CharacterController>(out var c))continue;
                var local=collider.transform.InverseTransformPoint(obj.transform.TransformPoint(c.center));
                var half=collider.size*.5f;
                if(Mathf.Abs(local.x)<half.x+c.radius+.04f && Mathf.Abs(local.z)<half.z+c.radius+.04f && Mathf.Abs(local.y)<half.y+c.height*.5f+.04f)return true;
            }
            foreach(var c in Physics.OverlapBox(collider.transform.position,collider.size*.5f+Vector3.one*.04f,collider.transform.rotation,~0,QueryTriggerInteraction.Ignore))
                if(c.GetComponentInParent<GuestLifecycle>()!=null)return true;
            return false;
        }
        public void Dispose(){if(collider!=null)Object.Destroy(collider.gameObject);}
    }
}
