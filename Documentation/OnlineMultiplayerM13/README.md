# M13 — Online Multiplayer

Motel Game / Unity 6000.3.19f1 / NGO listen server. M4–M12 gameplay and the motel environment are retained.

## Play with a friend

1. Both players extract the **same M13 version**. macOS: launch `MotelGame.app` or `LaunchWindowed.command`. Windows: launch `MotelGame.exe` (keep its data folder and DLLs next to it).
2. Wait for **Online ready**. Authentication is anonymous and automatic; no Unity account is required for players.
3. Host: **Create Lobby → Copy Code**. Send that code privately to the friend.
4. Friend: paste it into **Invite code → Join Lobby**. Both names should appear in the player list.
5. Host: **Start Game**. Each player gets their own character and camera in the lobby.
6. At the reception computer, press **E**, then **Start Shift**. This starts the 15-minute night; creating the online lobby does not start it.
7. WASD, mouse, Left Shift. **Esc** opens the connection menu; **Resume Game** returns to play. **Leave Lobby** disconnects a client; **End Session** ends the host's game for everyone.
8. When the night ends, start another shift at the computer without creating another lobby.

No router configuration, IP addresses, VPN or Unity Editor are needed. Voice chat is outside this milestone.

## Services and packages

The existing Unity project linkage is reused. No secret credentials or service-account keys are in the game sources. No paid plan was enabled by this implementation.

- Multiplayer Services SDK **2.4.1**: unified Sessions, Lobby and Relay integration.
- Authentication **3.8.0**, resolved by MPS.
- Unity Transport **2.7.4**, required by this MPS release (upgraded from 2.6.0).
- Netcode for GameObjects **2.13.3**, unchanged.
- Unity Services environment: the project's existing default **production** environment. This prototype has no separate deployed backend to isolate; no additional environment or paid service was created.

Private sessions are excluded from public queries and have capacity **4 including the host**. The displayed code is the real Sessions code returned by Unity; it is not a locally generated code or an IP address. Relay uses encrypted **DTLS** and automatic region selection. Matchmaker, Distributed Authority, Unity Relay manual API plumbing, Steam and host migration are not used.

Current public Unity pricing (checked 2026-10-25): Authentication is free; Relay includes 50 average monthly CCU and 3 GiB per CCU up to 150 GiB/month; Lobby includes 10 GiB/month per regional group. Account usage and billing settings are only visible to the account owner in Unity Dashboard; the public quota is not a measurement of this project's remaining allowance. Do not enable pay-as-you-go merely to run this test.

Sources: [Sessions](https://docs.unity.com/en-us/mps-sdk/sessions), [network startup](https://docs.unity.com/en-us/mps-sdk/session-networking), [anonymous sign-in](https://docs.unity.com/en-us/authentication/use-anon-sign-in), [profiles](https://docs.unity.com/en-us/authentication/profile-management), [pricing](https://unity.com/products/gaming-services/pricing).

## Implementation

- `OnlineIdentity`: cached anonymous sign-in and local process/profile isolation. Credentials remain in Unity Authentication's normal local storage.
- `OnlineSessionController`: private session create/join/leave, Relay lifecycle, version approval, cancellation, error translation, lobby-to-game gate and host departure.
- `OnlineSessionMenu`: existing IMGUI UI convention, dark menu, roster/code/copy/start/leave; no new UI framework.
- `DevelopmentSession`: retains LAN debugging and spawn slots. Online approval reserves a slot but delays `PlayerObject` creation until the host starts gameplay. It remains the sole NGO approval callback.
- `OnlineMultiplayerSetup`: explicit Editor setup/validation requests and menu commands; no scene mutation merely because the Editor reloads scripts.

There is one existing `NetworkManager` in `Motel_Blockout_V3`. Sessions' `WithRelayNetwork()` starts it automatically. Gameplay never calls a second `StartHost`/`StartClient` for online play, and Start Game neither reloads the motel nor reconnects players. Room/NPC/shift objects initialize through their existing `OnNetworkSpawn` methods. A lobby participant has no character and cannot pass existing gameplay distance/ownership checks before Start Game.

Player movement remains owner-authoritative; motel operations remain host/server-authoritative. Existing room, door, carry, NPC, computer, night and restart RPCs/NetworkVariables are reused unchanged.

Build compatibility string: `motel-m13-1`. Increment it whenever protocol, network prefab or scene compatibility changes. Host approval rejects an incompatible handshake with a readable reason; Sessions also carries the build property. This is compatibility checking, not an anti-cheat/security boundary.

## Late join and reconnect

Joining a started game is permitted while a slot is free. NGO spawns a fresh player at a free lobby position and sends the existing room, NPC, request and shift state. Rejoining uses the same cached Authentication identity but does **not** promise the same character, previous position or held item. Existing server disconnect cleanup handles carried items and computer occupancy. Stale lobby members are removed after NGO reports their disconnection.

There is no host migration. A graceful host departure deletes the session; an abrupt loss is detected by Relay/NGO timeouts, then clients leave and return to the menu. This can take tens of seconds. If internet is unavailable, remote cleanup is best effort and backend expiry may finish it later.

Create/join buttons are guarded against repeated requests. Cancellation and 45-second connection deadlines use SDK cancellation. Sign-in deadlines also leave the UI recoverable. Session cleanup has an 8-second service deadline and always shuts down local NGO. Error logs contain step/type/error code rather than authentication tokens or HTTP response bodies.

## Two to four windows on one Mac

The regular launcher remains `Builds/Playtest/LaunchWindowed.command` and starts an 800×600 window. Normal startup acquires a local profile lease: first process `player`, additional processes `local-2`, `local-3`, etc. The lease is released by the OS after a crash. Cached credentials are preserved.

For repeatable tests use `LocalTestPlayer1.command` through `LocalTestPlayer4.command` (Windows: matching `.bat` files), which select persistent `playtest1`–`playtest4` profiles. Do not launch two windows with the same explicit profile; the second is refused rather than sharing the same Unity player identity. Editor Play Mode uses its own `editor` profile.

CLI equivalent: `-motel-profile playtest2 -screen-fullscreen 0 -screen-width 800 -screen-height 600`.

**Local / LAN Debug** retains direct-IP tests and does not use Relay. It is a separate optional menu. A successful LAN test does not establish that online services work.

## Builds and verification

Stable Mac path: `Builds/Playtest/MotelGame.app`.
Windows output: `Builds/Playtest/Windows/MotelGame.exe` (successfully cross-built on this Mac; Windows runtime unverified).
Build menus: **Tools → Motel → Build Current Playtest** and **Tools → Motel → Online → Build Windows Playtest**.

Share `Builds/Playtest/Distributions/MotelGame-M13-macOS.zip` or `MotelGame-M13-Windows-x64.zip`. Each contains the application, launchers and `READ-ME-FIRST.txt`. Keep the whole extracted platform folder together. The archives contain no Unity project, Library, Backups or test-result directories. Archive checksums and executed checks are recorded beside the archives and in `verification.md`.

The macOS bundle is universal (Intel x86_64 + Apple Silicon arm64) and has Unity’s ad-hoc signature, with no Developer ID or notarization. Gatekeeper may block a downloaded copy: inspect the source, then use the macOS **Privacy & Security → Open Anyway** flow where available. Do not disable Gatekeeper globally. [Apple instructions](https://support.apple.com/en-us/102445). Windows runtime testing requires a Windows machine even when cross-compilation succeeds on this Mac.

## Troubleshooting a remote playtest

- Both sides must use the same build. Send a fresh session code after the host recreates a lobby.
- Check the game error message and internet access; retry after a few seconds. Full means four players including the host.
- Some initial local Authentication attempts encountered Unity TLS/connection timeouts. Later sequential sign-ins and long Relay sessions succeeded, but the underlying network/TLS cause is not established. Certificate validation was not bypassed. If Retry Connection also fails, retain the log and check the same build on the other network.
- If the host disappears, wait for the disconnect timeout and create a new session. There is no recovery of an abandoned host's night.
- For service failure, the project owner checks **Unity Dashboard → MotelGame → Multiplayer → Lobby / Relay**, the selected environment and usage. Players need no Dashboard account.
- Logs: macOS `~/Library/Logs/DefaultCompany/MotelGame/Player.log`; Windows `%USERPROFILE%/AppData/LocalLow/DefaultCompany/MotelGame/Player.log`; automated tests specify their own log files.
- During an external call confirm both physical machines see movement, an opened door, the same shift clock and the same room status. Test one service request and reconnect once before committing to the whole night.

Two processes on one Mac using a real remote Unity Relay allocation verify the service path but do not prove cross-country latency, another ISP's firewall behavior or a Windows runtime. Those limits are reported separately.

## Repeat the automated checks

Development builds only activate test actors when explicit `-motel-…-test` arguments are present. Normal play does not create them. Python launchers are in `Documentation/OnlineMultiplayerM13/Tests`; they find the enclosing Unity project and run its stable Mac build. Each uses a fresh result folder and inspects written PASS/FAIL reports in addition to process exit status.

- `run_online_lifecycle.py`: four identities, fifth/full, duplicate profile, incompatible version, ownership/movement, rejoin, graceful host leave, recreation and deliberate termination of its own host process.
- `run_profiles.py`: two ordinary launches automatically obtain different cached identities.
- `run_operations_online.py --night --graphics`: actual 900-second first shift and shortened second shift over Relay.
- `run_operations_online.py --edges`: competing gameplay requests, closing reset and disconnected computer occupancy.
- `run_operations_online.py --graphics`: focused late joins, carried item, electrical fault and relocation; see the recorded second-shift door timeout in verification.
- `run_door_regression.py` and `run_character_test.py`: direct LAN door/collision and player/camera/animation regressions.

Do not run gameplay suites concurrently on the same test port/profile. Reports and logs are development diagnostics; review them before sharing publicly.
