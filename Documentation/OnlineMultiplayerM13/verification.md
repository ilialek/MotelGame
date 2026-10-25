# M13 verification — 2026-10-25

## Result and evidence boundary

Private Sessions + anonymous Authentication + Relay are integrated with the existing NGO listen server. Actual service calls and encrypted Relay connections succeeded between independent macOS standalone processes. This is not a test between two countries or two physical networks.

The stable Mac application and Windows x64 executable both built with zero errors. Windows execution is unavailable on this Mac. The complete 900-second motel night and restart passed on the initial M13 runtime; the final Mac runtime additionally passed four-player lifecycle, profiles and LAN regression checks. Later fixes concern Authentication profile isolation, UI/error handling and bounded connection cleanup; no authoritative motel gameplay code changed.

## Executed checks

| Check | Evidence | Result |
|---|---|---|
| Native Unity compilation, NGO IL postprocessing, existing M7–M12 scene/reference/navigation/visual validation | `build-validation.txt`, `setup-validation.txt`, Editor log | PASS; final Editor-only polling guard also compiled after both builds |
| Actual 15-minute shift and second 3-minute shift, same Relay connections | `night-20261025-174810/host.txt`, `client.txt` | PASS, both graphical standalone processes, no critical runtime logs |
| Invalid syntax/unknown code, retry, cancel creation, recreate | `tls-retry/invalid.txt` | PASS |
| Four separate authenticated players, private capacity, host-only Start Game, movement/ownership/cameras | `lifecycle-20261025-182306/` | PASS on final stable Mac build |
| Fifth member rejected with readable full message; incompatible protocol rejected by NGO host | Same lifecycle folder, `fifth.txt`, `incompatible.txt` | PASS |
| Same-identity rejoin, member count returns from 3 to 4, normal host departure, another lobby without restarting | Same lifecycle folder | PASS |
| Abrupt host process termination | Same folder, `host-before-crash.txt`, three client reports | PASS; runner deliberately killed only its own host; clients returned to menu |
| Explicit duplicate profile blocked; two normal launches obtain separate cached profiles/player IDs | Lifecycle `duplicate.txt`; `profiles-20261025-182731/` | PASS |
| Late join during carried bag, electrical fault and relocation | `focused-20261025-181022/observer-*.txt` | All three PASS; synchronized state/visuals and client-side NPC agents disabled |
| Focused scenario through both nights | Same focused folder, host/client reports | FAILED in second shift; details below. This run is not counted as a full pass |
| LAN doors, collisions, distant RPC rejection, repeated toggles and late join | `door-regression-20261025-182328/` | PASS, host/client/late |
| LAN owner movement, cameras/audio, remote model, Idle/Walk/Run and animated leg | `locomotion-20261025-182731/` | PASS, host/client |
| Concurrent operations, closing cleanup and computer-owner disconnect | `edges-20261025-182806/host.txt`, `client.txt` | PASS on final stable build; 20 competing requests per operation, computer release/rejoin and stale-action rejection |
| macOS standalone | `build-validation.txt` | Succeeded, 0 errors, 2 warnings; executable universal x86_64/arm64; executed on this Apple Silicon Mac |
| Windows standalone | `windows-build-validation.txt` | Succeeded, 0 errors, 19 warnings; actual PE32+ x86-64 executable and data verified; runtime unverified |
| Archive integrity | `Builds/Playtest/Distributions/SHA256SUMS.txt` | ZIP CRC checks passed; no project/Library/backup/DoNotShip folders packaged |

The complete first night recorded 8 check-ins, 8 checkouts, 5 towel deliveries, 1 television repair, 13 disposed bags, 1/1 electrical repairs and 1 relocation. The second shift recorded 2 check-ins/checkouts and 3 bags. Both peers checked reset state and identical end-of-shift summaries.

The lifecycle runner's original summary mentions invalid/cancel tests; those were actually run separately in `tls-retry`. Individual role reports are the authoritative evidence for each lifecycle check.

## Failed attempts and remaining limitations

1. Some fresh Unity Authentication attempts failed with TLS certificate/handshake/connection timeouts. Subsequent sequential attempts passed, including four independent clients and two default-profile launches. No certificate validation, DNS, firewall or OS trust setting was changed. The underlying cause is unproven and remains a remote-playtest risk; Retry Connection remains available after failure.
2. The focused run completed its first shift, bag-disconnect recovery and all three late-join observations, then timed out waiting for Room 101's door to become open during the second shift. Client position was approximately `(19.05, 0.03, 12.65)`. The host subsequently failed the minimum second-night service-count assertion. The test sends a one-shot toggle followed by a wait; the existing door can reject/close actions when a guest reserves its passage or the sweep is occupied. The log does not establish which occurred, so this is an unresolved test failure, not a confirmed networking or geometry diagnosis. Manually repeat room preparation and exit during a second shift, especially while a guest uses the door. Door/gameplay/geometry code was left unchanged. The separate full-night + restart run and LAN door regression passed.
3. No second physical machine, foreign ISP, high-latency/loss simulation or intentional local internet outage was tested. Windows rendering, input, TLS and cross-platform gameplay require a Windows runtime. Intel Mac execution was not available even though the bundle contains both architectures.
4. macOS has a verified ad-hoc signature only: no Developer ID and no notarization. Downloaded copies may need the trusted-app Gatekeeper flow documented in README.
5. Build warnings include Unity Services' Editor project-link warning (its check requires both project linkage and Editor account login) and Windows ray-tracing shader/platform warnings. The existing public project ID is present and real Authentication/Lobby/Relay requests passed; no claim is made that Windows rendering was tested. No warning was hidden or treated as a successful runtime test.
6. Host migration, saved nights, voice chat and preservation of the disconnected character's position/items are not implemented. Rejoin spawns a fresh owned character using existing gameplay cleanup/state synchronization.

## Defects corrected during M13 verification

- Sessions 2.4.1 drops LobbyFull's typed reason; known service messages now translate to a readable capacity error. Final fifth-client check passed.
- Mono macOS did not enforce `FileShare.None` between processes. Explicit OS byte-range locking now isolates cached Authentication profiles. Both duplicate refusal and normal automatic separation passed.
- An already-running Editor initially reported a misleading Windows `Succeeded` with 2 errors and no executable after module installation. Success checks now also require zero errors and a real executable. After the user's Editor restart, Windows cross-build passed.
- Unity save/build serialization filled 32 existing empty TextMesh material references. These unrelated changes were restored. Scene/build helpers preserve original renderer and scene/settings bytes. Final audit shows only intended online component/timeout changes.
- Small-window menu footer clipping was corrected; final four-player 800×600 lobby screenshot was inspected (`lifecycle-20261025-182306/host-lobby.png`).

## Scope audit and rollback

Backup: `Backups/BeforeOnlineMultiplayerM13_20261025_173533`.

All 2,380 original Assets/Packages/ProjectSettings files were compared with the pre-M13 SHA-256 inventory. `changed-files.txt` and `scope-audit.json` contain the exact list. No original files were removed. Prefabs, art, materials, lighting, collider/transform geometry, player movement and authoritative M4–M12 gameplay scripts are unchanged. ProjectSettings and render settings match the baseline. The scene changes only NetworkSession's component list and NetworkManager's approval timeout, adding two components. No Git commit was created.

New SDK configuration: Multiplayer Services 2.4.1; Authentication 3.8.0; Unity Transport 2.7.4 (from 2.6.0); NGO 2.13.3 retained. Existing UGS linkage/default production environment reused. No account-owner activation or paid subscription was needed/enabled. Actual account quota consumption/billing was not visible; README records the checked public allowance separately.

## Distributions

- `Builds/Playtest/Distributions/MotelGame-M13-macOS.zip` — 120,388,375 bytes.
  SHA-256: `0a45fdaa7cfe53e749f2c4dd80b14ff7f8015cc2ad0639a244db87ab657a59e0`
- `Builds/Playtest/Distributions/MotelGame-M13-Windows-x64.zip` — 78,152,134 bytes.
  SHA-256: `d66bb4e9da26aeb9d3a3aeb4fe9bb5bc6e8b88a6bf1565aa0c404c2dafd66e6f`

The original stable Mac launcher remains `Builds/Playtest/LaunchWindowed.command`. Both archives contain four numbered local-profile launchers and friend instructions; they do not contain source/backup/test-result directories.

## External acceptance procedure

1. Send the archive appropriate for the friend's OS. Both sides use protocol `motel-m13-1` from this distribution and extract all files.
2. Launch the game, wait for Online ready. Host selects Create Lobby and sends Copy Code. Friend pastes into Invite code and selects Join Lobby. No Unity player account, IP address, router forwarding or VPN should be required.
3. Verify both participants. Host selects Start Game. Confirm independent movement/camera and a replicated door interaction.
4. Use E at the reception computer and select Start Shift. Compare shift clock and room status on both machines.
5. Prepare/check in a room; complete a request, disposal, electrical repair/relocation and checkout. Finish the night and start another without reconnecting. Repeat the Room 101 door exit mentioned above.
6. Client leaves/rejoins during the shift. Verify current state and that a computer occupied by a disconnected client is reusable. End the host session and confirm client returns to menu.
7. Retain both Player.log files and report platform, approximate time and visible error if anything fails. Windows runtime and cross-country latency can only be accepted after this external test.
