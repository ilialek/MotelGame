# Motel Game implementation requirements

## UI across resolutions

- All new or modified UI must keep its layout, readable content, reachable controls, and matching pointer hit areas across screen resolutions and aspect ratios, including Unity Editor Game View and standalone windows.
- Treat the physical in-world display as the boundary for diegetic UI. Reception computer tabs, camera feeds, and photo popups must remain inside the monitor glass, not expand relative to the whole game window.
- Preserve proportions with a reference layout and uniform scaling. Use anchors, clipping, scrolling, or aspect-aware framing as appropriate. Recalculate when the window size changes; do not cache only startup dimensions.
- Apply the same coordinate transform to drawing and input. Save and restore shared GUI state and camera settings when entering/exiting a workstation.
- Validate representative 4:3, 16:9, 16:10, and ultrawide resolutions; include resizing while the UI is open. Report which resolutions and Editor/Player paths were actually exercised. Do not claim that an untested screen size or platform was verified.
- Existing UI outside the requested change should be migrated when it is next touched, rather than rewritten without scope approval.
