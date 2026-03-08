namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private const string ChatCommandsPluginGuid = ModInfo.CHAT_COMMANDS_PLUGIN_GUID;
    private const string ChatCommandsApiTypeName = "ChatCommands.ChatCommandsApi";
    private const string ChatCommandDefinitionTypeName = "ChatCommands.ChatCommandsApi+CommandDefinition";
    private const string MbCommandPrefix = "mb";
    private static readonly MbCommandInfo[] MbCommandDefinitions =
    {
        new("/mb set pos 1", "Sets the first selection point from the object or world point you are aiming at."),
        new("/mb set pos 2", "Sets the second selection point from the object or world point you are aiming at."),
        new("/mb copy", "Copies the current selection into the blueprint clipboard."),
        new("/mb paste", "Places the ghost preview/anchor for the current clipboard."),
        new("/mb set", "Confirms the active ghost preview and places all clipboard objects."),
        new("/mb clear", "Clears only the current ghost preview."),
        new("/mb clear all", "Clears ghost preview and current selection points.")
    };
    private static readonly string[] MbCommandCatalog = MbCommandDefinitions.Select(x => x.Command).ToArray();

    private readonly struct MbCommandInfo
    {
        public MbCommandInfo(string command, string description)
        {
            Command = command ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public string Command { get; }
        public string Description { get; }
    }

    private Type? _chatCommandsApiType;
    private Type? _chatCommandDefinitionType;
    private ConstructorInfo? _chatCommandDefinitionCtor;
    private MethodInfo? _chatRegisterPrefixMethod;
    private MethodInfo? _chatSetCommandsMethod;
    private MethodInfo? _chatSetCommandsWithDescriptionsMethod;
    private MethodInfo? _chatUnregisterPrefixMethod;
    private MethodInfo? _chatPublishInfoMethod;
    private MethodInfo? _chatPublishErrorMethod;
    private PropertyInfo? _chatIsAvailableProperty;
    private bool _chatCommandsRegistered;
    private float _nextChatCommandsRegisterAttemptTime;

    private void TryRegisterChatCommands()
    {
        if (_chatCommandsRegistered) return;
        if (!Chainloader.PluginInfos.ContainsKey(ChatCommandsPluginGuid)) return;
        if (!TryResolveChatCommandsApi()) return;
        if (_chatIsAvailableProperty != null)
        {
            if (_chatIsAvailableProperty.GetValue(null) is not bool isAvailable || !isAvailable)
            {
                return;
            }
        }

        if (!InvokeChatCommandsBool(_chatRegisterPrefixMethod!, MbCommandPrefix, ModInfo.PLUGIN_GUID, (Action<string>)HandleMbCommand, "Miner's Blueprint controls"))
        {
            return;
        }

        PublishMbCommands();

        _chatCommandsRegistered = true;
        Logger.LogInfo($"{ModInfo.LOG_PREFIX} Registered /mb command prefix with Chat Commands.");
    }

    private void TryUnregisterChatCommands()
    {
        if (!_chatCommandsRegistered) return;
        if (!TryResolveChatCommandsApi()) return;

        InvokeChatCommandsBool(_chatUnregisterPrefixMethod!, MbCommandPrefix);
        _chatCommandsRegistered = false;
    }

    private bool TryResolveChatCommandsApi()
    {
        if (_chatCommandsApiType != null)
        {
            return _chatRegisterPrefixMethod != null &&
                   _chatSetCommandsMethod != null &&
                   _chatUnregisterPrefixMethod != null;
        }

        _chatCommandsApiType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(a => a.GetType(ChatCommandsApiTypeName, throwOnError: false))
            .FirstOrDefault(t => t != null);
        if (_chatCommandsApiType == null)
        {
            return false;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        _chatRegisterPrefixMethod = _chatCommandsApiType.GetMethod(
            "RegisterPrefix",
            flags,
            null,
            new[] { typeof(string), typeof(string), typeof(Action<string>), typeof(string) },
            null);
        _chatSetCommandsMethod = _chatCommandsApiType.GetMethod(
            "SetCommands",
            flags,
            null,
            new[] { typeof(string), typeof(IEnumerable<string>) },
            null);
        _chatCommandDefinitionType = _chatCommandsApiType.Assembly.GetType(ChatCommandDefinitionTypeName, throwOnError: false);
        if (_chatCommandDefinitionType != null)
        {
            _chatCommandDefinitionCtor = _chatCommandDefinitionType.GetConstructor(new[] { typeof(string), typeof(string) });
            var setCommandsMethods = _chatCommandsApiType
                .GetMethods(flags)
                .Where(m => m.Name == "SetCommands")
                .ToArray();
            _chatSetCommandsWithDescriptionsMethod = setCommandsMethods.FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                if (p.Length != 2) return false;
                if (p[0].ParameterType != typeof(string)) return false;
                if (!p[1].ParameterType.IsGenericType) return false;
                if (p[1].ParameterType.GetGenericTypeDefinition() != typeof(IEnumerable<>)) return false;
                return p[1].ParameterType.GetGenericArguments()[0] == _chatCommandDefinitionType;
            });
        }
        _chatUnregisterPrefixMethod = _chatCommandsApiType.GetMethod(
            "UnregisterPrefix",
            flags,
            null,
            new[] { typeof(string) },
            null);
        _chatPublishInfoMethod = _chatCommandsApiType.GetMethod(
            "PublishInfo",
            flags,
            null,
            new[] { typeof(string) },
            null);
        _chatPublishErrorMethod = _chatCommandsApiType.GetMethod(
            "PublishError",
            flags,
            null,
            new[] { typeof(string) },
            null);
        _chatIsAvailableProperty = _chatCommandsApiType.GetProperty("IsAvailable", flags);

        if (_chatRegisterPrefixMethod == null || _chatSetCommandsMethod == null || _chatUnregisterPrefixMethod == null)
        {
            Logger.LogWarning($"{ModInfo.LOG_PREFIX} Chat Commands API methods are missing expected signatures.");
            return false;
        }

        return true;
    }

    private void PublishMbCommands()
    {
        if (_chatSetCommandsWithDescriptionsMethod != null &&
            _chatCommandDefinitionType != null &&
            _chatCommandDefinitionCtor != null)
        {
            try
            {
                var commandArray = Array.CreateInstance(_chatCommandDefinitionType, MbCommandDefinitions.Length);
                for (var i = 0; i < MbCommandDefinitions.Length; i++)
                {
                    var item = MbCommandDefinitions[i];
                    var instance = _chatCommandDefinitionCtor.Invoke(new object[] { item.Command, item.Description });
                    commandArray.SetValue(instance, i);
                }

                if (InvokeChatCommandsBool(_chatSetCommandsWithDescriptionsMethod, MbCommandPrefix, commandArray))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"{ModInfo.LOG_PREFIX} Failed to publish /mb command descriptions: {ex.Message}");
            }
        }

        if (!InvokeChatCommandsBool(_chatSetCommandsMethod!, MbCommandPrefix, MbCommandCatalog))
        {
            Logger.LogWarning($"{ModInfo.LOG_PREFIX} Failed to publish /mb autocomplete commands.");
        }
    }

    private void UpdateChatCommandsRegistration()
    {
        if (_chatCommandsRegistered) return;
        if (Time.unscaledTime < _nextChatCommandsRegisterAttemptTime) return;

        _nextChatCommandsRegisterAttemptTime = Time.unscaledTime + 1f;
        TryRegisterChatCommands();
    }

    private static bool InvokeChatCommandsBool(MethodInfo method, params object[] args)
    {
        try
        {
            var result = method.Invoke(null, args);
            return result is bool ok && ok;
        }
        catch
        {
            return false;
        }
    }

    private void HandleMbCommand(string rawArgs)
    {
        var tokens = TokenizeMbCommand(rawArgs);
        if (tokens.Length == 0)
        {
            PublishMbHelpHint();
            return;
        }

        if (TokensMatch(tokens, "set", "pos", "1"))
        {
            SetStartPoint();
            return;
        }

        if (TokensMatch(tokens, "set", "pos", "2"))
        {
            SetEndPoint();
            return;
        }

        if (TokensMatch(tokens, "copy"))
        {
            CopySelection();
            return;
        }

        if (TokensMatch(tokens, "paste"))
        {
            PlaceGhostAtPlayerAnchor(showToast: true);
            return;
        }

        if (TokensMatch(tokens, "set"))
        {
            if (!_ghostPreviewVisible)
            {
                var message = "No ghost preview to confirm. Use /mb paste first.";
                _toasts.Push(message, ToastType.Warning);
                PublishMbError(message);
                return;
            }

            PasteClipboard();
            return;
        }

        if (TokensMatch(tokens, "clear"))
        {
            var hadGhost = _ghostPreviewVisible || _ghostPreviewInstances.Count > 0;
            HideGhostPreview(showToast: false);

            var message = hadGhost
                ? "Cleared ghost preview."
                : "No ghost preview to clear.";
            _toasts.Push(message, ToastType.Success);
            PublishMbInfo(message);
            return;
        }

        if (TokensMatch(tokens, "clear", "all"))
        {
            ClearSelectionAndGhost();
            return;
        }

        PublishMbUsage();
    }

    private static string[] TokenizeMbCommand(string rawArgs)
    {
        return (rawArgs ?? string.Empty)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().TrimEnd('.', ',', ';', ':'))
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static bool TokensMatch(IReadOnlyList<string> tokens, params string[] expected)
    {
        if (tokens.Count != expected.Length) return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(tokens[i], expected[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private void ClearSelectionAndGhost()
    {
        var hadGhost = _ghostPreviewVisible || _ghostPreviewInstances.Count > 0;
        HideGhostPreview(showToast: false);

        _hasPointA = false;
        _hasPointB = false;
        _selectionObjects.Clear();
        if (_selectionRoot != null && _selectionRoot.activeSelf)
        {
            _selectionRoot.SetActive(false);
        }

        var message = hadGhost
            ? "Cleared ghost preview and current selection."
            : "Cleared current selection.";
        _toasts.Push(message, ToastType.Success);
        PublishMbInfo(message);
    }

    private void PublishMbUsage()
    {
        const string usage = "Usage: /mb set pos 1|2, /mb copy, /mb paste, /mb set, /mb clear, /mb clear all";
        _toasts.Push(usage, ToastType.Warning, duration: 4.6f);
        PublishMbError(usage);
    }

    private void PublishMbHelpHint()
    {
        const string message = "Type /help mb for detailed command info.";
        _toasts.Push(message, ToastType.Info, duration: 4.6f);
        PublishMbInfo(message);
    }

    private void PublishMbInfo(string message)
    {
        if (_chatPublishInfoMethod == null) return;
        InvokeChatCommandsBool(_chatPublishInfoMethod, message);
    }

    private void PublishMbError(string message)
    {
        if (_chatPublishErrorMethod == null) return;
        InvokeChatCommandsBool(_chatPublishErrorMethod, message);
    }
}
