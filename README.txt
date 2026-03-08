MINERS BLUEPRINT - 1.0.1
Copy and paste building layouts in MineMogul using a grid selection, ghost preview, and inventory-aware placement.

:: REQUIREMENTS ::
- MineMogul up to date
- BepInEx installed

:: OPTIONAL INTEGRATIONS ::
- Rebind (com.rebind) for keybind rebinding UI
- Chat Commands (com.chatcommands) for /mb commands

:: FEATURES ::
- Select area start/end points and copy all valid building objects in the box
- Clipboard stores object type, rotation, offsets, and supported custom save data
- Ghost preview placement before confirming paste
- Ghost movement controls on X/Z/Y for precise alignment
- Paste checks required inventory items before placing
- Missing-item popup listing exactly what is required
- Occupied target replacement with inventory refund when possible
- Optional debug window with selection and clipboard breakdown

:: CHAT COMMANDS ::
- /mb set pos 1
- /mb set pos 2
- /mb copy
- /mb paste
- /mb set
- /mb clear
- /mb clear all

:: GENERAL NOTES ::
- Selection and placement are grid-snapped
- If ghost preview is active, paste confirms the ghost anchor location
- Hotkeys only process when game UI menus are not active

:: KNOWN ISSUES ::
- Some modded/variant prefabs may not resolve perfectly if source prefab definitions change between sessions
- Replacement can fail when inventory is full (warning toast is shown)

:: CREDITS ::
- Made by RBN
