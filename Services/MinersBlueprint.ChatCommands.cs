namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private const string ChatCommandsPluginGuid = ModInfo.CHAT_COMMANDS_PLUGIN_GUID;
    private const string ChatCommandsApiTypeName = "ChatCommands.ChatCommandsApi";
    private const string ChatCommandDefinitionTypeName = "ChatCommands.ChatCommandsApi+CommandDefinition";
    private const string MbCommandPrefix = "mb";
    private static readonly string[] DirectionNames = { "north", "south", "east", "west", "up", "down" };
    private static readonly MbCommandInfo[] MbCommandDefinitions =
    {
        new("/mb set pos 1", "Sets the first selection point from the object or world point you are aiming at."),
        new("/mb set pos 2", "Sets the second selection point from the object or world point you are aiming at."),
        new("/mb pos", "Prints the current selection point coordinates and selected object count."),
        new("/mb size", "Prints the current selection dimensions in blocks."),
        new("/mb copy", "Copies the current selection into the blueprint clipboard."),
        new("/mb cut", "Copies the current selection, then removes it using the active build mode."),
        new("/mb tool", "Adds a tagged pickaxe that can set selection positions with left and right click."),
        new("/mb mode normal", "Requires and consumes inventory items during paste."),
        new("/mb mode unlimited", "Bypasses inventory checks and item consumption during paste."),
        new("/mb blueprints save selection <FileName>", "Exports the current copied clipboard to FileName.blueprint."),
        new("/mb blueprints save all <FileName>", "Exports all supported saveable world objects to FileName.blueprint."),
        new("/mb blueprints import <FileName>", "Loads FileName.blueprint into the clipboard and places the ghost preview near the player."),
        new("/mb blueprints list", "Lists all available blueprint files."),
        new("/mb blueprints rename <CurrentFileName> <NewFileName>", "Renames an existing blueprint file."),
        new("/mb blueprints delete <CurrentFileName>", "Queues a blueprint file for deletion; follow with /mb confirm."),
        new("/mb confirm", "Confirms the current pending blueprint delete or selection removal."),
        new("/mb undo [Count]", "Undoes the latest paste, cut, or remove action, optionally repeating Count times."),
        new("/mb remove", "Removes all saveable world objects inside the current selection."),
        new("/mb rotate 90", "Rotates the current clipboard/ghost 90 degrees clockwise."),
        new("/mb rotate 180", "Rotates the current clipboard/ghost 180 degrees."),
        new("/mb rotate 270", "Rotates the current clipboard/ghost 270 degrees clockwise."),
        new("/mb mirror", "Mirrors the current clipboard/ghost across its local X axis."),
        new("/mb shift <Direction> <Amount>", "Shifts the active selection box by a whole-tile amount before copy/cut."),
        new("/mb grow <Amount>", "Expands the active selection equally in every direction."),
        new("/mb shrink <Amount>", "Contracts the active selection equally in every direction."),
        new("/mb expand <Direction> <Amount>", "Expands one face of the active selection."),
        new("/mb contract <Direction> <Amount>", "Contracts one face of the active selection."),
        new("/mb stack <Count> <Direction> <Gap>", "Stacks the current clipboard using its own size plus a whole-tile gap."),
        new("/mb paste", "Opens the ghost preview for the current clipboard near the player."),
        new("/mb place", "Places the current clipboard at the active ghost preview anchor."),
        new("/mb clear clipboard", "Clears only the current clipboard contents."),
        new("/mb clear ghost", "Clears only the current ghost preview."),
        new("/mb clear selection", "Clears only the current selection."),
        new("/mb clear all", "Clears the ghost preview and current selection points.")
    };
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
    private MethodInfo? _chatSetAutocompleteCommandsMethod;
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

        RefreshBlueprintCommandCatalog();
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
        _chatSetAutocompleteCommandsMethod = _chatCommandsApiType.GetMethod(
            "SetAutocompleteCommands",
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

    private void PublishPrefixCommands(string prefix, IReadOnlyList<MbCommandInfo> definitions, IEnumerable<string> catalog, string label)
    {
        if (_chatSetCommandsWithDescriptionsMethod != null &&
            _chatCommandDefinitionType != null &&
            _chatCommandDefinitionCtor != null)
        {
            try
            {
                var commandArray = Array.CreateInstance(_chatCommandDefinitionType, definitions.Count);
                for (var i = 0; i < definitions.Count; i++)
                {
                    var item = definitions[i];
                    var instance = _chatCommandDefinitionCtor.Invoke(new object[] { item.Command, item.Description });
                    commandArray.SetValue(instance, i);
                }

                if (InvokeChatCommandsBool(_chatSetCommandsWithDescriptionsMethod, prefix, commandArray))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"{ModInfo.LOG_PREFIX} Failed to publish /{label} command descriptions: {ex.Message}");
            }
        }

        if (!InvokeChatCommandsBool(_chatSetCommandsMethod!, prefix, catalog))
        {
            Logger.LogWarning($"{ModInfo.LOG_PREFIX} Failed to publish /{label} autocomplete commands.");
        }
    }

    private void RefreshBlueprintCommandCatalog()
    {
        if (!_chatCommandsRegistered && !TryResolveChatCommandsApi())
        {
            return;
        }

        PublishPrefixCommands(MbCommandPrefix, MbCommandDefinitions, MbCommandDefinitions.Select(x => x.Command), "mb");

        if (_chatSetAutocompleteCommandsMethod != null)
        {
            var autocompleteCommands = BuildAutocompleteMbCommands();
            if (!InvokeChatCommandsBool(_chatSetAutocompleteCommandsMethod, MbCommandPrefix, autocompleteCommands))
            {
                Logger.LogWarning($"{ModInfo.LOG_PREFIX} Failed to publish /mb autocomplete command catalog.");
            }
        }
    }

    private IReadOnlyList<string> BuildAutocompleteMbCommands()
    {
        var commands = new List<string>(MbCommandDefinitions.Select(x => x.Command));

        foreach (var direction in DirectionNames)
        {
            commands.Add($"/mb shift {direction} <Amount>");
            commands.Add($"/mb expand {direction} <Amount>");
            commands.Add($"/mb contract {direction} <Amount>");
            commands.Add($"/mb stack <Count> {direction} <Gap>");
        }

        foreach (var blueprintName in GetBlueprintFileNames())
        {
            commands.Add($"/mb blueprints import {blueprintName}");
            commands.Add($"/mb blueprints delete {blueprintName}");
            commands.Add($"/mb blueprints rename {blueprintName} <NewFileName>");
        }

        return commands;
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

        if (TokensMatch(tokens, "pos"))
        {
            PublishSelectionPositionSummary();
            return;
        }

        if (TokensMatch(tokens, "size"))
        {
            PublishSelectionSizeSummary();
            return;
        }

        if (TokensMatch(tokens, "copy"))
        {
            CopySelection();
            return;
        }

        if (TokensMatch(tokens, "cut"))
        {
            CutSelection();
            return;
        }

        if (TokensMatch(tokens, "tool"))
        {
            GiveSelectionTool();
            return;
        }

        if (tokens.Length == 2 &&
            string.Equals(tokens[0], "mode", StringComparison.OrdinalIgnoreCase))
        {
            HandleModeCommand(tokens[1]);
            return;
        }

        if (tokens.Length >= 2 &&
            string.Equals(tokens[0], "blueprints", StringComparison.OrdinalIgnoreCase))
        {
            HandleBlueprintsCommand(tokens);
            return;
        }

        if (TokensMatch(tokens, "confirm"))
        {
            ConfirmPendingBlueprintDelete();
            return;
        }

        if (tokens.Length >= 1 &&
            string.Equals(tokens[0], "undo", StringComparison.OrdinalIgnoreCase))
        {
            HandleUndoCommand(tokens);
            return;
        }

        if (TokensMatch(tokens, "remove"))
        {
            RemoveSelectionObjects();
            return;
        }

        if (tokens.Length == 2 &&
            string.Equals(tokens[0], "rotate", StringComparison.OrdinalIgnoreCase))
        {
            HandleRotateCommand(tokens[1]);
            return;
        }

        if (TokensMatch(tokens, "mirror"))
        {
            MirrorClipboard();
            return;
        }

        if (tokens.Length == 3 &&
            string.Equals(tokens[0], "shift", StringComparison.OrdinalIgnoreCase))
        {
            HandleShiftCommand(tokens[1], tokens[2]);
            return;
        }

        if (tokens.Length == 2 &&
            string.Equals(tokens[0], "grow", StringComparison.OrdinalIgnoreCase))
        {
            HandleGrowCommand(tokens[1]);
            return;
        }

        if (tokens.Length == 2 &&
            string.Equals(tokens[0], "shrink", StringComparison.OrdinalIgnoreCase))
        {
            HandleShrinkCommand(tokens[1]);
            return;
        }

        if (tokens.Length == 3 &&
            string.Equals(tokens[0], "expand", StringComparison.OrdinalIgnoreCase))
        {
            HandleExpandCommand(tokens[1], tokens[2]);
            return;
        }

        if (tokens.Length == 3 &&
            string.Equals(tokens[0], "contract", StringComparison.OrdinalIgnoreCase))
        {
            HandleContractCommand(tokens[1], tokens[2]);
            return;
        }

        if (tokens.Length == 4 &&
            string.Equals(tokens[0], "stack", StringComparison.OrdinalIgnoreCase))
        {
            HandleStackCommand(tokens[1], tokens[2], tokens[3]);
            return;
        }

        if (TokensMatch(tokens, "paste"))
        {
            PlaceGhostAtPlayerAnchor(showMessage: true);
            return;
        }

        if (TokensMatch(tokens, "place") || TokensMatch(tokens, "set"))
        {
            if (!_ghostPreviewVisible)
            {
                var message = "No ghost preview to confirm. Use /mb paste first.";
                Notify(message, NotificationLevel.Warning, publishToChat: true);
                return;
            }

            PasteClipboard();
            return;
        }

        if (TokensMatch(tokens, "clear") || TokensMatch(tokens, "clear", "ghost"))
        {
            var hadGhost = _ghostPreviewVisible || _ghostPreviewInstances.Count > 0;
            HideGhostPreview(showMessage: false);

            var message = hadGhost
                ? "Cleared ghost preview."
                : "No ghost preview to clear.";
            Notify(message, NotificationLevel.Success, publishToChat: true);
            return;
        }

        if (TokensMatch(tokens, "clear", "clipboard"))
        {
            var hadClipboard = _clipboard != null && _clipboard.Entries.Count > 0;
            _clipboard = null;
            HideGhostPreview(showMessage: false);

            var message = hadClipboard
                ? "Cleared clipboard."
                : "Clipboard is already empty.";
            Notify(message, NotificationLevel.Success, publishToChat: true);
            return;
        }

        if (TokensMatch(tokens, "clear", "selection"))
        {
            _hasPointA = false;
            _hasPointB = false;
            _selectionObjects.Clear();
            EnsureSelectionHighlightBoxCount(0);
            if (_selectionRoot != null && _selectionRoot.activeSelf)
            {
                _selectionRoot.SetActive(false);
            }

            Notify("Cleared current selection.", NotificationLevel.Success, publishToChat: true);
            return;
        }

        if (TokensMatch(tokens, "clear", "all") || TokensMatch(tokens, "clearall"))
        {
            ClearSelectionAndGhost();
            return;
        }

        PublishMbUsage();
    }

    private void HandleExportCommand(IReadOnlyList<string> tokens)
    {
        Notify("Use /mb blueprints save selection|all <FileName>.", NotificationLevel.Warning, 4.6f, publishToChat: true);
    }

    private void HandleModeCommand(string rawMode)
    {
        switch ((rawMode ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "normal":
                SetBuildMode(BuildMode.Normal, publishToChat: true);
                return;
            case "unlimited":
                SetBuildMode(BuildMode.Unlimited, publishToChat: true);
                return;
            default:
                Notify("Usage: /mb mode normal|unlimited", NotificationLevel.Warning, 4.6f, publishToChat: true);
                return;
        }
    }

    private void HandleBlueprintsCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 2 && string.Equals(tokens[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            ListBlueprints();
            return;
        }

        if (tokens.Count >= 3 && string.Equals(tokens[1], "import", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = string.Join(" ", tokens.Skip(2));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Notify("Usage: /mb blueprints import <FileName>", NotificationLevel.Warning, 4.6f, publishToChat: true);
                return;
            }

            ImportBlueprint(fileName);
            return;
        }

        if (tokens.Count >= 4 && string.Equals(tokens[1], "save", StringComparison.OrdinalIgnoreCase))
        {
            var scope = tokens[2];
            var fileName = string.Join(" ", tokens.Skip(3));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Notify("Usage: /mb blueprints save selection|all <FileName>", NotificationLevel.Warning, 4.6f, publishToChat: true);
                return;
            }

            switch (scope.ToLowerInvariant())
            {
                case "selection":
                    ExportSelectionBlueprint(fileName);
                    return;
                case "all":
                    ExportWorldBlueprint(fileName);
                    return;
                default:
                    Notify("Blueprint save scope must be 'selection' or 'all'.", NotificationLevel.Warning, 4.6f, publishToChat: true);
                    return;
            }
        }

        if (tokens.Count >= 4 && string.Equals(tokens[1], "rename", StringComparison.OrdinalIgnoreCase))
        {
            var currentFileName = tokens[2];
            var newFileName = string.Join(" ", tokens.Skip(3));
            RenameBlueprint(currentFileName, newFileName);
            return;
        }

        if (tokens.Count >= 3 && string.Equals(tokens[1], "delete", StringComparison.OrdinalIgnoreCase))
        {
            var currentFileName = string.Join(" ", tokens.Skip(2));
            RequestDeleteBlueprint(currentFileName);
            return;
        }

        Notify("Usage: /mb blueprints list | import <FileName> | save selection|all <FileName> | rename <CurrentFileName> <NewFileName> | delete <CurrentFileName>", NotificationLevel.Warning, 5f, publishToChat: true);
    }

    private void HandleImportCommand(IReadOnlyList<string> tokens)
    {
        Notify("Use /mb blueprints import <FileName>.", NotificationLevel.Warning, 4.6f, publishToChat: true);
    }

    private void HandleRotateCommand(string rawDegrees)
    {
        switch ((rawDegrees ?? string.Empty).Trim())
        {
            case "90":
                RotateClipboard(90);
                return;
            case "180":
                RotateClipboard(180);
                return;
            case "270":
                RotateClipboard(270);
                return;
            default:
                Notify("Usage: /mb rotate 90|180|270", NotificationLevel.Warning, 4.6f, publishToChat: true);
                return;
        }
    }

    private void HandleUndoCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 1)
        {
            UndoLastPaste();
            return;
        }

        if (tokens.Count == 2 && int.TryParse(tokens[1], out var count))
        {
            UndoLastPaste(count);
            return;
        }

        Notify("Usage: /mb undo [Count]", NotificationLevel.Warning, 4.6f, publishToChat: true);
    }

    private void HandleStackCommand(string rawCount, string rawDirection, string rawGap)
    {
        if (!int.TryParse(rawCount, out var count) || count <= 0)
        {
            Notify("Stack count must be a positive whole number.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        if (!int.TryParse(rawGap, out var gap) || gap < 0)
        {
            Notify("Stack gap must be a whole number 0 or higher.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        StackClipboard(count, rawDirection, gap);
    }

    private void HandleShiftCommand(string rawDirection, string rawAmount)
    {
        if (!int.TryParse(rawAmount, out var amount) || amount <= 0)
        {
            Notify("Shift amount must be a positive whole number.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        ShiftSelection(rawDirection, amount);
    }

    private void HandleGrowCommand(string rawAmount)
    {
        if (!int.TryParse(rawAmount, out var amount) || amount <= 0)
        {
            Notify("Grow amount must be a positive whole number.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        GrowSelection(amount);
    }

    private void HandleShrinkCommand(string rawAmount)
    {
        if (!int.TryParse(rawAmount, out var amount) || amount <= 0)
        {
            Notify("Shrink amount must be a positive whole number.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        ShrinkSelection(amount);
    }

    private void HandleExpandCommand(string rawDirection, string rawAmount)
    {
        if (!int.TryParse(rawAmount, out var amount) || amount <= 0)
        {
            Notify("Expand amount must be a positive whole number.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        ExpandSelection(rawDirection, amount);
    }

    private void HandleContractCommand(string rawDirection, string rawAmount)
    {
        if (!int.TryParse(rawAmount, out var amount) || amount <= 0)
        {
            Notify("Contract amount must be a positive whole number.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        ContractSelection(rawDirection, amount);
    }

    private static string[] TokenizeMbCommand(string rawArgs)
    {
        if (string.IsNullOrWhiteSpace(rawArgs))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        char quote = '\0';
        for (var i = 0; i < rawArgs.Length; i++)
        {
            var c = rawArgs[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                    continue;
                }

                current.Append(c);
                continue;
            }

            if (c == '\'' || c == '"')
            {
                quote = c;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (current.Length == 0) continue;
                tokens.Add(current.ToString().TrimEnd('.', ',', ';', ':'));
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString().TrimEnd('.', ',', ';', ':'));
        }

        return tokens
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
        HideGhostPreview(showMessage: false);

        _hasPointA = false;
        _hasPointB = false;
        _selectionObjects.Clear();
        EnsureSelectionHighlightBoxCount(0);
        if (_selectionRoot != null && _selectionRoot.activeSelf)
        {
            _selectionRoot.SetActive(false);
        }

        var message = hadGhost
            ? "Cleared ghost preview and current selection."
            : "Cleared current selection.";
        Notify(message, NotificationLevel.Success, publishToChat: true);
    }

    private void PublishSelectionPositionSummary()
    {
        if (!_hasPointA)
        {
            Notify("Selection start point is not set.", NotificationLevel.Warning, 4.2f, "Selection", publishToChat: true);
            return;
        }

        var pos1 = FormatVec(_pointA);
        var pos2 = _hasPointB ? FormatVec(_pointB) : "not set";
        var objectCount = _hasPointB ? _selectionObjects.Count : 0;
        Notify($"Pos 1: {pos1}", NotificationLevel.Info, 4.2f, "Selection", publishToChat: true);
        Notify($"Pos 2: {pos2}", NotificationLevel.Info, 4.2f, "Selection", publishToChat: true);
        Notify($"Selected objects: {objectCount}", NotificationLevel.Info, 4.2f, "Selection", publishToChat: true);
    }

    private void PublishSelectionSizeSummary()
    {
        if (!_hasPointA || !_hasPointB)
        {
            Notify("Create a full selection first.", NotificationLevel.Warning, 4.2f, "Selection", publishToChat: true);
            return;
        }

        var width = Mathf.Abs(_cellB.x - _cellA.x) + 1;
        var height = Mathf.Abs(_cellB.y - _cellA.y) + 1;
        var depth = Mathf.Abs(_cellB.z - _cellA.z) + 1;
        Notify($"Selection size: {width} x {height} x {depth}", NotificationLevel.Info, 4.2f, "Selection", publishToChat: true);
    }

    private void PublishMbUsage()
    {
        const string usage = "Usage: /mb set pos 1|2, /mb pos, /mb size, /mb copy, /mb cut, /mb tool, /mb mode normal|unlimited, /mb blueprints list|import|save|rename|delete, /mb confirm, /mb undo [Count], /mb remove, /mb rotate 90|180|270, /mb mirror, /mb shift <Direction> <Amount>, /mb grow <Amount>, /mb shrink <Amount>, /mb expand <Direction> <Amount>, /mb contract <Direction> <Amount>, /mb stack <Count> <Direction> <Gap>, /mb paste, /mb place, /mb clear clipboard|ghost|selection|all";
        Notify(usage, NotificationLevel.Warning, 4.6f, publishToChat: true);
    }

    private void PublishMbHelpHint()
    {
        const string message = "Use /help mb for command descriptions, or type /mb for a usage summary.";
        Notify(message, NotificationLevel.Info, 4.6f, publishToChat: true);
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
