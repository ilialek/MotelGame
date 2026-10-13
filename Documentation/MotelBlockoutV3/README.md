# Motel Blockout V3 — guest-wing redesign

Current scene: `Assets/Game/Scenes/Motel_Blockout_V3.unity`.
Backup before this change: `Backups/BeforeGuestWingRedesign_2026-10-13/` (outside Assets, so the original scene GUID is not imported twice).

## Layout

Front is -Z. From the public lobby, enter the east opening at X=14, Z=7..9.8, walk east (+X), then turn LEFT toward +Z.

| Space | Clear X | Clear Z | Height |
|---|---|---|---|
| Lobby | 0..14 | 0..10 | 3.2 m |
| Straight corridor | 14.2..23.6 | 7..9.8 | 2.8 m |
| Guest corridor | 20.8..23.6 | 9.8..21.2 | 2.8 m |
| Room 101 | 15.6..20.6 | 10..15.5 | 2.8 m |
| Room 102 | 23.8..28.8 | 10..15.5 | 2.8 m |
| Room 103 | 15.6..20.6 | 15.7..21.2 | 2.8 m |
| Room 104 | 23.8..28.8 | 15.7..21.2 | 2.8 m |
| Utility | 14.2..19.2 | 1.8..6.8 | 2.8 m |

Wall thickness: 0.2 m. Corridor width: 2.8 m. Entrance: 3 m. Lobby-to-corridor opening: 2.8 m. Guest door openings retain 1.1 x 2.2 m. Utility door: 1.2 x 2.2 m, Z=4..5.2. Utility is aligned against the corridor south wall, avoiding a narrow exterior slot; its 5 x 5 m clear dimensions remain unchanged.

The straight leg is 9.4 m long measured to its end wall; the centreline turning point is 8 m from the lobby threshold. Full routes from employee positions to just inside room doors are approximately 16–20.3 m for 101/102 and 22.8–27.1 m for 103/104 using a conservative 0.1 m orthogonal navigation grid. These are geometric routes, not measured controller travel times.

## Changes and preservation

- Existing Room101–104 and MainRun primitives translated by (+15.6, 0, -3.2), retaining object IDs, component data, furniture, signs, doors and window materials.
- Utility moved by (0, 0, -3.2); door leaf repositioned to match the revised staff opening and avoid shelves.
- Original EntryLeg structure replaced. ReceptionSightlineReturn removed.
- Lobby rear opening closed. East wall receives separate staff and public openings. Lobby front/left glazing, floor, ceiling, furniture, desk and equipment unchanged.
- Existing room/corridor/utility light transforms follow their spaces. Light component settings, lobby lights, exterior lights and scene lighting settings unchanged.
- Parking unchanged. Ground extended east from X=24 to X=30 beneath the relocated wing.
- Player-scale capsules and eye-height markers for the guest wing moved. No gameplay systems added.
- 723 original primitive objects reused; scene now contains 738 primitives.

## Verification

`guest-wing-geometry-validation.json` records checks against parsed saved-scene geometry, not just intended coordinates: structural overlaps, matching spec, floor height, parent/child references, navigable routes, two-player corridor clearance and complete door-width occlusion from both employee positions at Y=1.65, including +/-0.2 m movement.

`guest-wing-patch-manifest.json` identifies changed objects. `guest-wing-preservation.json` records project file hashes and allowed changes. `guest-wing-floor-plan.png` is a schematic generated from geometry, not a Unity render.

The Editor command **Tools > Motel Blockout V3 > Validate Saved Scene and Render Previews** loads a separate preview scene directly from disk, validates it and renders six views. It does not save, reload or alter the user's open scenes. Fresh runtime evidence is recorded in `guest-wing-unity-validation.txt` with UTC timestamp and scene SHA256; failures are recorded in `guest-wing-unity-error.txt`.

Earlier `geometry-validation.json`, `sightline-validation.json`, `unity-validation.txt` and older preview images describe superseded layouts. Use the `guest-wing-*` files for this revision.

Manual checks: comfort with the actual first-person controller/camera and two players, doorway interactions, visual lighting at the bend, and overall perceived walking distances. No controller or multiplayer simulation is implemented by this task.

## Completed Unity verification

The saved scene was loaded from disk in an isolated preview scene in Unity 6000.3.19f1 on 2026-10-13. The report SHA256 matches the delivered scene. All 738 primitives and 22,200 doorway rays passed. Six images were rendered; the top-down plan, corridor and both reception-to-corridor views were visually inspected. No structural problem was found in those views. Corridor lighting remains dim; evaluate it with the actual player camera without treating the top-down inspection render as gameplay exposure. The open user scene was not saved or reloaded by the validator.
