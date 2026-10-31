#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Inspection;
using MotelGame.Horror;

namespace MotelGame.Networking.Tests
{
    public sealed partial class MotelLayoutSmokeTest
    {
        string Evidence()=>scanner.State.Visit+"/"+scanner.State.Revision+"/"+scanner.State.Known+"/"+scanner.State.Asked+"/"+scanner.State.Mass+"/"+scanner.State.Metal+"\n"+
            string.Join("\n",Enumerable.Range(0,scanner.Statements.Count).Select(i=>scanner.Statements[i].Question+":"+scanner.Statements[i].Answer))+"\n"+string.Join("\n",Enumerable.Range(0,scanner.History.Count).Select(i=>scanner.History[i].Test+":"+scanner.History[i].Result));
        IEnumerator InspectionTeam()
        {
            if(role=="late")
            {
                yield return Wait(()=>scanner.MatchesWaitingGuest&&scanner.History.Count==3&&scanner.Statements.Count==5&&!scanner.State.Busy,"late shared inspection",90);
                yield return Wait(()=>File.Exists(Path.Combine(dir,"evidence.txt"))&&Evidence()==File.ReadAllText(Path.Combine(dir,"evidence.txt")),"late exact statements/history");
                Need(!scanner.OwnsControls,"Late join stole controls");checks.Add("Late join received the exact existing measurements, five guest statements and history; no ownership takeover.");
                Mark("inspection-late-checked");session.Disconnect();yield return Wait(()=>!session.Manager.IsListening,"late disconnect");yield break;
            }
            if(Host)
            {
                yield return Walk(new Vector3(21.5f,0,-10));
                yield return Wait(()=>session.Manager.ConnectedClients.Count==3,"host plus two real clients");
                Field(requests,"callDelay",100000f);Field(shift,"electricalFaultCount",Vector2Int.zero);
                Need(shift.DebugStartScenario(900,1,3),"inspection fixture");
                Field(scanner,"deck",((BaggageCase[])Get(scanner,"cases")).Where(c=>!c.containsMimic).Take(1).ToArray());
                foreach(var room in reception.Rooms)room.PrepareForHuntDebug();
            }
            yield return Wait(()=>shift.IsRunning,"inspection start");
            if(role=="client")yield return OpenScanner();
            else if(role=="registrar")yield return OpenComputer(computer);
            Mark("station-"+role);yield return Wait(()=>Has("station-client")&&Has("station-registrar"),"two clients at separate instruments");
            Need(scanner.Seat.Occupied&&computer.Occupied&&scanner.Seat.User!=computer.User,"Separate scanner/reception users");
            yield return Wait(()=>reception.WaitingNpc!=null,"ordinary guest arrived");var guest=reception.WaitingNpc;uint visit=guest.State.Visit;
            if(role=="registrar")
            {
                scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),visit,BaggageQuestion.Contents,computer.Lease,true);
                scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),visit,BaggageQuestion.Contents,computer.Lease,false);
                scanner.TestRpc(AnalyzerTest.Weight,scanner.State.Revision,visit,computer.Lease);
                yield return new WaitForSecondsRealtime(.7f);
                Need(scanner.Statements.Count==0&&scanner.State.Asked==0&&scanner.History.Count==0&&!scanner.State.Busy,"Computer could interrogate or measure");
                checks.Add("Server rejected questioning and measurement from the Reception user, including forged analyzer-origin requests.");Mark("computer-probe-done");
            }
            yield return Wait(()=>Has("computer-probe-done"),"computer denial confirmed");
            if(role=="client")
            {
                scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),visit,BaggageQuestion.Contents,scanner.Seat.Lease,false);
                scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),visit,BaggageQuestion.Contents,scanner.Seat.Lease,false);
                scanner.PlaceBagRpc(new NetworkObjectReference(guest.NetworkObject),visit);scanner.PlaceBagRpc(new NetworkObjectReference(guest.NetworkObject),visit);
                yield return Wait(()=>scanner.Current!=null&&scanner.Current.State.Phase==BaggagePhase.OnAnalyzer,"physical bag on tray");
                foreach(var test in AnalyzerPanel.PrimaryTests)yield return Measure(test);
                foreach(BaggageQuestion q in Enum.GetValues(typeof(BaggageQuestion)))
                {scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),visit,q,scanner.Seat.Lease,false);scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),visit,q,scanner.Seat.Lease,false);}
            }
            yield return Wait(()=>scanner.Statements.Count==5&&scanner.History.Count==3&&!scanner.State.Busy,"same three tests and five answers on every peer");
            Need(!scanner.WorldReadout.text.Contains("BALANCE")&&AnalyzerPanel.PrimaryTests.Length==3,"Advanced controls leaked into regular UI");
            if(!Application.isBatchMode&&role!="host")
            {
                if(role=="registrar")Field(computer,"baggage",true);
                yield return new WaitForSecondsRealtime(.3f);ScreenCapture.CaptureScreenshot(Path.Combine(dir,role+"-inspection.png"));yield return new WaitForSecondsRealtime(.4f);
            }
            Mark("record-"+role);yield return Wait(()=>Has("record-client")&&Has("record-registrar"),"clients observed shared evidence");
            if(Host){File.WriteAllText(Path.Combine(dir,"evidence.txt"),Evidence());Mark("late");}
            yield return Wait(()=>Has("inspection-late-checked"),"fourth late observer",120);
            if(role=="client")
            {
                checks.Add("Analyzer-only questions and three tests committed once despite duplicate RPCs. Reception simultaneously observed the same authoritative record.");
                session.Disconnect();yield return Wait(()=>!session.Manager.IsListening,"disconnect active analyzer owner");Mark("operator-disconnected");yield break;
            }
            if(Host)
            {
                yield return Wait(()=>Has("operator-disconnected")&&!scanner.Seat.Occupied&&session.Manager.ConnectedClients.Count==2,"operator disconnect released lease");
                Need(scanner.History.Count==3&&scanner.Statements.Count==5,"Disconnect lost evidence");
                yield return OpenScanner();yield return Measure(AnalyzerTest.Weight);
                scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),visit,BaggageQuestion.Contents,scanner.Seat.Lease,false);
                yield return new WaitForSecondsRealtime(.3f);Need(scanner.History.Count==4&&scanner.Statements.Count==5,"Takeover duplicated statements");
                scanner.GetComponent<AnalyzerPanel>().CloseLocal();yield return Wait(()=>!scanner.OwnsControls,"release recovered analyzer");Mark("takeover-complete");
                checks.Add("Disconnect preserved evidence; another player took free controls and continued the same record.");
            }
            yield return Wait(()=>Has("takeover-complete")&&scanner.History.Count==4,"recovered shared history");
            if(role=="registrar")
            {
                var room=reception.Rooms[0];
                reception.RequestCheckInRpc(0,room.State.Cycle,new NetworkObjectReference(guest.NetworkObject),visit,computer.Lease);
                reception.RequestCheckInRpc(0,room.State.Cycle,new NetworkObjectReference(guest.NetworkObject),visit,computer.Lease);
                reception.RequestRefuseRpc(new NetworkObjectReference(guest.NetworkObject),visit,computer.Lease);
                yield return Wait(()=>guest.State.Assigned,"admission decision");computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"registrar closes station");Mark("registrar-clear");
            }
            yield return Wait(()=>guest.Phase==GuestPhase.InRoom&&shift.State.CheckedIn==1,"single physical guest admitted",180);
            Need(guest.Room==reception.Rooms[0]&&reception.Rooms.Count(r=>r.State.Status==RoomStatus.Occupied)==1,"Conflicting registration outcomes");
            checks.Add("Duplicate Admit plus stale Refuse produced one guest, one occupied room and one admission. Guest physically reached Room 101.");
            Mark("admitted-"+role);yield return Wait(()=>Has("admitted-host")&&Has("admitted-registrar"),"both observed physical arrival");
            if(Host)
            {
                yield return Wait(()=>Has("registrar-clear"),"free reception");shift.EndForHuntDebug();yield return OpenComputer(computer);shift.StartShiftRpc(shift.State.Number,computer.Lease);
            }
            yield return Wait(()=>shift.IsRunning&&shift.State.Number==2,"ordinary next shift");
            Need(scanner.History.Count==0&&scanner.Statements.Count==0&&!scanner.Seat.Occupied&&shift.State.StaffAtStart==2,"Inspection reset / staff count");
            Need(reception.Rooms.Count(r=>r.State.Status==RoomStatus.Ready)==2,"Two-player handover configuration");
            if(Host)computer.CloseLocal();Mark("reset-"+role);yield return Wait(()=>Has("reset-host")&&Has("reset-registrar"),"both see next-shift reset");
            checks.Add("Ordinary next shift cleared measurement history/answers and restored the configured handover without duplicate baggage.");
            session.Disconnect();yield return Wait(()=>!session.Manager.IsListening,"inspection session disconnect");
        }

        IEnumerator SoloShift()
        {
            Need(Host,"Solo actor must host");
            yield return OpenComputer(computer);shift.StartShiftRpc(shift.State.Number,computer.Lease);
            yield return Wait(()=>shift.IsRunning,"ordinary Start Shift");
            Need(shift.State.Duration==900&&shift.State.ArrivalTarget>=3&&shift.State.ArrivalTarget<=4&&shift.State.StaffAtStart==1,"Solo ordinary profile");
            Need(reception.Rooms.Count(r=>r.State.Status==RoomStatus.Ready)==2&&shift.State.Prepared==0,"Prepared handover must not inflate work counters");
            computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"leave start computer");
            var serviced=new HashSet<ServiceRoom>();var observations=new List<string>();float last=0,idle=0;int admitted=0,refused=0;
            while(shift.IsRunning)
            {
                if(Time.realtimeSinceStartup-last>=10)
                {
                    last=Time.realtimeSinceStartup;observations.Add($"{shift.Elapsed:0}s arrivals={shift.State.Arrived}/{shift.State.ArrivalTarget} queue={reception.QueueCount} request={requests.Phase} ready={reception.Rooms.Count(r=>r.State.Status==RoomStatus.Ready)} faults={shift.State.ElectricalFaults}");
                    File.WriteAllLines(Path.Combine(dir,"solo-cadence.txt"),observations);
                }
                if(shift.State.Duration-shift.Elapsed<45){yield return new WaitForSecondsRealtime(1);continue;}
                if(reception.CheckoutNpc!=null)
                {
                    yield return OpenComputer(computer);var guest=reception.CheckoutNpc;
                    if(guest!=null){int index=Array.IndexOf(reception.Rooms,guest.Room);serviced.Add(guest.Room);reception.Checkout(index);yield return Wait(()=>guest.Phase==GuestPhase.Leaving,"ordinary checkout",30);}
                    computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"leave checkout");continue;
                }
                if(reception.WaitingNpc!=null&&reception.Rooms.Any(r=>r.AvailableForCheckIn))
                {
                    Need(shift.State.Arrived>0&&shift.State.Arrived<=shift.State.ArrivalTarget,"Ordinary arrival accounting");
                    yield return OpenScanner();var guest=reception.WaitingNpc;
                    if(guest==null){scanner.GetComponent<AnalyzerPanel>().CloseLocal();continue;}
                    scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),guest.State.Visit,BaggageQuestion.Contents,scanner.Seat.Lease,false);
                    scanner.PlaceBagRpc(new NetworkObjectReference(guest.NetworkObject),guest.State.Visit);
                    yield return Wait(()=>scanner.Current!=null&&scanner.Current.Guest==guest&&scanner.Current.State.Phase==BaggagePhase.OnAnalyzer,"ordinary bag placed");
                    foreach(var test in AnalyzerPanel.PrimaryTests)yield return Measure(test);
                    foreach(BaggageQuestion q in Enum.GetValues(typeof(BaggageQuestion)))scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),guest.State.Visit,q,scanner.Seat.Lease,false);
                    yield return Wait(()=>scanner.Statements.Count==5,"ordinary follow-ups");
                    // Explicit test oracle only: refuse the authored carrier so the full ordinary service
                    // cadence can be observed independently of combat. No gameplay parameters are changed.
                    bool reject=guest.GetComponent<GuestBaggage>().ServerCase.containsMimic;
                    scanner.GetComponent<AnalyzerPanel>().CloseLocal();yield return Wait(()=>!scanner.OwnsControls,"leave ordinary analyzer");
                    yield return OpenComputer(computer);
                    if(reject){reception.RefuseWaitingGuest();refused++;yield return Wait(()=>guest.Phase==GuestPhase.Leaving,"ordinary refusal");}
                    else{var room=reception.Rooms.First(r=>r.AvailableForCheckIn);serviced.Add(room);reception.SelectRoom(Array.IndexOf(reception.Rooms,room));admitted++;yield return Wait(()=>guest.State.Assigned,"ordinary admission");}
                    computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"leave admission computer");continue;
                }
                if(requests.Phase==GuestRequestPhase.Ringing)
                {yield return Approach(requests.Phone.TargetCollider);Use(requests.Phone,requests.Phone.AimPoint);yield return Wait(()=>requests.Phase!=GuestRequestPhase.Ringing,"ordinary answer");continue;}
                if(requests.Phase==GuestRequestPhase.PendingDelivery)
                {
                    var room=requests.Room;var kind=requests.State.Kind;
                    if(kind==GuestRequestKind.CleanTowel)
                    {
                        yield return Approach(requests.TowelSupply.TargetCollider);Use(requests.TowelSupply,requests.TowelSupply.AimPoint);yield return Wait(()=>own.GetComponent<TowelCarrier>().HasTowel,"ordinary towel");
                        yield return Walk(room.DoorOutside.position);yield return Approach(room.Delivery.TargetCollider);Use(room.Delivery,room.Delivery.AimPoint);
                    }
                    else if(kind==GuestRequestKind.RepairTelevision)
                    {
                        yield return Walk(room.DoorInside.position);
                        foreach(var control in room.Television.Controls){yield return Approach(control.TargetCollider);Use(control,control.AimPoint);yield return Wait(()=>requests.State.RepairStep>control.Step,"ordinary TV repair");}
                        yield return Walk(room.DoorOutside.position);
                    }
                    else{yield return Approach(room.Breaker.TargetCollider);Use(room.Breaker,room.Breaker.AimPoint);}
                    yield return Wait(()=>requests.Phase==GuestRequestPhase.Completed,"ordinary request complete",30);continue;
                }
                var dirty=serviced.FirstOrDefault(r=>r.State.Status==RoomStatus.Dirty);
                if(dirty!=null)
                {
                    yield return Walk(dirty.DoorInside.position);yield return Clean(dirty);
                    var dumpster=FindFirstObjectByType<GarbageDumpster>();yield return Approach(dumpster.TargetCollider);Use(dumpster,dumpster.AimPoint);
                    yield return Wait(()=>dirty.State.Status==RoomStatus.Ready,"ordinary turnover");continue;
                }
                var fault=reception.Rooms.FirstOrDefault(r=>r.State.Faulted);
                if(fault!=null){yield return Approach(fault.Breaker.TargetCollider);Use(fault.Breaker,fault.Breaker.AimPoint);yield return Wait(()=>!fault.State.Faulted,"ordinary empty-room fault");continue;}
                yield return Walk(new Vector3(21.5f,0,-10));idle+=1;yield return new WaitForSecondsRealtime(1);
            }
            Need(shift.State.Phase==ShiftPhase.Finished&&shift.State.Arrived>=3&&admitted>=2&&shift.State.CheckedOut>=1&&shift.State.TrashDisposed>=1,"Incomplete ordinary service loop");
            Need(shift.State.MissedRequests==0&&shift.State.Unserved==0,"Ordinary workload prevented completion");
            checks.Add($"Real 900-second ordinary shift via Start Shift, no F8/timing override: {shift.State.Arrived} arrivals, {admitted} admitted, {refused} refused, {shift.State.CheckedOut} checkout, {shift.State.TrashDisposed} disposals, {shift.State.Towels} towels, {shift.State.Repairs} TV repairs, {shift.State.ElectricalFaults} faults; {idle:0}+ seconds of explicit idle recovery. Test oracle refused carriers; deduction usability was not evaluated.");
            uint number=shift.State.Number;yield return OpenComputer(computer);shift.StartShiftRpc(number,computer.Lease);
            yield return Wait(()=>shift.IsRunning&&shift.State.Number==number+1,"normal next shift after full night");
            Need(reception.Rooms.Count(r=>r.State.Status==RoomStatus.Ready)==2&&scanner.History.Count==0&&scanner.Statements.Count==0&&shift.State.CheckedIn==0,"Next ordinary shift reset");
            computer.CloseLocal();checks.Add("Next shift reset successfully, preserving two prepared handover rooms and clearing all inspection/service counters.");session.Disconnect();yield return Wait(()=>!session.Manager.IsListening,"solo disconnect");
        }
    }
}
#endif
