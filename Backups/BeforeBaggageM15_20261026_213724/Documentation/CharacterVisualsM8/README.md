# M8 — Character Models & Animations

Unity 6000.3.19f1 / URP / NGO 2.13.3. Visual upgrade over the confirmed M4–M7 gameplay. No packages added. Backup: `Backups/BeforeCharacterVisualsM8_20261024_214101/`.

## Assets and license

Three selected characters from **Quaternius Animated Men Pack**, [official pack](https://quaternius.com/packs/animatedmen.html), [author's Poly Pizza catalogue](https://poly.pizza/bundle/Animated-Men-Pack-DAC9SDgMQT). CC0 1.0; source URLs, terms and download provenance are in `Assets/Game/Art/Characters/Quaternius/LICENSE.txt`.

The official Google Drive download was quota-limited. Actual GLB assets were obtained from the author's public Poly Pizza CDN and converted with installed Blender 5.2.3 to Unity-native FBX. No GLTF package or custom importer was added. The files retain only the selected Idle, Walk, Run clips. Approximately 1,849 / 1,967 / 2,055 triangles per model, three FBX files total about 3 MB. Material colors are preserved in new matte URP/Lit materials; no textures or custom shaders are required. These are prototype models, not final horror art.

## Rig and locomotion

**Generic rig**, valid Unity Avatars. The source has independently animated feet outside the lower-leg hierarchy; it is not a standard Humanoid skeleton. Using its baked native animation avoids forced bone mapping and retargeting distortions. A future Humanoid art replacement will need a compatible Avatar/controller setup; there is no custom retargeting system to maintain.

All three models share bone paths and the `Locomotion.controller` with clips from `Casual.fbx`. Model scale and floor offset are normalized from the posed skin, with FBX scale compensation: 1.8 m tall in Idle, feet at root floor. The model child remains fixed under its functional root. `Animator.applyRootMotion = false`, clips loop, no `NetworkAnimator`.

One controller: Idle ↔ Walk ↔ Run. `Speed` is meters per second; Idle/Walk boundary 0.1, Run entry 4.8 and exit 4.5, fixed 0.12 s transitions. WalkRate and RunRate scale playback to actual speed. The foot trajectory sampled in Unity gives approximate nominal speeds 2.1 m/s (Walk) and 4.5 m/s (Run). Thus a 4 m/s player uses about 1.90× walking playback; sprint 6 m/s uses 1.33× running; a 1.6 m/s guest uses 0.76× walking. No controller movement values were changed.

`CharacterVisuals` measures horizontal root displacement locally, rejects spawn/arrival teleports, and smooths speed over 0.08 s. Owners and observers use their existing replicated positions; there is no additional animation NetworkVariable or RPC. An NPC stays Idle in WaitingForCheckIn/InRoom and walks while it physically moves along its existing route. The same measurement also stops walking animation when blocked. There is no seated-state gameplay addition.

## Prefabs and visibility

`NetworkPlayer` functional root retains NetworkObject, owner-authoritative NetworkTransform, CharacterController, FirstPersonController, PlayerInteractor and TowelCarrier. Camera remains at 1.65 m; walk/sprint remain 4/6 m/s.

- `CharacterVisual/Casual`: Animator, bones and SkinnedMeshRenderer.
- `RemoteBody`: old capsule renderer disabled; the functional CharacterController is untouched.
- `NetworkPlayer.ApplyOwnership` controls local camera/input as before and disables only its local character skin renderers. Other processes still render the whole character.
- `CarriedTowel` is attached to `Palm.R`; its world dimensions are approximately 0.40 × 0.10 × 0.26 m. TowelCarrier still determines third-person/first-person visibility from server-authoritative carry state. FirstPersonTowel and its camera-relative placement are unchanged.

`NetworkGuest` retains its server-controlled NavMeshAgent, CapsuleCollider, NetworkObject/NetworkTransform and GuestLifecycle. `CharacterVisual` contains three variants; exactly one is active. A/D use Casual, B/E LongSleeve, C Suit. Configured guest shirt colors affect only the Shirt material slot; skin/hair are not recolored. The name label is preserved. `HumanoidPlaceholder` is inactive; none of its visual primitives supply gameplay collision.

Only the renderers of static `EmployeeComputer_1.8m`, `EmployeeSecond_1.8m`, `CorridorPlayer_1.8m` markers are hidden. Other inactive scale markers remain inactive. No room geometry, furniture, lighting, parking, NavMesh, existing materials, input settings, Packages or ProjectSettings were changed.

## Working in Unity

Use the configured prefabs and scene directly. No manual rig, material or reference setup remains. `Tools → Motel → Validate Character Visuals M8` checks assets/references, scales/floor alignment and sampled animation movement. `Tools → Motel → Build Current Playtest` performs M7 and M8 validation and writes the permanent `Builds/Playtest/MotelGame.app`.

`CharacterVisualsSetup` is an Editor-only installation/validation utility; installation is explicit and refuses an already-installed prefab. Normal domain reloads do not reconstruct characters or scenes. Never run historical M1/M6 install scripts to rebuild the current player/guest prefab; use the existing integrated assets.

## Manual Host/Client check

1. Launch `Builds/Playtest/LaunchWindowed.command` twice. Place the two 800×600 windows side by side. Alternatively use Unity Play Mode as Host and one standalone window as Client.
2. First window: **Start Host**. Second: **Start Client**, `127.0.0.1`, UDP port `7777`. Both instances must be the current build/version.
3. Click outside the connection panel. WASD + mouse, Left Shift sprint; Esc releases the cursor. Take turns watching the other player walk, run, stop and rotate. The local screen must have no head/torso/capsule obstruction.
4. Wait for a guest at reception. Prepare rooms 101–104 with E on the beds and trash; E at Reception to assign Ready rooms. Watch at least three distinct guests walk to rooms and wait inside.
5. Answer the phone with E, take a towel from Utility, inspect it in both windows, deliver it to the correct room's tray. No interaction bindings changed.
6. Reception → TEST: Check out the selected room. Watch the guest leave to parking. Clean and service the room again; repeat with another guest.
7. Open another client after rooms are occupied and a towel is being carried. Confirm the active guest variants, animations and item. Disconnect it; verify no orphan character remains.

## Verification and limits

Exact automated results, build timestamps and changed-file manifest are recorded in `verification.md` and the generated reports. Headless runs verify networking, ownership, Animator states and changing bone poses, but cannot establish subjective animation quality. Unity-rendered preview images check actual meshes, poses, floor scale, URP materials and the towel in the motel. They are staged visual inspection frames, not screenshots of a live multiplayer playthrough.

Remaining manual checks: movement/transition appearance in live graphical Host/Client, sideways/backward sliding, turns in doorways, hand/towel overlap throughout the animation, subjective FPS responsiveness, and LAN between separate machines. This prototype uses forward locomotion clips for strafing/backward motion, no directional blend set, foot IK, upper-body carry pose, facial animation or seated transitions. The local skin is hidden; first-person arms/body awareness remain outside scope.

## Files

Runtime: new `Assets/Game/Scripts/Characters/CharacterVisuals.cs`; targeted ownership integration in `Networking/NetworkPlayer.cs`; material-slot tint in `Service/GuestLifecycle.cs`. FirstPersonController, TowelCarrier and the rest of M4–M7 gameplay are unchanged.

Assets: `NetworkPlayer.prefab`, `NetworkGuest.prefab`, `Motel_Blockout_V3.unity`, new `Assets/Game/Art/Characters/Quaternius/` models/materials/controller/license and their `.meta` files.

Editor/tests: `CharacterVisualsSetup.cs`, `PlaytestBuild.cs`, updated `LocalMultiplayerSmokeTest.cs`, new `CharacterLocomotionTest.cs`, `CharacterVisualsProbe.cs`; `Tests/run_character_test.py` and `Tests/run_regression.py` here. Development tests remain inactive unless explicitly requested with command-line flags.
