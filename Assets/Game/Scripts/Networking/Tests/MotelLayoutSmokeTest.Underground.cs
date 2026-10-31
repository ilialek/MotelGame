#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Netcode.Components;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Registration;

namespace MotelGame.Networking.Tests
{
    public sealed partial class MotelLayoutSmokeTest
    {
        Vector3 testHome;
        bool lastUndergroundVisible;
        Vector3 lastUndergroundPosition;
        float lastUndergroundTime, totalUndergroundTravel;
        void ObserveUnderground()
        {
            if (hunt == null || !hunt.IsSpawned)
                return;
            bool shown = hunt.Visible && hunt.Body.enabled;
            if ((Host || !Has("hunt-fixtures")) && shown && lastUndergroundVisible)
            {
                float d = Vector3.Distance(lastUndergroundPosition, hunt.transform.position), dt = Mathf.Max(Time.deltaTime, Time.realtimeSinceStartup - lastUndergroundTime);
                if (d > dt * 3 + .3f)
                    errors.Add("Visible underground discontinuity: distance=" + d + " dt=" + dt + " frameDt=" + Time.deltaTime + " phase=" + hunt.State.Phase);
                totalUndergroundTravel += d;
            }

            lastUndergroundVisible = shown;
            lastUndergroundPosition = hunt.transform.position;
            lastUndergroundTime = Time.realtimeSinceStartup;
        }

        FirstPersonController Movement => own.GetComponent<FirstPersonController>();
        NetworkPlayer Player => own.GetComponent<NetworkPlayer>();
        VentHatch Hatch(int room) => FindObjectsByType<VentHatch>(FindObjectsSortMode.None).Single(h => h.room.RoomNumber == room.ToString());
        IEnumerator Crouch(bool value)
        {
            if(Application.isFocused&&Cursor.lockState==CursorLockMode.Locked)
            {
                Movement.InputSuspended=false;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,value?new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.LeftCtrl):new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return new WaitForSecondsRealtime(.15f);Movement.InputSuspended=true;
                checks.Add("Hold/release Left Ctrl keyboard input exercised on "+role);
            }
            else Movement.RequestCrouch(value);
            yield return Wait(() => Movement.Crouched == value && Player.Crouched == value, "posture " + value);
            Need(Mathf.Abs(own.GetComponent<CharacterController>().height - (value ? .9f : 1.8f)) < .01f, "Capsule height");
        }

        IEnumerator ToggleGrille(VentHatch h, bool open)
        {
            if (h.State.Open == open)
            {
                yield return Wait(() => !h.IsMoving, "grille settled");
                yield break;
            }

            yield return new WaitForSecondsRealtime(.3f);
            var before = own.transform.position;
            Use(h, h.leaf.bounds.center);
            yield return Wait(() => h.State.Open == open && !h.IsMoving, "direct grille interaction " + h.room.RoomNumber);
            Need(Vector3.Distance(before, own.transform.position) < .12f, "Opening grille moved the player");
            checks.Add("Direct grille raycast/RPC; no player displacement, Room " + h.room.RoomNumber);
        }

        IEnumerator HatchUse(VentHatch h, bool down)
        {
            if (down)
                yield return ToggleGrille(h, true);
            var points = down ? h.route : h.route.Reverse().ToArray();
            if (!down && !h.State.Open)
            {
                foreach (var p in h.route.Reverse().Take(h.route.Length - 3))
                    yield return Crawl(p.position);
                yield return ToggleGrille(h, true);
                points = h.route.Take(3).Reverse().ToArray();
            }

            foreach (var p in points)
            {
                yield return Crawl(p.position);
                if(!Application.isBatchMode && (p==h.route[2]||p==h.route[4]||p==h.route[^2]))
                {
                    int index=Array.IndexOf(h.route,p),next=Mathf.Clamp(index+(down?1:-1),0,h.route.Length-1);
                    view.transform.LookAt(h.route[next].position+Vector3.up*.78f);
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir,$"ramp-{h.room.RoomNumber}-{(down?"down":"up")}-{index}.png"));
                    yield return new WaitForSecondsRealtime(.15f);
                }
                if (p == h.route[3])
                {
                    typeof(FirstPersonController).GetMethod("UpdatePosture",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Movement,new object[]{false});
                    yield return new WaitForSecondsRealtime(.15f);
                    Need(Movement.Crouched, "Standing was allowed inside ramp");
                    var idle = own.transform.position;
                    yield return new WaitForSecondsRealtime(.8f);
                    Need(Vector3.Distance(idle, own.transform.position) < .08f, "Uncontrolled sliding on ramp: " + (own.transform.position - idle));
                }
            }

            if(!down)
            {
                yield return Wait(()=>{typeof(FirstPersonController).GetMethod("UpdatePosture",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Movement,new object[]{false});return !Movement.Crouched&&!Player.Crouched;},"Auto stand after leaving low passage");
                checks.Add("Released Ctrl remains crouched inside and automatically stands in Room "+h.room.RoomNumber);
            }
            Need(Vector3.Distance(own.transform.position, down ? h.lower.position : h.upper.position) < .18f, "Ramp endpoint");
        }

        IEnumerator Crawl(Vector3 target)
        {
            Progress("Physical crawl " + target);
            float timeout = Time.realtimeSinceStartup + 25;
            var move = typeof(FirstPersonController).GetMethod("MovePlayer", BindingFlags.Instance | BindingFlags.NonPublic);
            // Drive the exact normal input movement function; no path/position setter exists in production.
            // Disable Update only for deterministic input in two background player windows.
            Movement.enabled = false;
            try
            {
                while (true)
                {
                    var delta = target - own.transform.position;
                    delta.y = 0;
                    if (delta.magnitude < .065f)
                        break;
                    Need(Time.realtimeSinceStartup < timeout, "Crawl blocked: " + own.transform.position + " -> " + target);
                    own.transform.rotation = Quaternion.LookRotation(delta);
                    move.Invoke(Movement, new object[]{new Vector2(0, Mathf.Min(1, delta.magnitude / Mathf.Max(.001f, 1.7f * Time.deltaTime))), 1.7f});
                    Need(own.transform.position.y > -3.3f, "Fell through ramp");
                    yield return null;
                }
            }
            finally
            {
                Movement.enabled = true;
            }

            yield return new WaitForSecondsRealtime(.1f);
        }

        int[] GraphPath(int start, int goal)
        {
            var previous = Enumerable.Repeat(-1, hunt.Nodes.Length).ToArray();
            var q = new Queue<int>();
            q.Enqueue(start);
            previous[start] = start;
            while (q.Count > 0)
            {
                int n = q.Dequeue();
                if (n == goal)
                    break;
                foreach (var next in hunt.Nodes[n].neighbours)
                    if (previous[next.index] < 0)
                    {
                        previous[next.index] = n;
                        q.Enqueue(next.index);
                    }
            }

            Need(previous[goal] >= 0, "Connected route");
            var path = new List<int>();
            for (int at = goal; at != start; at = previous[at])
                path.Add(at);
            path.Add(start);
            path.Reverse();
            return path.ToArray();
        }

        IEnumerator CrawlBetween(VentHatch from, VentHatch to)
        {
            yield return Crawl(from.lower.position);
            // Every straight entrance now meets the unchanged central cross-corridor.
            Need(Mathf.Abs(from.lower.position.z-to.lower.position.z)<.01f,"Shared main crossing");
            yield return Crawl(to.lower.position);
        }

        IEnumerator DoorClearanceProbe(MotelGame.Interactions.NetworkDoor door)
        {
            var hinge=(Transform)Get(door,"hinge");var leaf=(BoxCollider)Get(door,"leaf");
            var center=hinge.parent.TransformPoint(hinge.localPosition+leaf.transform.localPosition+Vector3.Scale(leaf.center,leaf.transform.localScale));
            var size=Vector3.Scale(leaf.size,leaf.transform.localScale);
            var normal=hinge.parent.TransformDirection(leaf.transform.localRotation*(size.x<size.z?Vector3.right:Vector3.forward));center.y=.025f;
            FixturePlace(center+normal*.8f,door.InteractionPoint);
            yield return new WaitForSecondsRealtime(.3f);
            if(door.IsOpen){Use(door,door.InteractionPoint);yield return Wait(()=>!door.IsOpen&&!door.IsMoving,"door initial closed");}
            FixturePlace(center+normal*.38f,door.InteractionPoint);
            yield return new WaitForSecondsRealtime(.3f);var before=own.transform.position;
            Use(door,door.InteractionPoint);yield return Wait(()=>door.IsOpen&&!door.IsMoving,"door opens directly in front of capsule");
            Need(Vector3.Distance(before,own.transform.position)<.12f,"Door opening displaced player");
            FixturePlace(center,door.InteractionPoint);yield return new WaitForSecondsRealtime(.3f);before=own.transform.position;
            door.RequestToggleRpc();yield return Wait(()=>!door.IsOpen&&!door.IsMoving,"door closes through occupant");
            Need(!door.PassageBlocked&&Vector3.Distance(before,own.transform.position)<.12f,"Door closed blocker trapped player");
            yield return Crawl(center+normal*.8f);yield return Wait(()=>door.PassageBlocked,"door blocker arms after occupant exits");
            Movement.enabled=false;
            try
            {
                own.transform.rotation=Quaternion.LookRotation(-normal);
                var move=typeof(FirstPersonController).GetMethod("MovePlayer",BindingFlags.Instance|BindingFlags.NonPublic);
                float until=Time.realtimeSinceStartup+1;
                while(Time.realtimeSinceStartup<until){move.Invoke(Movement,new object[]{Vector2.up,1.7f});yield return null;}
            }
            finally{Movement.enabled=true;}
            Need(Vector3.Dot(own.transform.position-center,normal)>.25f,"Closed door allowed capsule through");
            Use(door,door.InteractionPoint);yield return Wait(()=>door.IsOpen&&!door.IsMoving,"reopen blocked door with direct raycast");
            checks.Add("Actual CharacterController: near-contact door E opens without displacement; closing through occupant defers blocker; walking out arms blocker; closed passage stops movement.");
        }

        IEnumerator UndergroundRoutes()
        {
            testHome = own.transform.position;
            if (role == "late")
            {
                yield return Wait(() => Hatch(101).IsSpawned && Hatch(101).State.Open && !Hatch(101).IsMoving, "late open grille");
                Need(Mathf.Abs(Hatch(101).CurrentAngle - 90) < .1f, "Late grille pose");
                checks.Add("Late join restores server grille state and hinge angle.");
                Mark("late-checked");
                yield break;
            }

            yield return Barrier("connected");
            Movement.InputSuspended = true;
            if (Host)
                Need(shift.DebugStartScenario(2400, 0, 30), "navigation-only shift");
            yield return Wait(() => shift.IsRunning, "shift");
            if (!Host)
            {
                yield return Walk(new Vector3(27, 0, -1));
                Mark("observer-clear");
            }
            else
            {
                yield return Wait(() => Has("observer-clear"), "clear lobby");
                if(!Environment.GetCommandLineArgs().Contains("-motel-grille-only"))
                {
                var h = Hatch(101);
                yield return Walk(h.room.DoorOutside.position);
                yield return DoorClearanceProbe(h.room.Door);
                yield return Walk(h.upper.position, 1 | (1 << h.room.NavigationArea));
                Need(!Movement.Crouched, "Initial grille test must start standing");
                yield return ToggleGrille(h, true);
                checks.Add("Standing player opens recessed grille before crouching; no repositioning");
                yield return Crouch(true);
                Player.ToggleFlashlight();
                yield return Wait(() => Player.FlashlightOn, "flashlight");
                yield return ToggleGrille(h, true);
                if (!Application.isBatchMode)
                {
                    view.transform.LookAt(h.leaf.bounds.center);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "host-wall-grille.png"));
                    yield return null;
                }

                yield return HatchUse(h, true);
                foreach (int n in new[]{102, 103, 104, 105, 106, 101})
                {
                    var other = Hatch(n);
                    yield return CrawlBetween(h, other);
                    yield return HatchUse(other, false);
                    yield return Crouch(false);
                    yield return Crouch(true);
                    yield return HatchUse(other, true);
                    h = other;
                    checks.Add("Normal controller collision up/down ramp and unchanged graph, Room " + n);
                }

                yield return HatchUse(h, false);
                yield return Crouch(false);
                Mark("late");
                yield return Wait(() => Has("late-checked"), "late grille snapshot", 100);
                }
                else Player.ToggleFlashlight();
                var g = Hatch(106);
                yield return Walk(g.room.DoorOutside.position);
                yield return Walk(g.upper.position, 1 | (1 << g.room.NavigationArea));
                yield return Crouch(true);
                yield return ToggleGrille(g, false);
                FixturePlace(testHome, Vector3.zero);
                yield return Crouch(false);
                Mark("host-routes");
            }

            if (!Host)
            {
                yield return Wait(() => Has("host-routes"), "six ramps", 700);
                var h = Hatch(106);
                yield return Walk(h.room.DoorOutside.position);
                yield return Walk(h.upper.position, 1 | (1 << h.room.NavigationArea));
                yield return Crouch(true);
                Player.ToggleFlashlight();
                Mark("client-ready");
            }
            else
            {
                yield return Wait(() => Has("client-ready"), "client ready", 180);
                var g = Hatch(106);
                FixturePlace(g.transform.TransformPoint(new Vector3(4.3f, .025f, 5.1f)), g.leaf.bounds.center);
                yield return new WaitForSecondsRealtime(.4f);
                File.WriteAllText(Path.Combine(dir, "toggle-at.txt"), (session.Manager.ServerTime.Time + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            yield return Wait(() => File.Exists(Path.Combine(dir, "toggle-at.txt")), "shared toggle time");
            double at = double.Parse(File.ReadAllText(Path.Combine(dir, "toggle-at.txt")), System.Globalization.CultureInfo.InvariantCulture);
            uint revision = Hatch(106).State.Revision;
            yield return Wait(() => session.Manager.ServerTime.Time >= at, "simultaneous grille use");
            if (Host)
                Need((ServiceInteractionValidation.CanReach(session.Manager, own.OwnerClientId, Hatch(106).leaf)||ServiceInteractionValidation.CanReach(session.Manager, own.OwnerClientId, Hatch(106).leaf,useCenter:true)), "Concurrent test host cannot reach leaf");
            Hatch(106).UseRpc(revision);
            Hatch(106).UseRpc(revision);
            yield return Wait(() => Hatch(106).State.Open && !Hatch(106).IsMoving, "one shared open transition");
            Need(Hatch(106).State.Revision == revision + 1, "Repeated simultaneous requests toggled twice");
            if (!Host)
            {
                var h = Hatch(106);
                yield return Crawl(h.route[1].position);
                Mark("in-opening");
                var before=own.transform.position;
                yield return Wait(() => Has("closed-occupied"), "safe close through occupant");
                Need(!h.PassageBlocked&&Vector3.Distance(before,own.transform.position)<.12f,"Closing blocked/displaced occupying client");
                foreach (var p in h.route.Skip(2))
                    yield return Crawl(p.position);
                Mark("opening-clear");
                var other = Hatch(105);
                yield return CrawlBetween(h, other);
                yield return HatchUse(other, false);
                yield return Crouch(false);
                Mark("client-routes");
            }
            else
            {
                yield return Wait(() => Has("in-opening"), "client in grille");
                yield return new WaitForSecondsRealtime(.4f);
                var h = Hatch(106);
                uint before = h.State.Revision;
                Need(ServiceInteractionValidation.CanReach(session.Manager, own.OwnerClientId, h.leaf), "Close test host cannot reach leaf");
                h.UseRpc(before);
                yield return new WaitForSecondsRealtime(.8f);
                Need(!h.State.Open && h.State.Revision == before+1 && !h.PassageBlocked, "Occupied closing animation or deferred blocker failed");
                Mark("closed-occupied");
                yield return Wait(()=>Has("opening-clear")&&h.PassageBlocked,"Closed blocker activates after departure");
                FixturePlace(testHome, Vector3.zero);
                yield return Crouch(false);
                yield return Wait(() => Has("client-routes"), "client physical ramp route", 240);
            }

            foreach (var p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                Need(p.GetComponent<FirstPersonController>().enabled == p.IsOwner, "Owner movement");
                Need(p.GetComponentInChildren<Camera>(true).enabled == p.IsOwner, "Owner camera");
                Need(p.FlashlightOn && p.GetComponentInChildren<Light>(true).enabled, "Remote flashlight");
            }

            checks.Add("Host/client: repeated concurrent request accepted once, occupied grille closes visually without blocking; blocker activates only after clearance, remote crouch and torch, client 106 -> 105 via real slopes, ownership/disconnect.");
            yield return UndergroundDisconnect();
        }

        IEnumerator UndergroundDisconnect()
        {
            yield return Barrier("underground-done");
            if (!Host)
            {
                session.Disconnect();
                yield return Wait(() => !session.Manager.IsListening, "client disconnect");
                Mark("client-gone");
            }
            else
            {
                yield return Wait(() => Has("client-gone") && session.Manager.ConnectedClients.Count == 1, "host cleanup");
                session.Disconnect();
            }
        }

        [Serializable]
        class UndergroundCommand
        {
            public int id;
            public string action;
            public Vector3 position, look;
            public bool crouch;
        }

        int commandId;
        bool clientDone;
        IEnumerator Command(string action, Vector3 position = default, Vector3 look = default, bool crouch = true)
        {
            var c = new UndergroundCommand{id = ++commandId, action = action, position = position, look = look, crouch = crouch};
            string path = Path.Combine(dir, "command-" + c.id + ".json");
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(c));
            File.Move(path + ".tmp", path);
            yield return Wait(() => File.Exists(Path.Combine(dir, "ack-" + c.id + ".signal")), "client " + action, 60);
        }

        void FixturePlace(Vector3 position, Vector3 look)
        {
            var cc = own.GetComponent<CharacterController>();
            cc.enabled = false;
            own.transform.position = position;
            cc.enabled = true;
            Vector3 direction = look - (position + Vector3.up * Movement.EyeHeight);
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude > .01f)
                own.transform.rotation = Quaternion.LookRotation(flat);
            view.transform.LookAt(look);
            Field(Movement, "pitch", -Mathf.Asin(view.transform.forward.y) * Mathf.Rad2Deg);
            Physics.SyncTransforms();
        }

        IEnumerator UndergroundClient()
        {
            Movement.InputSuspended = true;
            yield return Wait(() => shift.IsRunning, "hunt shift");
            yield return Approach(hunt.Firearm.PickupCollider);
            Use(hunt.Firearm, hunt.Firearm.PickupCollider.bounds.center);
            yield return Wait(() => hunt.Firearm.LocallyHeld, "client pistol ownership");
            yield return Walk(new Vector3(27, 0, -1));
            Mark("armed");
            int seen = 0;
            while (!clientDone)
            {
                string path = Path.Combine(dir, "command-" + (seen + 1) + ".json");
                if (!File.Exists(path))
                {
                    yield return null;
                    continue;
                }

                var c = JsonUtility.FromJson<UndergroundCommand>(File.ReadAllText(path));
                if (c == null || c.id == seen)
                {
                    yield return null;
                    continue;
                }

                seen = c.id;
                if (c.action == "pose")
                {
                    FixturePlace(c.position, c.look);
                    yield return new WaitForSecondsRealtime(.4f);
                    yield return Crouch(c.crouch);
                    view.transform.LookAt(c.look);
                    Field(Movement, "pitch", -Mathf.Asin(view.transform.forward.y) * Mathf.Rad2Deg);
                    yield return new WaitForSecondsRealtime(.4f);
                }
                else if (c.action == "shot")
                {
                    yield return Wait(() => !own.GetComponent<TowelCarrier>().Incapacitated, "recover before shot");
                    yield return Wait(() => hunt.Visible && hunt.Body.enabled, "client visible shot target");
                    // Wait for the test fixture teleport to arrive, then track the actual replicated moving hitbox.
                    float aimUntil = Time.realtimeSinceStartup + .4f;
                    while (Time.realtimeSinceStartup < aimUntil)
                    {
                        Vector3 d = hunt.Body.bounds.center - view.transform.position;
                        own.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(d, Vector3.up));
                        view.transform.LookAt(hunt.Body.bounds.center);
                        yield return null;
                    }

                    if (!Player.FlashlightOn)
                        Player.ToggleFlashlight();
                    view.transform.LookAt(hunt.Body.bounds.center);
                    Field(Movement, "pitch", -Mathf.Asin(view.transform.forward.y) * Mathf.Rad2Deg);
                    Physics.SyncTransforms();
                    File.AppendAllText(Path.Combine(dir, "shots.txt"), "Client eye=" + view.transform.position + " target=" + hunt.Body.bounds.center + " HP=" + hunt.State.Health + " rays=" + string.Join(";", Physics.RaycastAll(view.transform.position, view.transform.forward, 30, ~0, QueryTriggerInteraction.Ignore).Where(h => !h.collider.transform.IsChildOf(own.transform)).OrderBy(h => h.distance).Select(h => h.collider.name + "/" + h.distance)) + "\n");
                    hunt.Firearm.FireLocal();
                    yield return new WaitForSecondsRealtime(.6f);
                }
                else if (c.action == "capture")
                {
                    view.transform.LookAt(hunt.transform.position);
                    if (!Application.isBatchMode)
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, "client-hunt.png"));
                    yield return new WaitForSecondsRealtime(.3f);
                }
                else if (c.action == "done")
                    clientDone = true;
                string ack = Path.Combine(dir, "ack-" + c.id + ".signal");
                File.WriteAllText(ack + ".tmp", c.id.ToString());
                File.Move(ack + ".tmp", ack);
            }

            checks.Add("Real client-owned pistol RPCs, crouch, flashlight and independent view snapshots; shared threat state observed.");
        }

        void FixtureAttempt(GuestLifecycle guest, MimicPhase phase = MimicPhase.ActiveHunt)
        {
            var node = hunt.Nodes.Single(n => n.room == guest.Room);
            hunt.GetComponent<NetworkTransform>().Teleport(node.transform.position, Quaternion.identity, Vector3.one);
            var nv = (NetworkVariable<MimicSnapshot>)Get(hunt, "state");
            var s = nv.Value;
            s.Node = node.index;
            s.Phase = phase;
            s.Since = session.Manager.ServerTime.Time;
            s.Until = s.Since + 30;
            nv.Value = s;
            Field(hunt, "target", guest);
            Field(hunt, "commitAt", 0d);
            Field(hunt, "activeUntil", s.Since + 30);
            Field(hunt, "nextPlan", s.Since);
            var route = (List<int>)Get(hunt, "route");
            route.Clear();
            if (phase == MimicPhase.ActiveHunt)
                route.Add(node.index);
            Field(hunt, "routeStep", 0);
            lastUndergroundVisible = false;
        }

        IEnumerator UndergroundHunt()
        {
            testHome = own.transform.position;
            if (role == "late")
            {
                yield return Wait(() => hunt.IsSpawned && hunt.State.Active && hunt.Remains.Count == 1, "late sequential threat");
                Need(reception.Guests.Count(g => g.Phase == GuestPhase.Revealed) == 2, "Late duplicate disguised representation");
                Need(FindObjectsByType<MimicHunt>(FindObjectsSortMode.None).Length == 1, "Late multiple monsters");
                Need(!hunt.Visible, "Late hidden monster rendered");
                checks.Add("Late join restored active hidden threat, two revealed NPCs, one eliminated sphere and authoritative room states.");
                Mark("late-checked");
                yield break;
            }

            yield return Barrier("connected");
            Movement.InputSuspended = true;
            if (!Host)
            {
                yield return UndergroundClient();
                yield return UndergroundDisconnect();
                yield break;
            }

            Field(requests, "callDelay", 100000f);
            Field(shift, "electricalFaultCount", Vector2Int.zero);
            Need(shift.DebugStartScenario(3600, 6, 3), "hunt fixture shift");
            foreach (var r in reception.Rooms)
                r.PrepareForHuntDebug();
            var catalog = new[]{registration.Cases[2], registration.Cases[5], registration.Cases[0], registration.Cases[1], registration.Cases[3], registration.Cases[4]}.Select(Instantiate).ToArray();
            foreach (var c in catalog)
                c.disguiseSeconds = 10000;
            Field(registration, "agenda", new Queue<GuestCase>(catalog));
            foreach (var g in reception.Guests)
                Field(g, "stayDuration", 1800f);
            yield return Walk(new Vector3(20, 0, -11));
            yield return Wait(() => Has("armed"), "client armed");
            for (int i = 0; i < 6; i++)
            {
                yield return Wait(() => reception.WaitingNpc != null, "fixture arrival " + i, 180);
                var guest = reception.WaitingNpc;
                var room = reception.Rooms[i];
                Need(guest.TryCheckIn(room, room.State.Cycle), "fixture room assignment");
                registration.RecordDecisionOnServer(guest, true);
            }

            yield return Wait(() => reception.Guests.Count(g => g.Phase == GuestPhase.InRoom) == 6, "all six real NPC routes", 180);
            var activeSlots=reception.Guests.Where(g=>g.Phase==GuestPhase.InRoom).Select(g=>g.GetComponent<RegisteredGuest>().State.ParkingSlot).ToArray();
            Need(activeSlots.Length==6&&activeSlots.All(n=>n!=0)&&activeSlots.Distinct().Count()==6,"Six simultaneous guests share a parking slot");
            checks.Add("Six concurrent vehicle reservations have distinct actual parking slots: "+string.Join(",",activeSlots));
            var first = reception.Rooms[0].Occupant;
            var second = reception.Rooms[1].Occupant;
            Field(hunt, "excursionInterval", new Vector2(2, 2));
            Field(hunt, "preparationSeconds", new Vector2(3, 3));
            Field(hunt, "excursionDuration", new Vector2(50, 50));
            Field(hunt, "hiddenTransitSeconds", new Vector2(1, 1));
            Field(hunt, "nextMimicDelay", 10f);
            Need(hunt.QueueGuestOnServer(first) && hunt.QueueGuestOnServer(second), "queue two admitted Mimics");
            Need(!hunt.QueueGuestOnServer(first), "duplicate queue accepted");
            second.ExtendStayOnServer(session.Manager.ServerTime.Time + .2);
            yield return Wait(() => hunt.State.Active && first.Phase == GuestPhase.Revealed, "first activation");
            uint firstRevision = hunt.State.Revision;
            yield return new WaitForSecondsRealtime(.5f);
            Need(second.Phase == GuestPhase.InRoom && !second.CheckoutDue && second.Visible, "Dormant Mimic checked out");
            yield return Wait(() => hunt.State.Phase == MimicPhase.PreparingExcursion, "warning");
            double warningAt = session.Manager.ServerTime.Time;
            Need(hunt.ActivityText.Contains("SECTOR"), "No coarse warning");
            yield return Wait(() => hunt.Visible, "physical emergence");
            Need(session.Manager.ServerTime.Time - warningAt >= 2.8, "Warning skipped");
            int alertsBeforeEmergence=hunt.LocalActivityAlertCount;
            yield return Wait(() => hunt.State.Phase == MimicPhase.ActiveHunt, "active graph hunt", 40);
            Need(hunt.LocalActivityAlertCount==alertsBeforeEmergence+1,"Emergence did not trigger exactly one activity alert");
            if(!Application.isBatchMode)Need(FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Any(a=>a.name=="UndergroundActivityAlert"&&a.isPlaying&&a.clip!=null),"Activity alert audio not playing");
            Need(Mathf.Abs((float)Get(hunt,"ductSpeed")-.75f)<.001f&&Mathf.Abs((float)Get(hunt,"retreatSpeed")-.9f)<.001f,"Mimic tuning not applied");
            checks.Add("Physical emergence sends one general activity alert; speeds .75/.9m/s below crouch 1.7m/s.");
            var nearest = reception.Guests.Where(g => g.Phase == GuestPhase.InRoom && g.GetComponent<RegisteredGuest>().IsMimicOnServer == false).OrderBy(g => hunt.GraphDistance(hunt.State.Node, hunt.Nodes.Single(n => n.room == g.Room).index)).First();
            Need(hunt.DebugTarget == nearest.GuestName, "Nearest graph target selection");
            Field(hunt, "excursionInterval", new Vector2(1000, 1000));
            yield return Wait(() => shift.State.GuestDeaths == 1, "unobserved physical attack", 90);
            Need(reception.Guests.Count(g => g.Phase == GuestPhase.Dead) == 1, "Duplicate death");
            yield return Wait(() => hunt.State.Phase == MimicPhase.Hidden, "physical escape and hidden transit", 100);
            Need(totalUndergroundTravel > 2 && !hunt.Visible && !hunt.Body.enabled && hunt.State.Health == 100, "Hidden retreat or movement");
            Need(second.Phase == GuestPhase.InRoom && hunt.State.Revision == firstRevision, "Escape activated another Mimic");
            checks.Add("Real admission routes; two eligible Mimics queued. Warning -> physical emergence -> nearest room -> one offscreen death -> physical hidden retreat. No second activation on escape.");
            var protectedGuest = reception.Guests.First(g => g.Phase == GuestPhase.InRoom && !g.GetComponent<RegisteredGuest>().IsMimicOnServer && (g.Room.RoomNumber == "103" || g.Room.RoomNumber == "106"));
            var attackNode = hunt.Nodes.Single(n => n.room == protectedGuest.Room);
            Vector3 at = attackNode.transform.position;
            Vector3 axis = protectedGuest.Room.RoomNumber == "103" ? Vector3.forward : Vector3.back;
            var floorAt = new Vector3(at.x, -3.075f, at.z);
            Mark("hunt-fixtures");
            FixtureAttempt(protectedGuest);
            yield return Wait(() => (double)Get(hunt, "commitAt") > 0, "commit window armed");
            FixturePlace(Hatch(int.Parse(protectedGuest.Room.RoomNumber)).upper.position, Vector3.zero);
            yield return Wait(() => hunt.State.Phase == MimicPhase.Retreating, "room entry on commit tick cancels death");
            Need(protectedGuest.Alive && shift.State.GuestDeaths == 1, "Commit window entry failed");
            FixturePlace(testHome, Vector3.zero);
            checks.Add("Room-entry race fixture: entering after the attack arms but before commit cancels the death on that server frame.");
            yield return Command("pose", Hatch(int.Parse(protectedGuest.Room.RoomNumber)).upper.position, Vector3.up * 3, false);
            FixtureAttempt(protectedGuest);
            yield return Wait(() => hunt.State.Phase == MimicPhase.Retreating, "room occupancy cancels attempt");
            Need(protectedGuest.Alive && shift.State.GuestDeaths == 1, "Killed observed room occupant");
            yield return Command("pose", floorAt + axis * 1.8f, floorAt + axis * 6, true);
            FixtureAttempt(protectedGuest);
            yield return Wait(() => hunt.State.Phase == MimicPhase.Retreating, "close interception cancels attempt");
            Need(protectedGuest.Alive, "Proximity protection failed");
            yield return Command("pose", floorAt + axis * 4.6f, at, true);
            FixtureAttempt(protectedGuest);
            yield return Wait(() => hunt.State.Phase == MimicPhase.Retreating, "remote camera LOS cancels attempt");
            Need(protectedGuest.Alive, "Remote line-of-sight protection failed");
            checks.Add("Server attack fixtures: remote player inside room, close in tunnel, or observing beyond 3m all cancel immediately; no wait-for-look-away and no guest death.");
            FixtureAttempt(protectedGuest);
            yield return Command("shot");
            Need(hunt.State.Health == 66 && hunt.State.Phase == MimicPhase.Retreating, "Client pistol damage/retreat: HP=" + hunt.State.Health + " phase=" + hunt.State.Phase);
            // Cornered fixture: real player capsules block both exits; defense must warn before its existing stagger event.
            yield return Command("pose", floorAt - axis * .95f, at, true);
            FixturePlace(floorAt + axis * .85f, at);
            yield return Crouch(true);
            yield return new WaitForSecondsRealtime(.3f);
            FixtureAttempt(protectedGuest, MimicPhase.Retreating);
            yield return Wait(() => hunt.State.Phase == MimicPhase.Defending, "cornered defensive windup", 20);
            FixturePlace(floorAt + axis * 2.2f, at);
            yield return new WaitForSecondsRealtime(1.9f);
            Need(!own.GetComponent<TowelCarrier>().Incapacitated, "Retreat during warning still hit player");
            checks.Add("Backing away during defensive warning avoids the stagger.");
            FixturePlace(floorAt + axis * .85f, at);
            Field(hunt, "nextDefense", 0d);
            FixtureAttempt(protectedGuest, MimicPhase.Retreating);
            yield return Wait(() => hunt.State.Phase == MimicPhase.Defending, "second defensive windup", 20);
            double defendedAt = session.Manager.ServerTime.Time;
            Need(!own.GetComponent<TowelCarrier>().Incapacitated, "Instant defensive attack");
            yield return Wait(() => own.GetComponent<TowelCarrier>().Incapacitated || session.Manager.ConnectedClientsList.Any(c => c.PlayerObject != null && c.PlayerObject.GetComponent<TowelCarrier>().Incapacitated), "defensive stagger", 10);
            Need(session.Manager.ServerTime.Time - defendedAt >= 1.4, "Defensive warning too short");
            yield return Command("capture");
            yield return new WaitForSecondsRealtime(1.4f);
            yield return Command("shot");
            yield return Command("shot");
            yield return Wait(() => hunt.State.Phase == MimicPhase.Dead, "pistol eliminates sphere");
            Need(shift.State.MimicsEliminated == 1 && hunt.Remains.Count == 1 && !hunt.DamageOnServer(34), "Duplicate elimination / invulnerability");
            yield return new WaitForSecondsRealtime(2);
            Need(hunt.State.Phase == MimicPhase.Dead && hunt.State.Revision == firstRevision, "Eliminated entity returned");
            checks.Add("Actual client pistol and flashlight together: 34 damage/hit, retreat on hit, warned defensive stagger when two capsules corner it, permanent death after three hits.");
            // Keep everyone outside room 102; delay the next excursion for a stable late-join snapshot.
            yield return Command("pose", new Vector3(27, 0, -1), Vector3.zero, false);
            FixturePlace(testHome, Vector3.zero);
            yield return Crouch(false);
            Field(hunt, "nextActivation", session.Manager.ServerTime.Time + 1);
            yield return Wait(() => hunt.State.Active && hunt.State.Revision > firstRevision, "next queued entity");
            Need(second.Phase == GuestPhase.Revealed && first.Phase == GuestPhase.Revealed && hunt.DebugCarrier == second && hunt.Remains.Count == 1, "Sequential identity transition");
            Mark("late");
            yield return Wait(() => Has("late-checked"), "late observer", 100);
            checks.Add("Only after death and delay: second admitted Mimic becomes the sole active hidden threat. First corpse persists; late join consistent.");
            shift.EndForHuntDebug();
            yield return Wait(() => hunt.State.Phase == MimicPhase.Dormant && !hunt.State.Active && hunt.Remains.Count == 0, "shift reset");
            Need(shift.State.GuestDeaths == 1 && shift.State.MimicsEliminated == 1, "Summary counters changed");
            yield return Command("done");
            yield return UndergroundDisconnect();
        }
    }
}
#endif

