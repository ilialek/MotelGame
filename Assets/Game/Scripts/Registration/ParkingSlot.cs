using UnityEngine;

namespace MotelGame.Registration
{
    // Authored world-space routes. The car body, plate and CCTV all use this same slot.
    public sealed class ParkingSlot : MonoBehaviour
    {
        public int number;
        public Transform driverPoint;
        public Vector3[] arrival, reverse, departure;
        public string Code=>"P"+number.ToString("00");
        public static float Length(Vector3[] route)
        {float length=0;for(int i=1;i<route.Length;i++)length+=Vector3.Distance(route[i-1],route[i]);return length;}
        public static void Sample(Vector3[] route,float distance,bool reversing,out Vector3 position,out Quaternion rotation)
        {
            int i=1;
            for(;i<route.Length-1;i++){float length=Vector3.Distance(route[i-1],route[i]);if(distance<=length)break;distance-=length;}
            var direction=route[i]-route[i-1];position=Vector3.Lerp(route[i-1],route[i],Mathf.Clamp01(distance/direction.magnitude));
            rotation=Quaternion.LookRotation(reversing?-direction:direction);
        }
    }
}
