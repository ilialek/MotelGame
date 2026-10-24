# First Person Controller — minimal exploration milestone

Scene: `Assets/Game/Scenes/Motel_Blockout_V3.unity`
Player: `Motel/PlayerSpawnPoints/Player`
Runtime script: `Assets/Game/Scripts/FirstPersonController.cs`

## Test in Unity

1. Wait for script compilation. Open/reopen `Motel_Blockout_V3` from Assets/Game/Scenes to load the updated scene from disk. If Unity reports an externally changed scene, reload it; avoid saving a stale in-memory version over the updated file. Preserve any separate unsaved work first.
2. Press Play and click the Game view.
3. WASD moves; mouse looks; Left Shift sprints when Allow Sprint is enabled.
4. Escape releases the cursor and stops horizontal movement/look. Gravity continues. Click the Game view to recapture. Losing application focus also releases the cursor.
5. Walk through the lobby, pass behind the north end of reception, enter the right-hand corridor, turn left and enter all four rooms. Check bathrooms too. Walk/run against walls and furniture, including diagonally; inspect corners while looking up/down.
6. Confirm Console has no new errors. Stop Play to return to the saved spawn.

## Inspector settings

Select Player, then First Person Controller: Walking Speed 4 m/s, Allow Sprint, Sprint Speed 6 m/s, Eye Height 1.65 m, Mouse Sensitivity 0.1 degrees per pixel, Pitch Limit 85 degrees, Gravity 20 m/s². Values apply to a local player only. Eye position is configured in Awake and clamped inside the capsule.

CharacterController: height 1.8 m, radius 0.3 m, center Y=0.9 m, skin width 0.03 m, step offset 0.2 m, slope limit 45°, minimum move distance 0. Camera near plane is 0.03 m. Spawn feet: (7, 0.05, 1.8); gravity settles the initial 5 cm clearance onto the lobby floor.

The controller reads the installed Input System keyboard/mouse devices directly. It does not modify the existing input-actions asset or project settings. Mouse delta is not multiplied by deltaTime; movement/gravity are. Diagonal movement is normalized. No Rigidbody, jump, crouch, stamina, sound, head bob, networking or runtime geometry is added.

For future multiplayer, enable local input only on the owning player and disable remote cameras/AudioListeners. This component owns no global player singleton and uses an explicit camera reference. No networking abstraction is introduced.

## Preservation and validation

The scene patch adds Player, CharacterController, FirstPersonController and AudioListener, reparents the existing inspection camera as PlayerCamera, and shortens its near plane. Every environment object/component remains unchanged, including geometry, furniture, materials and lighting. SampleScene, packages and project settings are unchanged.

Backup before controller installation: `Motel_Blockout_V3_before_controller.unity` (outside Assets).

Editor command: **Tools > Motel > Validate First Person Controller**. It opens the saved scene in a disposable preview scene, checks references and invokes the native CharacterController.Move along a route into all four rooms and into a wall. It never saves or changes the user's open scene. Results: `unity-validation.txt`.

Native movement checks exercise colliders without entering Play Mode. Keyboard/mouse behavior, cursor focus, responsiveness and camera clearance at all view angles still require the manual Game view test above.

Completed validation: Unity 6000.3.19f1 loaded the saved scene and compiled the scripts; native CharacterController movement reached all four room entrances, stayed supported by floors and stopped at a wall at sprint speed. No error/exception/assert logs were captured during these checks. Play Mode input/cursor tests were not executed: desktop UI automation failed to start (Sky Computer Use service startup request failed).
