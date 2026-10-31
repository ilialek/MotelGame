#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MotelGame.Service;
using MotelGame.Registration;

namespace MotelGame.Networking.Tests
{
    public sealed partial class MotelLayoutSmokeTest
    {
        IEnumerator VehicleLate()
        {
            yield return Wait(()=>reception.Guests.Any(g=>g.GetComponent<GuestVehicleJourney>().Moving),"late moving car");
            var g=reception.Guests.First(x=>x.GetComponent<GuestVehicleJourney>().Moving);var j=g.GetComponent<GuestVehicleJourney>();
            Need(!g.Visible&&!g.Body.enabled&&!g.Agent.enabled,"Late driver visible outside car");
            Need(g.GetComponent<RegisteredGuest>().State.ParkingSlot==j.slot.number,"Late parking mismatch");
            var old=j.vehicle.transform.position;yield return new WaitForSecondsRealtime(1);
            Need(Vector3.Distance(old,j.vehicle.transform.position)>.5f,"Late car not moving");
            checks.Add("Late join during driving restored moving car, identity and parking, with hidden driver and no client NavMesh simulation.");Mark("late-checked");
        }
        IEnumerator Vehicles()
        {
            // Walk the real owner capsule onto the entry lane before the controlled arrival.
            if(!Host)yield return Walk(new Vector3(29,0,-28.5f));
            yield return Barrier("lane-ready");
            if(Host)
            {
                Field(requests,"callDelay",100000f);Field(shift,"electricalFaultCount",Vector2Int.zero);Need(shift.DebugStartScenario(900,1,30),"vehicle fixture");
                Field(registration,"agenda",new Queue<GuestCase>(new[]{registration.Cases[0]}));foreach(var r in reception.Rooms)r.PrepareForHuntDebug();
            }
            yield return Wait(()=>reception.Guests.Any(g=>g.Phase==GuestPhase.DrivingToMotel),"real driver arrival");
            var guest=reception.Guests.Single(g=>g.Phase==GuestPhase.DrivingToMotel);var journey=guest.GetComponent<GuestVehicleJourney>();
            yield return Wait(()=>journey.State.Blocked,"car yields to remote owner's capsule",60);
            // A Relay snapshot can arrive before the visual interpolation reaches the stopped position.
            float stoppedDistance=journey.State.Distance;
            yield return Wait(()=>Mathf.Abs((float)Get(journey,"displayDistance")-journey.State.Distance)<.02f,"stopped car visual converges",3);
            var stopped=journey.vehicle.transform.position;
            yield return new WaitForSecondsRealtime(1);
            Need(journey.State.Blocked&&Mathf.Abs(journey.State.Distance-stoppedDistance)<.01f,"Blocked authoritative vehicle continued moving");
            Need(Vector3.Distance(stopped,journey.vehicle.transform.position)<.08f,"Stopped visual continued moving after convergence");
            Need(!guest.Visible&&!guest.Body.enabled,"Driver appeared before parking");yield return Barrier("car-paused");
            if(!Host)yield return Walk(new Vector3(29,0,-25));
            yield return Wait(()=>!journey.State.Blocked&&Vector3.Distance(stopped,journey.vehicle.transform.position)>1,"car resumes after player leaves lane");
            if(Host)Mark("late");yield return Wait(()=>Has("late-checked"),"late moving observer",120);yield return Barrier("vehicle-late");
            yield return OpenComputer(Station);yield return Wait(()=>registration.Waiting!=null,"driver exits and walks to counter",180);
            Need(journey.Parked&&guest.Visible,"Parked guest presentation");checks.Add("Authoritative vehicle stopped for remote player's actual controller, held position, resumed, parked, opened door and delivered guest to Reception.");
            yield return Barrier("vehicle-counter");
        }
    }
}
#endif
