# M9 verification — 2026-10-24

## Actual execution

- Unity6000.3.19f1 compiled runtime and Editor assemblies, including NGO IL post-processing. Latest Development build: **Succeeded,0 errors,0 warnings** (`build-validation.txt`). Output remains `Builds/Playtest/MotelGame.app`.
- Saved Unity scene reopened by native Editor validation. Four room configurations, five server-controlled guests, shared Reception point, service reference, default120-second scene/prefab durations, no missing scripts. M9 validates16 return/departure path segments; existing M7 validates25 normal route segments and all four door sweeps/player passages. M8 character asset/reference/animation validation also passes.
- **Full Host/Client end-to-end: PASS**, `run-20261024-225413/`. Five executed processes: Host,Client, and three successive late observers. Three complete Room102 cleaning/check-in/stay/service/checkout cycles. The last stay used120 seconds. Other test durations were set as Inspector-equivalent values before registration; no NPC phase, transform, room snapshot or deadline was injected.
- Full test covered: Client checkout; independent101/102/103/104 occupancy; invalid target/distance/visit/cycle; concurrent duplicate requests; arrival vs two expired stays; oldest-expiry order; unfinished103 cancellation and two carried towels; stale service requests; late Returning/Waiting/Leaving snapshots; retained Completed result during return; Client disconnect at WaitingForCheckout followed by Host checkout and reconnect. Host observed448.9m of NPC walking, with no detected route teleport, solid/NPC penetration, duplicate assignment or duplicate Reception slot. All5 reports PASS; no critical runtime logs. Ten navigation-obstruction warnings occurred on Host; all monitored routes subsequently completed.
- **Final-build targeted regression: PASS**, `run-20261024-230737/` (Host,Client,three late observers; all5reports PASS;254.7m of NPC travel). After the full run, the only production-code change was refreshing the guest's label in NGO `OnNetworkSessionSynchronized`, so room references are guaranteed available to late joiners. The final build passed the initial two checkout flows, concurrent checkout, cancellation and three late joins, including exact checkout text. The test runner's optional `--initial-only` flag skips only subsequent turnover cycles; default execution still runs the full test.
- **Existing M8 two-instance locomotion/ownership: PASS**, `../CharacterVisualsM8/locomotion-20261024-224642/`. Walk4m/s,run6m/s,owner-only controls/camera/listener,remote movement/yaw,Idle/Walk/Run and actual animated bone changes,disconnect cleanup.
- **Existing network-door regression: PASS**, `../MultipleGuestsM7/door-regression-20261024-224644/`. Host/Client/late client; E raycast,server reach validation,20 repeated RPCs,closed/open collision,late door pose,state preserved across disconnect.
- Permanent launcher was executed after the last rebuild: process started successfully with Metal graphics initialization, then only this validation instance was closed. Args remain windowed800×600. See `launcher-validation.txt` and `launcher-startup.log`. GUI automation failed to start, so the window was not visually inspected.
- Earlier diagnostic `run-20261024-224624/` was intentionally interrupted after the initial checkouts and3successful late joins to correct test expectations about the existing global request scheduler. It is **not** counted as a complete passed run.

Full-test assembly SHA256 before the final label-only correction:
`ec054427e52e92dcc863ba2b76987dd32b035235fa3160727c0b40b5562d77c5`.
Final build/scene/prefab hashes are recorded in `build-hashes.json`.

## Change audit

`change-audit.json` compares existing Assets,Packages,ProjectSettings to the pre-M9 backup by SHA256.304existing files are unchanged. The scene differs by exactly the new `ReceptionDesk.serviceRequests` reference; NetworkGuest prefab differs by exactly `stayDuration:120`.

Modified existing source/assets:

- `Assets/Game/Scripts/Service/GuestLifecycle.cs`
- `Assets/Game/Scripts/Service/GuestServiceRequests.cs`
- `Assets/Game/Scripts/Service/ReceptionDesk.cs`
- `Assets/Game/Scripts/Service/ReceptionInteraction.cs`
- `Assets/Game/Scripts/Service/ServiceRoom.cs`
- `Assets/Game/Editor/PlaytestBuild.cs`
- `Assets/Game/Prefabs/NetworkGuest.prefab`
- `Assets/Game/Scenes/Motel_Blockout_V3.unity`

Added: `Assets/Game/Editor/GuestCheckoutSetup.cs`, `Assets/Game/Scripts/Networking/Tests/GuestCheckoutSmokeTest.cs` and their Unity meta files; `Documentation/GuestCheckoutM9/README.md`, this report, runner `Tests/run_checkout_test.py`, audit and execution reports. Existing M7 generated setup-validation report now records the new scene hash; M8/door execution reports were added alongside their existing tests.

No packages installed. FirstPersonController,NetworkPlayer,TowelCarrier,NetworkDoor,all character/environment assets,geometry,furniture,lighting,parking,NavMesh,Packages and ProjectSettings are unchanged. No Git commit created.

Backup: `Backups/BeforeGuestCheckoutM9_20261024_224110/` (Assets,Packages,ProjectSettings). No backups or unique files removed. Older build directories contain only harmless.DS_Store leftovers; the only standalone app remains the current Playtest.

## Limits / manual verification

Headless tests check network state,actual navigation,collisions,interaction targeting,animation state and bone movement. They do not prove rendered appearance,animation smoothness,text readability,mouse feel or audio. Check these in two graphical windows using README instructions. Separate-machine LAN remains untested.

A guest can wait for a physical obstruction to clear. An arriving guest with a vacant Dirty room waits until players clean/assign it; expired stays defer until that service slot is freed. Existing one-request-at-a-time M5 scheduling is preserved. Guests use the existing five-object scene pool: after physically reaching ParkingDeparture they become Absent/hidden and are reused; no additional NGO Despawn/Respawn layer was introduced.

Historical M4–M7 smoke files that rely on forced checkout while WalkingToRoom/InRoom were retained but not presented as passing M9 regressions: that contract intentionally no longer exists. Their working cleaning/service/navigation behavior is exercised by the new full end-to-end test; the existing door and M8 locomotion harnesses were actually run unchanged.
