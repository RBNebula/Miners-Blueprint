namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private bool TryRegisterRebindKeybinds()
    {
        if (_rebindRegistered || _rebindHandles.Count > 0) return true;

        var apiType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(a => a.GetType("Rebind.RebindApi", throwOnError: false))
            .FirstOrDefault(t => t != null);
        if (apiType == null) return false;

        var registerShortcut = apiType.GetMethod(
            "RegisterKeybind",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[]
            {
                typeof(string),
                typeof(string),
                typeof(Func<KeyboardShortcut>),
                typeof(Action<KeyboardShortcut>),
                typeof(KeyboardShortcut),
                typeof(Action<KeyboardShortcut>)
            },
            null);

        if (registerShortcut != null)
        {
            var registered = 0;
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Set Selection Start", _setStartKey, DefaultSetStartShortcut);
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Set Selection End", _setEndKey, DefaultSetEndShortcut);
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Copy Selection", _copyKey, DefaultCopyShortcut);
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Place Ghost / Confirm Ghost Placement", _pasteKey, DefaultPasteShortcut);
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Toggle/Remove Ghost", _toggleGhostPreviewKey, DefaultToggleGhostPreviewShortcut);
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Ghost Move X-", _ghostMoveXMinusKey, new KeyboardShortcut(DefaultGhostMoveXMinusKey));
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Ghost Move X+", _ghostMoveXPlusKey, new KeyboardShortcut(DefaultGhostMoveXPlusKey));
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Ghost Move Z-", _ghostMoveZMinusKey, new KeyboardShortcut(DefaultGhostMoveZMinusKey));
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Ghost Move Z+", _ghostMoveZPlusKey, new KeyboardShortcut(DefaultGhostMoveZPlusKey));
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Ghost Elevation -", _ghostMoveYMinusKey, new KeyboardShortcut(DefaultGhostMoveYMinusKey));
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Ghost Elevation +", _ghostMoveYPlusKey, new KeyboardShortcut(DefaultGhostMoveYPlusKey));
            registered += RegisterShortcutRebind(registerShortcut, "Miner's Blueprint", "Toggle Debug Window", _toggleWindowKey, new KeyboardShortcut(DefaultToggleWindowKey));

            if (registered > 0)
            {
                Logger.LogInfo($"{ModInfo.LOG_PREFIX} Registered {registered} keybind(s) with Rebind (KeyboardShortcut).");
                return true;
            }
        }

        // Backward compatibility with older Rebind versions.
        var registerLegacy = apiType.GetMethod(
            "RegisterKeybind",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[]
            {
                typeof(string),
                typeof(string),
                typeof(Func<KeyCode>),
                typeof(Action<KeyCode>),
                typeof(KeyCode),
                typeof(Action<KeyCode>)
            },
            null);

        if (registerLegacy == null)
        {
            Logger.LogWarning($"{ModInfo.LOG_PREFIX} Rebind API found but no supported RegisterKeybind signature was found.");
            return false;
        }

        var legacyRegistered = 0;
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Set Selection Start", _setStartKey, DefaultSetStartShortcut);
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Set Selection End", _setEndKey, DefaultSetEndShortcut);
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Copy Selection", _copyKey, DefaultCopyShortcut);
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Place Ghost / Confirm Ghost Placement", _pasteKey, DefaultPasteShortcut);
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Toggle/Remove Ghost", _toggleGhostPreviewKey, DefaultToggleGhostPreviewShortcut);
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Ghost Move X-", _ghostMoveXMinusKey, new KeyboardShortcut(DefaultGhostMoveXMinusKey));
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Ghost Move X+", _ghostMoveXPlusKey, new KeyboardShortcut(DefaultGhostMoveXPlusKey));
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Ghost Move Z-", _ghostMoveZMinusKey, new KeyboardShortcut(DefaultGhostMoveZMinusKey));
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Ghost Move Z+", _ghostMoveZPlusKey, new KeyboardShortcut(DefaultGhostMoveZPlusKey));
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Ghost Elevation -", _ghostMoveYMinusKey, new KeyboardShortcut(DefaultGhostMoveYMinusKey));
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Ghost Elevation +", _ghostMoveYPlusKey, new KeyboardShortcut(DefaultGhostMoveYPlusKey));
        legacyRegistered += RegisterLegacyRebind(registerLegacy, "Miner's Blueprint", "Toggle Debug Window", _toggleWindowKey, new KeyboardShortcut(DefaultToggleWindowKey));

        if (legacyRegistered > 0)
        {
            Logger.LogInfo($"{ModInfo.LOG_PREFIX} Registered {legacyRegistered} keybind(s) with Rebind (legacy KeyCode fallback).");
        }

        return legacyRegistered > 0;
    }

    private int RegisterShortcutRebind(
        MethodInfo register,
        string section,
        string title,
        ConfigEntry<KeyboardShortcut> entry,
        KeyboardShortcut defaultShortcut)
    {
        try
        {
            Func<KeyboardShortcut> getter = () => entry.Value;
            Action<KeyboardShortcut> setter = shortcut => entry.Value = NormalizeShortcut(shortcut);
            Action<KeyboardShortcut> onChanged = _ => Config.Save();

            var handle = register.Invoke(null, new object?[] { section, title, getter, setter, defaultShortcut, onChanged });
            if (handle is IDisposable d)
            {
                _rebindHandles.Add(d);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"{ModInfo.LOG_PREFIX} Failed to register shortcut rebind '{title}': {ex.Message}");
        }

        return 0;
    }

    private int RegisterLegacyRebind(
        MethodInfo register,
        string section,
        string title,
        ConfigEntry<KeyboardShortcut> entry,
        KeyboardShortcut defaultShortcut)
    {
        try
        {
            Func<KeyCode> getter = () => entry.Value.MainKey;
            Action<KeyCode> setter = key => entry.Value = key == KeyCode.None ? KeyboardShortcut.Empty : new KeyboardShortcut(key);
            Action<KeyCode> onChanged = _ => Config.Save();
            var defaultKey = defaultShortcut.MainKey;

            var handle = register.Invoke(null, new object?[] { section, title, getter, setter, defaultKey, onChanged });
            if (handle is IDisposable d)
            {
                _rebindHandles.Add(d);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"{ModInfo.LOG_PREFIX} Failed to register legacy rebind '{title}': {ex.Message}");
        }

        return 0;
    }

    private static KeyboardShortcut NormalizeShortcut(KeyboardShortcut shortcut)
    {
        if (shortcut.MainKey == KeyCode.None) return KeyboardShortcut.Empty;
        return shortcut;
    }

    private bool WasPressed(ConfigEntry<KeyboardShortcut>? entry)
    {
        if (!IsHotkeyInputEnabledCached()) return false;
        if (entry == null) return false;
        var shortcut = entry.Value;
        if (shortcut.MainKey == KeyCode.None) return false;
        return shortcut.IsDown();
    }

    private bool IsHotkeyInputEnabledCached()
    {
        var frame = Time.frameCount;
        if (_hotkeyGateFrame != frame)
        {
            _hotkeyGateFrame = frame;
            _hotkeyGateEnabled = HotkeyGate.IsHotkeyInputEnabled();
        }

        return _hotkeyGateEnabled;
    }

    private static string GetBindingText(ConfigEntry<KeyboardShortcut>? entry)
    {
        if (entry == null) return "-";
        var shortcut = entry.Value;
        if (shortcut.MainKey == KeyCode.None) return "-";

        var parts = shortcut.Modifiers
            .Where(k => k != KeyCode.None)
            .Append(shortcut.MainKey)
            .Select(FormatKey)
            .ToArray();

        if (parts.Length == 0) return "-";
        return string.Join(" + ", parts);
    }

    private static string FormatKey(KeyCode key)
    {
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
        {
            return ((int)(key - KeyCode.Alpha0)).ToString();
        }

        return key switch
        {
            KeyCode.LeftControl => "Ctrl",
            KeyCode.RightControl => "Right Ctrl",
            KeyCode.LeftShift => "Shift",
            KeyCode.RightShift => "Right Shift",
            KeyCode.LeftAlt => "Alt",
            KeyCode.RightAlt => "Right Alt",
            _ => key.ToString()
        };
    }
}
