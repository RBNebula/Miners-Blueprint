namespace MinersBlueprint;

[BepInPlugin(ModInfo.PLUGIN_GUID, ModInfo.PLUGIN_NAME, ModInfo.PLUGIN_VERSION)]
[BepInDependency(ModInfo.REBIND_PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(ModInfo.CHAT_COMMANDS_PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
public sealed partial class MinersBlueprint : BaseUnityPlugin
{
    private void Awake()
    {
        _setStartKey = Config.Bind("Input", "SetStartKey", DefaultSetStartShortcut, "Set selection start point from looked object/hit.");
        _setEndKey = Config.Bind("Input", "SetEndKey", DefaultSetEndShortcut, "Set selection end point from looked object/hit.");
        _copyKey = Config.Bind("Input", "CopyKey", DefaultCopyShortcut, "Copy selected objects.");
        _pasteKey = Config.Bind("Input", "PasteKey", DefaultPasteShortcut, "Place static ghost anchor on first press, paste at ghost anchor on second press.");
        _toggleGhostPreviewKey = Config.Bind("Input", "ToggleGhostPreviewKey", DefaultToggleGhostPreviewShortcut, "Toggle ghost preview of clipboard placement.");
        _ghostMoveXMinusKey = Config.Bind("Input", "GhostMoveXMinusKey", new KeyboardShortcut(DefaultGhostMoveXMinusKey), "Move ghost preview one tile on X-.");
        _ghostMoveXPlusKey = Config.Bind("Input", "GhostMoveXPlusKey", new KeyboardShortcut(DefaultGhostMoveXPlusKey), "Move ghost preview one tile on X+.");
        _ghostMoveZMinusKey = Config.Bind("Input", "GhostMoveZMinusKey", new KeyboardShortcut(DefaultGhostMoveZMinusKey), "Move ghost preview one tile on Z-.");
        _ghostMoveZPlusKey = Config.Bind("Input", "GhostMoveZPlusKey", new KeyboardShortcut(DefaultGhostMoveZPlusKey), "Move ghost preview one tile on Z+.");
        _ghostMoveYMinusKey = Config.Bind("Input", "GhostMoveYMinusKey", new KeyboardShortcut(DefaultGhostMoveYMinusKey), "Move ghost preview one tile on Y- (lower elevation).");
        _ghostMoveYPlusKey = Config.Bind("Input", "GhostMoveYPlusKey", new KeyboardShortcut(DefaultGhostMoveYPlusKey), "Move ghost preview one tile on Y+ (raise elevation).");
        _toggleWindowKey = Config.Bind("Input", "ToggleDebugWindowKey", new KeyboardShortcut(DefaultToggleWindowKey), "Toggle debug window visibility (off by default).");
        _lookDistance = Config.Bind("Selection", "LookDistance", 80f, "Raycast distance used to pick selection points.");
        _cellSize = Config.Bind("Selection", "CellSize", 1f, "Grid cell size used to expand selection to outside cell edges.");

        _toasts.MaxToasts = 8;
        _toasts.DefaultDuration = 3.2f;
        SetupSelectionRenderer();
        _rebindRegistered = TryRegisterRebindKeybinds();
        _nextRebindAttemptTime = Time.unscaledTime + 1f;
        TryRegisterChatCommands();
        Logger.LogInfo($"{ModInfo.LOG_PREFIX} {ModInfo.PLUGIN_VERSION} initialized.");
    }

    private void OnDestroy()
    {
        TryUnregisterChatCommands();

        for (var i = 0; i < _rebindHandles.Count; i++)
        {
            try
            {
                _rebindHandles[i].Dispose();
            }
            catch
            {
                // Ignore cleanup failures from external APIs.
            }
        }
        _rebindHandles.Clear();

        if (_selectionRoot != null)
        {
            Destroy(_selectionRoot);
            _selectionRoot = null;
        }
        if (_lineMaterial != null)
        {
            Destroy(_lineMaterial);
            _lineMaterial = null;
        }
        DestroyGhostPreviewVisuals();
        if (_ghostMaterial != null)
        {
            Destroy(_ghostMaterial);
            _ghostMaterial = null;
        }
        DestroyGhostBlockerPreviewVisuals();
        if (_ghostBlockerLineMaterial != null)
        {
            Destroy(_ghostBlockerLineMaterial);
            _ghostBlockerLineMaterial = null;
        }
    }
}
