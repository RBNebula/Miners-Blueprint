MINERS BLUEPRINT - 2.0.0
Copy, export, import, and paste building layouts in MineMogul using a grid selection, ghost preview, and configurable build rules.

:: REQUIREMENTS ::
- MineMogul up to date
- BepInEx installed

:: OPTIONAL INTEGRATIONS ::
- Rebind (com.rebind) for keybind rebinding UI
- Chat Commands (com.ChatCommands) for /mb commands

:: FEATURES ::
- Select area start/end points and copy valid building objects plus saveable world items in the box
- Clipboard stores object type, rotation, offsets, and supported custom save data
- Export copied selections or full world builds, including supported saveable world items, to .blueprint JSON files
- Import .blueprint files back into the runtime clipboard
- Ghost preview placement before confirming paste
- Adaptive staged paste at 0.1s per slice, using the axis with the largest span so each step places fewer objects
- Ghost movement controls on X/Z/Y for precise alignment
- Paste checks required inventory items before placing
- Mode Unlimited bypasses inventory checks and item consumption
- Missing-item popup listing exactly what is required
- Occupied target replacement with inventory refund when possible
- Optional debug window with selection and clipboard breakdown

:: CHAT COMMANDS ::
- /mb set pos 1 - Sets the first selection point from the object or world point you are aiming at.
- /mb set pos 2 - Sets the second selection point from the object or world point you are aiming at.
- /mb pos - Prints the current selection point coordinates and selected object count.
- /mb size - Prints the current selection dimensions in blocks.
- /mb copy - Copies the current selection into the blueprint clipboard.
- /mb cut - Copies the current selection, then removes it using the active build mode.
- /mb tool - Adds a tagged pickaxe that sets pos1 with left click, pos2 with right click, and copies with middle click.
- /mb mode normal - Requires and consumes inventory items during paste.
- /mb mode unlimited - Bypasses inventory checks and item consumption during paste.
- /mb blueprints save selection <FileName> - Exports the current copied clipboard to FileName.blueprint.
- /mb blueprints save all <FileName> - Exports all supported saveable world objects to FileName.blueprint.
- /mb blueprints import <FileName> - Loads FileName.blueprint into the clipboard and places the ghost preview near the player.
- /mb blueprints list - Lists all available blueprint files.
- /mb blueprints rename <CurrentFileName> <NewFileName> - Renames an existing blueprint file.
- /mb blueprints delete <CurrentFileName> - Queues a blueprint file for deletion; follow with /mb confirm.
- /mb confirm - Confirms the current pending blueprint delete or selection removal.
- /mb undo [Count] - Undoes the latest paste, cut, or remove action, optionally repeating Count times.
- /mb remove - Removes all saveable world objects inside the current selection.
- /mb rotate 90 - Rotates the current clipboard/ghost 90 degrees clockwise.
- /mb rotate 180 - Rotates the current clipboard/ghost 180 degrees.
- /mb rotate 270 - Rotates the current clipboard/ghost 270 degrees clockwise.
- /mb mirror - Mirrors the current clipboard/ghost across its local X axis.
- /mb shift <Direction> <Amount> - Shifts the active selection box by a whole-tile amount before copy/cut.
- /mb grow <Amount> - Expands the active selection equally in every direction.
- /mb shrink <Amount> - Contracts the active selection equally in every direction.
- /mb expand <Direction> <Amount> - Expands one face of the active selection.
- /mb contract <Direction> <Amount> - Contracts one face of the active selection.
- /mb stack <Count> <Direction> <Gap> - Stacks the current clipboard using its own size plus a whole-tile gap.
- /mb paste - Opens the ghost preview for the current clipboard near the player.
- /mb place - Places the current clipboard at the active ghost preview anchor.
- /mb clear clipboard - Clears only the current clipboard contents.
- /mb clear ghost - Clears only the current ghost preview.
- /mb clear selection - Clears only the current selection.
- /mb clear all - Clears the ghost preview and current selection points.

Direction aliases:
- n = north
- s = south
- e = east
- w = west
- u = up
- d = down

:: GENERAL NOTES ::
- Selection and placement are grid-snapped
- If ghost preview is active, paste confirms the ghost anchor location
- blueprints save selection preserves the copied anchor/player context for reusable sharing
- blueprints save all preserves world layout and re-imports back to the saved world anchor
- Selection and blueprints save all now include supported physics/shop items such as tools, pumpkins, sleds, and similar saveable world objects
- /mb cut copies the current selection, then removes it; normal mode returns supported objects to inventory while unlimited removes them directly
- /mb remove deletes all supported saveable world objects inside the current selection and can be reversed with /mb undo or /mb undo <Count>
- shift moves the active selection box before you copy or cut
- grow and shrink expand or contract the active selection box on all sides before copy/cut
- expand and contract move only the selected face of the active selection box
- rotate and mirror transform the current clipboard and immediately refresh any live ghost preview
- stack repeats the current clipboard using its own size plus a whole-tile gap and immediately refreshes the ghost preview
- Paste now chooses the axis with the largest span and places from the minimum coordinate to the maximum with a 0.1s delay between slices
- Notifications appear in the top-right and fade quickly after actions
- Hotkeys only process when game UI menus are not active

:: KNOWN ISSUES ::
- Some modded/variant prefabs may not resolve perfectly if source prefab definitions change between sessions
- Replacement can fail when inventory is full (warning popup is shown)

:: CREDITS ::
- Made by RBN
