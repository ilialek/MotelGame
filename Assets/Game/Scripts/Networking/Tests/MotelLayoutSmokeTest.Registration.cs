#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Registration;

namespace MotelGame.Networking.Tests
{
    public sealed partial class MotelLayoutSmokeTest
    {
        RegistrationDesk registration;
        ReceptionComputer Station=>Host?computer:cctv;
        string SharedRegistration()=>registration.Waiting==null?"none":registration.Waiting.State.Visit+"|"+registration.Waiting.State.Name+"|"+registration.Waiting.State.Asked+"|"+registration.Lookup.Plate+"|"+registration.Lookup.Report+"|"+string.Join("/",Enumerable.Range(0,registration.Waiting.Statements.Count).Select(i=>registration.Waiting.Statements[i].Answer.ToString()));
        IEnumerator RegistrationLate()
        {
            if(mode=="reveal")
            {
                yield return Wait(()=>hunt.State.Active&&reception.Guests.Any(g=>g.Phase==GuestPhase.Revealed),"late transformed guest");
                var guest=reception.Guests.Single(g=>g.Phase==GuestPhase.Revealed);Need(!guest.Visible&&!guest.Body.enabled&&!guest.Agent.enabled,"Late duplicate guest body");
                checks.Add("Late join: revealed guest hidden, navigation/body disabled, active Mimic synchronized.");
            }
            else
            {
                yield return Wait(()=>File.Exists(Path.Combine(dir,"registration-state.txt"))&&SharedRegistration()==File.ReadAllText(Path.Combine(dir,"registration-state.txt")),"late registration statements and shared search");
                Need(reception.Guests.All(g=>!g.Agent.enabled),"Late client simulates NPC");checks.Add("Late join restored asked answers, identity, lookup and server-only NPC simulation.");
            }
            Mark("late-checked");
        }
        IEnumerator AskRegistration(RegisteredGuest guest,RegistrationQuestion q)
        {
            yield return Wait(()=>!guest.Speaking,"Previous dialogue complete");
            var reference=new NetworkObjectReference(guest.NetworkObject);var station=new NetworkObjectReference(Station.NetworkObject);
            int report=q==RegistrationQuestion.FollowUp?registration.Lookup.Report:-1;uint revision=registration.Lookup.Revision;
            typeof(RegistrationPanel).GetMethod("AskOrRead",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(Station.GetComponent<RegistrationPanel>(),new object[]{guest,q,report});
            registration.AskRpc(reference,guest.State.Visit,q,station,Station.Lease,report,revision);
            yield return Wait(()=>guest.Asked(q),"answer "+q);Need(Enumerable.Range(0,guest.Statements.Count).Count(i=>guest.Statements[i].Question==q)==1,"Duplicate answer committed");
        }
        IEnumerator CapturePanel(string name,int tab)
        {
            Field(Station,"registrationTab",tab);yield return new WaitForSecondsRealtime(.4f);
            if(!Application.isBatchMode){ScreenCapture.CaptureScreenshot(Path.Combine(dir,role+"-"+name+".png"));yield return new WaitForSecondsRealtime(.3f);}
        }
        IEnumerator RegistrationCases()
        {
            if(Host)
            {
                Field(requests,"callDelay",100000f);Field(shift,"electricalFaultCount",Vector2Int.zero);Need(shift.DebugStartScenario(1800,6,30),"registration fixture");
                Field(registration,"agenda",new Queue<GuestCase>(registration.Cases));foreach(var r in reception.Rooms)r.PrepareForHuntDebug();
            }
            yield return Wait(()=>shift.IsRunning,"registration shift");yield return OpenComputer(Station);yield return Barrier("registration-stations");
            for(int i=0;i<6;i++)
            {
                yield return Wait(()=>registration.Waiting!=null,"case arrival "+i,180);var guest=registration.Waiting;var npc=guest.Guest;var data=registration.Cases[i];var reference=new NetworkObjectReference(guest.NetworkObject);uint visit=guest.State.Visit;
                var journey=npc.GetComponent<GuestVehicleJourney>();
                Need(journey.Parked&&Vector3.Distance(guest.vehicle.transform.position,journey.slot.transform.position)<.05f,"Arrival did not end in assigned bay");
                Need(guest.PublicName==data.guestName&&guest.State.FirstName.ToString()==data.firstName&&guest.State.LastName.ToString()==data.lastName&&guest.State.DateOfBirth.ToString()==data.dateOfBirth,"Automatic license mismatch");
                Need(guest.State.BookingFirstName.ToString()==data.bookingFirstName&&guest.State.ExpectedGuests==data.expectedGuests&&guest.Statements.Count==0&&!guest.State.ParkingKnown&&guest.Complete,"Automatic booking / optional investigation");
                Need(guest.State.ClaimedPlate.IsEmpty&&guest.Presentation.portrait!=null,"License presentation / unasked plate leak");
                if(!Host)
                {
                    uint revision=guest.State.Revision;
                    registration.AskRpc(reference,visit,RegistrationQuestion.ParkingSpace,new NetworkObjectReference(computer.NetworkObject),computer.Lease);
                    yield return new WaitForSecondsRealtime(.35f);Need(guest.State.Revision==revision,"Non-owner workstation accepted question");
                    reception.RequestCheckInRpc(0,reception.Rooms[0].State.Cycle,reference,visit,computer.Lease);
                    yield return new WaitForSecondsRealtime(.2f);Need(npc.CanCheckIn,"Other station accepted admission");
                }
                yield return Barrier("unauthorized-"+i);
                yield return CapturePanel("case-"+i+"-automatic-license",1);
                if(Host)foreach(var q in new[]{RegistrationQuestion.BookingName,RegistrationQuestion.GuestCount,RegistrationQuestion.BookingNote})
                    if(data.Supports(q)){yield return CapturePanel("case-"+i+"-field-"+q,1);yield return AskRegistration(guest,q);}
                yield return Barrier("booking-"+i);yield return CapturePanel("case-"+i+"-booking-answer",1);
                if(i!=4)
                {
                    if(!Host)yield return AskRegistration(guest,RegistrationQuestion.ParkingSpace);
                    yield return Wait(()=>guest.State.ParkingKnown,"shared parking space");Need(guest.ParkingCode==journey.slot.Code,"False parking space");
                    Need(reception.Guests.Where(g=>g.GetComponent<RegisteredGuest>().State.ParkingSlot!=0).GroupBy(g=>g.GetComponent<RegisteredGuest>().State.ParkingSlot).All(g=>g.Count()==1),"Duplicate parking reservation");
                }
                Need(guest.vehicle.gameObject.activeInHierarchy&&guest.vehicle.plates.All(p=>p.text==data.actualPlate),"Parked plate not displayed");
                var feed=FindFirstObjectByType<VentSurveillance>();Field(Station,"registrationTab",3);feed.Select(2+journey.slot.number);yield return new WaitForSecondsRealtime(.5f);CaptureFeed(feed,role+"-parking-"+i);
                yield return CapturePanel("case-"+i+"-parking",3);yield return Barrier("answered-"+i);
                if(!Host)
                {
                    uint revision=registration.Lookup.Revision;
                    Field(Station.GetComponent<RegistrationPanel>(),"plateInput",data.actualPlate);
                    typeof(RegistrationPanel).GetMethod("Search",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(Station.GetComponent<RegistrationPanel>(),null);
                    registration.SearchRpc(data.actualPlate,revision,new NetworkObjectReference(cctv.NetworkObject),cctv.Lease);
                }
                yield return Wait(()=>registration.Lookup.Completed&&registration.Lookup.Plate.ToString()==RegistrationDesk.NormalizePlate(data.actualPlate),"shared RoadWatch result");
                Need(registration.Lookup.Report==(data.hasReport?i:-1),"Wrong search report");
                Field(Station.GetComponent<RegistrationPanel>(),"plateInput",data.actualPlate);yield return CapturePanel("case-"+i+"-roadwatch",2);
                if(data.hasReport)
                {
                    Need(data.report.photograph!=null&&data.report.photograph.width==480,"Missing report pixels");
                    Field(Station.GetComponent<RegistrationPanel>(),"enlarged",true);yield return CapturePanel("case-"+i+"-photo",2);Station.GetComponent<RegistrationPanel>().ClosePhotograph();
                    if(Host)
                    {
                        int before=guest.Statements.Count;
                        registration.AskRpc(reference,visit,RegistrationQuestion.FollowUp,new NetworkObjectReference(computer.NetworkObject),computer.Lease,i,registration.Lookup.Revision-1);
                        yield return new WaitForSecondsRealtime(.2f);Need(guest.Statements.Count==before,"Stale search accepted report question");
                        yield return AskRegistration(guest,RegistrationQuestion.FollowUp);
                    }
                    yield return Wait(()=>guest.AskedReport(i),"shared contextual report answer");yield return CapturePanel("case-"+i+"-report-answer",2);
                }
                if(i==2)
                {
                    if(Host){File.WriteAllText(Path.Combine(dir,"registration-state.txt"),SharedRegistration());Mark("late");}
                    yield return Wait(()=>Has("late-checked"),"registration late join",120);yield return Barrier("registration-late");
                }
                yield return Barrier("decision-"+i);
                if(i==4)Need(guest.Statements.Count==0,"Early refusal unexpectedly required questions");
                if(Host){reception.RequestRefuseRpc(reference,visit,computer.Lease);reception.RequestCheckInRpc(0,reception.Rooms[0].State.Cycle,reference,visit,computer.Lease);reception.RequestRefuseRpc(reference,visit,computer.Lease);}
                yield return Wait(()=>guest.State.Decision==RegistrationDecision.Refused&&npc.Phase==GuestPhase.Leaving,"atomic refusal against conflicting admit");
                Need(shift.State.CheckedIn==0&&!hunt.State.Active,"Refused guest produced admission/threat");
                yield return Wait(()=>journey.State.Phase==VehiclePhase.Boarding,"driver reaches car",120);
                yield return Wait(()=>journey.State.Phase==VehiclePhase.Reversing,"car reverses out",120);Need(!npc.Visible&&!npc.Body.enabled,"Driver body remains outside driving car");
                yield return Wait(()=>journey.State.Phase==VehiclePhase.Leaving,"car returns to road",120);
                yield return Wait(()=>npc.Phase==GuestPhase.Absent,"refused guest departed "+i,120);yield return new WaitForSecondsRealtime(.2f);Need(!guest.vehicle.gameObject.activeSelf,"Departed car remains");Need(guest.State.ParkingSlot==0&&!guest.State.ParkingKnown,"Departed parking reservation was not released");
                checks.Add(data.guestName+": real car arrival/parking, automatic documents, authored booking answers, "+(data.hasReport?"report + photo + question":"no report")+", shared state, one refusal, walk to car / reverse / road departure.");yield return Barrier("case-done-"+i);
            }
            if(Host)shift.EndForHuntDebug();yield return Wait(()=>shift.State.Phase==ShiftPhase.Finished,"registration debrief");
            Need(registration.Debrief.Contains("Human stays refused: 4")&&registration.Debrief.Contains("Disguised Mimics refused: 2"),"Decision totals duplicated");
            checks.Add("Six cases / three reports; four lost human stays and two safely refused disguises. Walk-in refused without questions. No duplicate decision or stale report question.");
            if(!Host){yield return Wait(()=>!cctv.LocalOpen,"closed at end of shift");yield return OpenComputer(cctv);}
            yield return Barrier("occupied-before-disconnect");Need(cctv.Occupied,"Disconnect fixture requires occupied station");
        }
        IEnumerator RegisterNew(int start,int count,string tag)
        {
            yield return OpenComputer(Station);yield return Barrier(tag+"-stations");
            for(int i=start;i<start+count;i++)
            {
                yield return Wait(()=>registration.Waiting!=null,"waiting guest "+tag+"/"+i,180);var g=registration.Waiting;uint visit=g.State.Visit;
                Need(g.Statements.Count==0,"Early admission fixture unexpectedly questioned guest");
                yield return Wait(()=>registration.CanAdmit(g.Guest),"complete registration");yield return Barrier(tag+"-asked-"+i);
                if(Host)
                {reception.RequestCheckInRpc(i,reception.Rooms[i].State.Cycle,new NetworkObjectReference(g.NetworkObject),visit,computer.Lease);reception.RequestCheckInRpc(i,reception.Rooms[i].State.Cycle,new NetworkObjectReference(g.NetworkObject),visit,computer.Lease);reception.RequestRefuseRpc(new NetworkObjectReference(g.NetworkObject),visit,computer.Lease);}
                yield return Wait(()=>g.State.Decision==RegistrationDecision.Admitted&&g.Guest.Room==reception.Rooms[i],"atomic admission");yield return Barrier(tag+"-admitted-"+i);
            }
            Station.CloseLocal();yield return Wait(()=>!Station.LocalOpen,"leave registration");yield return Barrier(tag+"-closed");
        }
        IEnumerator RegistrationReveal()
        {
            if(Host)
            {
                Field(requests,"callDelay",100000f);Field(shift,"electricalFaultCount",Vector2Int.zero);Need(shift.DebugStartScenario(900,1,30),"disguise fixture");
                Field(registration,"agenda",new Queue<GuestCase>(new[]{registration.Cases[2]}));foreach(var r in reception.Rooms)r.PrepareForHuntDebug();
            }
            yield return Wait(()=>shift.IsRunning,"disguise shift");yield return RegisterNew(0,1,"disguise");
            var guest=reception.Rooms[0].Occupant;Need(guest!=null,"Missing admitted disguise");
            yield return Wait(()=>guest.Phase==GuestPhase.InRoom,"normal guest reaches room",180);double arrived=session.Manager.ServerTime.Time;
            Need(guest.Visible&&guest.Body.enabled&&!hunt.State.Active,"Premature transformation");
            yield return OpenComputer(Station);Field(Station,"registrationTab",4);
            yield return Wait(()=>hunt.State.Active&&guest.Phase==GuestPhase.Revealed,"natural timed guest transformation",90);
            Need(session.Manager.ServerTime.Time-arrived>=40,"Disguise delay shortened");Need(!guest.Visible&&!guest.Body.enabled&&!guest.Agent.enabled,"Duplicate guest or simulation after reveal");
            Need(reception.Rooms[0].State.Status==RoomStatus.Dirty&&reception.Rooms[0].Occupant==null,"Room not vacated for turnover");
            Need(guest.GetComponent<RegisteredGuest>().State.Decision==RegistrationDecision.Missing,"Missing guest state not replicated");
            if(Host)Mark("late");yield return Wait(()=>Has("late-checked"),"late hunt observer",120);
            yield return Wait(()=>sawExploration,"existing vent exploration",120);Need(sawInfiltration,"No continuous infiltration observed");
            var surveillance=FindFirstObjectByType<VentSurveillance>();surveillance.Select(0);yield return new WaitForSecondsRealtime(.5f);CaptureFeed(surveillance,role+"-vent");
            checks.Add("Admitted disguise walked to Room 101, stayed 45 seconds, retired human body/agent, vacated room, continuously entered existing vents and explored; late join matched.");
            yield return Barrier("reveal-observed");if(Host)shift.EndForHuntDebug();yield return Wait(()=>shift.State.Phase==ShiftPhase.Finished,"finish transformed shift");
            yield return Wait(()=>!Station.LocalOpen,"workstation closed after shift");
            if(Host){yield return OpenComputer(computer);shift.StartShiftRpc(shift.State.Number,computer.Lease);}
            yield return Wait(()=>shift.IsRunning,"next shift");Need(!hunt.State.Active&&reception.Guests.All(g=>g.Phase==GuestPhase.Absent)&&!registration.Lookup.Completed,"Next shift did not clear threat and registration");
            if(Host)computer.CloseLocal();checks.Add("Next ordinary shift resets disguise, parked vehicles, searches and existing threat without reconnecting.");
        }
    }
}
#endif
