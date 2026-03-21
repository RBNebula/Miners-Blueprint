using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private static readonly string BlueprintExportDir = Path.Combine(Paths.ConfigPath, "MinersBlueprint", "Blueprints");

    [DataContract]
    private sealed class BlueprintExportFile
    {
        [DataMember(Order = 1)]
        public int FormatVersion { get; set; } = 1;

        [DataMember(Order = 2)]
        public string PluginGuid { get; set; } = ModInfo.PLUGIN_GUID;

        [DataMember(Order = 3)]
        public string PluginVersion { get; set; } = ModInfo.PLUGIN_VERSION;

        [DataMember(Order = 4)]
        public string ExportMode { get; set; } = string.Empty;

        [DataMember(Order = 5)]
        public string ExportedAtUtc { get; set; } = string.Empty;

        [DataMember(Order = 6, EmitDefaultValue = false)]
        public BlueprintVector3? PlayerPosition { get; set; }

        [DataMember(Order = 7, EmitDefaultValue = false)]
        public BlueprintVector3? CopyAnchor { get; set; }

        [DataMember(Order = 8)]
        public List<BlueprintExportEntry> Entries { get; set; } = new();
    }

    [DataContract]
    private sealed class BlueprintExportEntry
    {
        [DataMember(Order = 1)]
        public string SavableObjectId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public int SavableObjectIdValue { get; set; }

        [DataMember(Order = 3)]
        public string RequiredSavableObjectId { get; set; } = string.Empty;

        [DataMember(Order = 4)]
        public int RequiredSavableObjectIdValue { get; set; }

        [DataMember(Order = 5)]
        public BlueprintVector3 Position { get; set; } = new();

        [DataMember(Order = 6)]
        public BlueprintQuaternion Rotation { get; set; } = new();

        [DataMember(Order = 7)]
        public bool SupportsEnabled { get; set; }

        [DataMember(Order = 8)]
        public string CustomData { get; set; } = string.Empty;

        [DataMember(Order = 9)]
        public string Label { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class BlueprintVector3
    {
        [DataMember(Order = 1)]
        public float X { get; set; }

        [DataMember(Order = 2)]
        public float Y { get; set; }

        [DataMember(Order = 3)]
        public float Z { get; set; }
    }

    [DataContract]
    private sealed class BlueprintQuaternion
    {
        [DataMember(Order = 1)]
        public float X { get; set; }

        [DataMember(Order = 2)]
        public float Y { get; set; }

        [DataMember(Order = 3)]
        public float Z { get; set; }

        [DataMember(Order = 4)]
        public float W { get; set; }
    }

    private void ExportSelectionBlueprint(string requestedFileName)
    {
        if (_clipboard == null || _clipboard.Entries.Count == 0)
        {
            Notify("Clipboard is empty. Copy a selection first.", NotificationLevel.Warning, title: "Export", publishToChat: true);
            return;
        }

        var file = new BlueprintExportFile
        {
            ExportMode = "selection",
            ExportedAtUtc = DateTime.UtcNow.ToString("O"),
            PlayerPosition = ToBlueprintVector(_clipboard.PlayerPosition),
            CopyAnchor = ToBlueprintVector(_clipboard.CopyAnchor),
            Entries = _clipboard.Entries.Select(entry => ToBlueprintEntry(entry, entry.RelativeOffset)).ToList()
        };

        WriteBlueprintFile(requestedFileName, file, "selection");
    }

    private void ExportWorldBlueprint(string requestedFileName)
    {
        var worldObjects = FindSelectableWorldObjects()
            .Where(obj => obj != null && obj.Component != null && obj.Saveable != null)
            .ToArray();
        if (worldObjects.Length == 0)
        {
            Notify("No placed saveable world objects were found to export.", NotificationLevel.Warning, title: "Export", publishToChat: true);
            return;
        }

        var entries = new List<BlueprintExportEntry>(worldObjects.Length);
        for (var i = 0; i < worldObjects.Length; i++)
        {
            var obj = worldObjects[i];
            var building = obj.Building;
            var requiredSavableId = building != null
                ? ResolveRequiredSavableId(building)
                : obj.SavableObjectID;
            entries.Add(new BlueprintExportEntry
            {
                SavableObjectId = obj.SavableObjectID.ToString(),
                SavableObjectIdValue = ToBlueprintIdValue(obj.SavableObjectID),
                RequiredSavableObjectId = requiredSavableId.ToString(),
                RequiredSavableObjectIdValue = ToBlueprintIdValue(requiredSavableId),
                Position = ToBlueprintVector(obj.Component.transform.position),
                Rotation = ToBlueprintQuaternion(obj.Component.transform.rotation),
                SupportsEnabled = building != null && building.GetBuildingSupportsEnabled(),
                CustomData = obj.Saveable.GetCustomSaveData() ?? string.Empty,
                Label = obj.Label ?? string.Empty
            });
        }

        var worldAnchor = ResolveWorldExportAnchor(entries);

        var file = new BlueprintExportFile
        {
            ExportMode = "all",
            ExportedAtUtc = DateTime.UtcNow.ToString("O"),
            CopyAnchor = ToBlueprintVector(worldAnchor),
            Entries = entries
        };

        WriteBlueprintFile(requestedFileName, file, "world");
    }

    private void WriteBlueprintFile(string requestedFileName, BlueprintExportFile file, string sourceLabel)
    {
        try
        {
            Directory.CreateDirectory(BlueprintExportDir);

            var safeName = SanitizeBlueprintFileName(requestedFileName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                Notify("Export file name is invalid.", NotificationLevel.Warning, title: "Export", publishToChat: true);
                return;
            }

            var path = Path.Combine(BlueprintExportDir, safeName + ".blueprint");
            var serializer = new DataContractJsonSerializer(typeof(BlueprintExportFile));
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, file);
            var json = Encoding.UTF8.GetString(stream.ToArray());
            File.WriteAllText(path, PrettyPrintJson(json), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Notify($"Exported {file.Entries.Count} {sourceLabel} object(s) to {Path.GetFileName(path)}.", NotificationLevel.Success, 4.2f, "Export", publishToChat: true);
            PublishMbInfo($"Path: {path}");
            PublishMbInfo($"Directory: {BlueprintExportDir}");
            RefreshBlueprintCommandCatalog();
            Logger.LogInfo($"{ModInfo.LOG_PREFIX} Exported blueprint to {path}");
        }
        catch (Exception ex)
        {
            Notify($"Export failed: {ex.Message}", NotificationLevel.Warning, 5f, "Export", publishToChat: true);
        }
    }

    private void ImportBlueprint(string requestedFileName)
    {
        try
        {
            var path = ResolveBlueprintFilePath(requestedFileName);
            if (path == null)
            {
                Notify($"Blueprint file not found: {requestedFileName}", NotificationLevel.Warning, 4.6f, "Import", publishToChat: true);
                return;
            }

            var serializer = new DataContractJsonSerializer(typeof(BlueprintExportFile));
            using var stream = File.OpenRead(path);
            if (serializer.ReadObject(stream) is not BlueprintExportFile file || file.Entries == null || file.Entries.Count == 0)
            {
                Notify("Blueprint file is empty or invalid.", NotificationLevel.Warning, 4.6f, "Import", publishToChat: true);
                return;
            }

            var clipboard = BuildClipboardFromImport(file);
            if (clipboard == null || clipboard.Entries.Count == 0)
            {
                Notify("Blueprint file did not contain any importable objects.", NotificationLevel.Warning, 4.6f, "Import", publishToChat: true);
                return;
            }

            _clipboard = clipboard;
            _hasPointA = false;
            _hasPointB = false;
            _selectionObjects.Clear();
            if (_selectionRoot != null && _selectionRoot.activeSelf)
            {
                _selectionRoot.SetActive(false);
            }

            if (string.Equals((file.ExportMode ?? string.Empty).Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                ShowGhostPreviewAtAnchor(clipboard.CopyAnchor, showMessage: true);
            }
            else
            {
                PlaceGhostAtPlayerAnchor(showMessage: true);
            }
            Notify($"Imported {clipboard.Entries.Count} object(s) from {Path.GetFileName(path)}.", NotificationLevel.Success, 4.2f, "Import", publishToChat: true);
            PublishMbInfo($"Path: {path}");
            PublishMbInfo($"Directory: {BlueprintExportDir}");
            Logger.LogInfo($"{ModInfo.LOG_PREFIX} Imported blueprint from {path}");
        }
        catch (Exception ex)
        {
            Notify($"Import failed: {ex.Message}", NotificationLevel.Warning, 5f, "Import", publishToChat: true);
        }
    }

    private void ListBlueprints()
    {
        try
        {
            Directory.CreateDirectory(BlueprintExportDir);
            var files = Directory.GetFiles(BlueprintExportDir, "*.blueprint", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                Notify("No blueprints were found.", NotificationLevel.Info, 3.6f, "Blueprints", publishToChat: true);
                return;
            }

            Notify($"Found {files.Length} blueprint(s).", NotificationLevel.Success, 3.2f, "Blueprints", publishToChat: true);
            PublishMbInfo($"Directory: {BlueprintExportDir}");
            for (var i = 0; i < files.Length; i++)
            {
                PublishMbInfo(files[i]);
            }
        }
        catch (Exception ex)
        {
            Notify($"Failed to list blueprints: {ex.Message}", NotificationLevel.Warning, 5f, "Blueprints", publishToChat: true);
        }
    }

    private void RenameBlueprint(string currentFileName, string newFileName)
    {
        try
        {
            var source = ResolveBlueprintFilePath(currentFileName);
            if (source == null)
            {
                Notify($"Blueprint file not found: {currentFileName}", NotificationLevel.Warning, 4.6f, "Blueprints", publishToChat: true);
                return;
            }

            var safeNewName = SanitizeBlueprintFileName(newFileName);
            if (string.IsNullOrWhiteSpace(safeNewName))
            {
                Notify("New blueprint file name is invalid.", NotificationLevel.Warning, 4.6f, "Blueprints", publishToChat: true);
                return;
            }

            var target = Path.Combine(BlueprintExportDir, safeNewName + ".blueprint");
            if (File.Exists(target))
            {
                Notify($"A blueprint named {Path.GetFileName(target)} already exists.", NotificationLevel.Warning, 4.6f, "Blueprints", publishToChat: true);
                return;
            }

            File.Move(source, target);
            Notify($"Renamed {Path.GetFileName(source)} to {Path.GetFileName(target)}.", NotificationLevel.Success, 4f, "Blueprints", publishToChat: true);
            PublishMbInfo($"Path: {target}");
            RefreshBlueprintCommandCatalog();
        }
        catch (Exception ex)
        {
            Notify($"Failed to rename blueprint: {ex.Message}", NotificationLevel.Warning, 5f, "Blueprints", publishToChat: true);
        }
    }

    private void RequestDeleteBlueprint(string currentFileName)
    {
        try
        {
            var source = ResolveBlueprintFilePath(currentFileName);
            if (source == null)
            {
                Notify($"Blueprint file not found: {currentFileName}", NotificationLevel.Warning, 4.6f, "Blueprints", publishToChat: true);
                return;
            }

            ClearPendingConfirmAction();
            _pendingConfirmAction = PendingConfirmAction.BlueprintDelete;
            _pendingDeleteBlueprintPath = source;
            _pendingDeleteBlueprintName = Path.GetFileName(source);
            Notify($"Delete {_pendingDeleteBlueprintName}? Type /mb confirm to delete it.", NotificationLevel.Warning, 5f, "Blueprints", publishToChat: true);
        }
        catch (Exception ex)
        {
            Notify($"Failed to prepare delete: {ex.Message}", NotificationLevel.Warning, 5f, "Blueprints", publishToChat: true);
        }
    }

    private void ConfirmPendingBlueprintDelete()
    {
        if (_pendingConfirmAction == PendingConfirmAction.SelectionRemove)
        {
            ConfirmPendingSelectionRemoval();
            return;
        }

        if (_pendingConfirmAction != PendingConfirmAction.BlueprintDelete ||
            string.IsNullOrWhiteSpace(_pendingDeleteBlueprintPath) ||
            string.IsNullOrWhiteSpace(_pendingDeleteBlueprintName))
        {
            Notify("There is no pending confirmed action.", NotificationLevel.Warning, 4.2f, "Blueprints", publishToChat: true);
            return;
        }

        try
        {
            if (!File.Exists(_pendingDeleteBlueprintPath))
            {
                Notify($"Pending blueprint no longer exists: {_pendingDeleteBlueprintName}", NotificationLevel.Warning, 4.6f, "Blueprints", publishToChat: true);
                ClearPendingConfirmAction();
                return;
            }

            File.Delete(_pendingDeleteBlueprintPath);
            Notify($"Deleted {_pendingDeleteBlueprintName}.", NotificationLevel.Success, 4f, "Blueprints", publishToChat: true);
            RefreshBlueprintCommandCatalog();
        }
        catch (Exception ex)
        {
            Notify($"Failed to delete blueprint: {ex.Message}", NotificationLevel.Warning, 5f, "Blueprints", publishToChat: true);
        }
        finally
        {
            ClearPendingConfirmAction();
        }
    }

    private static BlueprintExportEntry ToBlueprintEntry(ClipboardEntry entry, Vector3 position)
    {
        return new BlueprintExportEntry
        {
            SavableObjectId = entry.SavableObjectID.ToString(),
            SavableObjectIdValue = ToBlueprintIdValue(entry.SavableObjectID),
            RequiredSavableObjectId = entry.RequiredSavableObjectID.ToString(),
            RequiredSavableObjectIdValue = ToBlueprintIdValue(entry.RequiredSavableObjectID),
            Position = ToBlueprintVector(position),
            Rotation = ToBlueprintQuaternion(entry.Rotation),
            SupportsEnabled = entry.SupportsEnabled,
            CustomData = entry.CustomData ?? string.Empty,
            Label = entry.Label ?? string.Empty
        };
    }

    private ClipboardData? BuildClipboardFromImport(BlueprintExportFile file)
    {
        if (file.Entries == null || file.Entries.Count == 0)
        {
            return null;
        }

        var mode = (file.ExportMode ?? string.Empty).Trim();
        var clipboard = new ClipboardData();
        var anchor = ResolveImportAnchor(file);
        clipboard.CopyAnchor = anchor;
        clipboard.PlayerPosition = file.PlayerPosition != null
            ? FromBlueprintVector(file.PlayerPosition)
            : anchor;

        for (var i = 0; i < file.Entries.Count; i++)
        {
            var item = file.Entries[i];
            var relativeOffset = string.Equals(mode, "selection", StringComparison.OrdinalIgnoreCase)
                ? FromBlueprintVector(item.Position)
                : FromBlueprintVector(item.Position) - anchor;

            clipboard.Entries.Add(new ClipboardEntry
            {
                SavableObjectID = ParseBlueprintId(item.SavableObjectIdValue, item.SavableObjectId),
                RequiredSavableObjectID = ParseBlueprintId(item.RequiredSavableObjectIdValue, item.RequiredSavableObjectId),
                RelativeOffset = relativeOffset,
                Rotation = FromBlueprintQuaternion(item.Rotation),
                SupportsEnabled = item.SupportsEnabled,
                CustomData = item.CustomData ?? string.Empty,
                Label = item.Label ?? string.Empty
            });
        }

        return clipboard;
    }

    private static BlueprintVector3 ToBlueprintVector(Vector3 value)
    {
        return new BlueprintVector3
        {
            X = value.x,
            Y = value.y,
            Z = value.z
        };
    }

    private static Vector3 FromBlueprintVector(BlueprintVector3? value)
    {
        if (value == null)
        {
            return Vector3.zero;
        }

        return new Vector3(value.X, value.Y, value.Z);
    }

    private static BlueprintQuaternion ToBlueprintQuaternion(Quaternion value)
    {
        return new BlueprintQuaternion
        {
            X = value.x,
            Y = value.y,
            Z = value.z,
            W = value.w
        };
    }

    private static Quaternion FromBlueprintQuaternion(BlueprintQuaternion? value)
    {
        if (value == null)
        {
            return Quaternion.identity;
        }

        return new Quaternion(value.X, value.Y, value.Z, value.W);
    }

    private static int ToBlueprintIdValue(SavableObjectID id)
    {
        try
        {
            return Convert.ToInt32(id);
        }
        catch
        {
            return 0;
        }
    }

    private static SavableObjectID ParseBlueprintId(int numericValue, string? textValue)
    {
        try
        {
            return (SavableObjectID)numericValue;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(textValue) &&
                Enum.TryParse(textValue, ignoreCase: true, out SavableObjectID parsed))
            {
                return parsed;
            }

            return SavableObjectID.INVALID;
        }
    }

    private static string SanitizeBlueprintFileName(string requestedFileName)
    {
        var value = (requestedFileName ?? string.Empty).Trim();
        if (value.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(0, value.Length - ".blueprint".Length);
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Trim();
    }

    private static string? ResolveBlueprintFilePath(string requestedFileName)
    {
        var safeName = SanitizeBlueprintFileName(requestedFileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return null;
        }

        var direct = Path.Combine(BlueprintExportDir, safeName);
        if (File.Exists(direct))
        {
            return direct;
        }

        var withExtension = direct.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase)
            ? direct
            : direct + ".blueprint";
        if (File.Exists(withExtension))
        {
            return withExtension;
        }

        return null;
    }

    private static string[] GetBlueprintFileNames()
    {
        try
        {
            Directory.CreateDirectory(BlueprintExportDir);
            return Directory.GetFiles(BlueprintExportDir, "*.blueprint", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static Vector3 ResolveImportAnchor(BlueprintExportFile file)
    {
        if (file.CopyAnchor != null)
        {
            return FromBlueprintVector(file.CopyAnchor);
        }

        if (file.Entries == null || file.Entries.Count == 0)
        {
            return Vector3.zero;
        }

        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var minZ = float.PositiveInfinity;
        for (var i = 0; i < file.Entries.Count; i++)
        {
            var pos = FromBlueprintVector(file.Entries[i].Position);
            minX = Mathf.Min(minX, pos.x);
            minY = Mathf.Min(minY, pos.y);
            minZ = Mathf.Min(minZ, pos.z);
        }

        return new Vector3(minX, minY, minZ);
    }

    private static Vector3 ResolveWorldExportAnchor(IReadOnlyList<BlueprintExportEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return Vector3.zero;
        }

        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var minZ = float.PositiveInfinity;
        for (var i = 0; i < entries.Count; i++)
        {
            var pos = FromBlueprintVector(entries[i].Position);
            minX = Mathf.Min(minX, pos.x);
            minY = Mathf.Min(minY, pos.y);
            minZ = Mathf.Min(minZ, pos.z);
        }

        return new Vector3(minX, minY, minZ);
    }

    private static string PrettyPrintJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        var builder = new StringBuilder(json.Length + 64);
        var inString = false;
        var escape = false;
        var indent = 0;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (escape)
            {
                builder.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                builder.Append(c);
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                builder.Append(c);
                continue;
            }

            if (inString)
            {
                builder.Append(c);
                continue;
            }

            switch (c)
            {
                case '{':
                case '[':
                    builder.Append(c);
                    builder.AppendLine();
                    indent++;
                    builder.Append(new string(' ', indent * 2));
                    break;
                case '}':
                case ']':
                    builder.AppendLine();
                    indent = Math.Max(0, indent - 1);
                    builder.Append(new string(' ', indent * 2));
                    builder.Append(c);
                    break;
                case ',':
                    builder.Append(c);
                    builder.AppendLine();
                    builder.Append(new string(' ', indent * 2));
                    break;
                case ':':
                    builder.Append(": ");
                    break;
                default:
                    if (!char.IsWhiteSpace(c))
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }

        return builder.ToString();
    }
}
