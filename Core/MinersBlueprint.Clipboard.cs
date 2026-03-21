namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private readonly struct PlacementOverlapBox
    {
        public readonly Vector3 Center;
        public readonly Vector3 HalfExtents;
        public readonly Quaternion Rotation;

        public PlacementOverlapBox(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            Center = center;
            HalfExtents = halfExtents;
            Rotation = rotation;
        }
    }

    private void CopySelection()
    {
        if (!TryCreateClipboardFromSelection("Copy", out var clipboard))
        {
            return;
        }

        _clipboard = clipboard;
        _hasPointA = false;
        _hasPointB = false;
        _selectionObjects.Clear();
        EnsureSelectionHighlightBoxCount(0);
        if (_selectionRoot != null && _selectionRoot.activeSelf)
        {
            _selectionRoot.SetActive(false);
        }
        if (_ghostPreviewVisible)
        {
            RebuildGhostPreviewNow();
        }
        Notify($"Copied {clipboard.Entries.Count} objects.", NotificationLevel.Success, title: "Copy");
    }

    private void CutSelection()
    {
        if (!TryCreateClipboardFromSelection("Cut", out var clipboard))
        {
            return;
        }

        var removedCount = 0;
        var skippedCount = 0;
        var undoTransaction = new UndoTransaction { BuildMode = _buildMode };
        var cutClipboard = new ClipboardData
        {
            CopyAnchor = clipboard.CopyAnchor,
            PlayerPosition = clipboard.PlayerPosition
        };

        for (var i = 0; i < _selectionObjects.Count; i++)
        {
            var selected = _selectionObjects[i];
            if (selected == null || selected.Component == null || selected.Saveable == null)
            {
                continue;
            }

            var snapshot = BuildUndoSnapshot(selected);
            if (snapshot == null)
            {
                skippedCount++;
                continue;
            }

            if (!TryCreateClipboardEntry(selected, clipboard.CopyAnchor, out var entry))
            {
                skippedCount++;
                continue;
            }

            var removed = _buildMode == BuildMode.Unlimited
                ? RemoveSelectedObjectUnlimited(selected)
                : TryReturnSelectedObjectToInventory(selected);
            if (!removed)
            {
                skippedCount++;
                continue;
            }

            var instanceId = selected.Component.GetInstanceID();
            if (undoTransaction.RemovedInstanceIds.Add(instanceId))
            {
                undoTransaction.RemovedObjects.Add(snapshot);
            }

            cutClipboard.Entries.Add(entry);
            removedCount++;
        }

        if (removedCount <= 0)
        {
            Notify(_buildMode == BuildMode.Unlimited
                ? "No objects were cut from the selection."
                : "No objects were cut. Items may be unsupported for inventory return or inventory may be full.",
                NotificationLevel.Warning, 5f, "Cut", publishToChat: true);
            return;
        }

        _clipboard = cutClipboard;
        PushUndoTransaction(undoTransaction);
        _hasPointA = false;
        _hasPointB = false;
        _selectionObjects.Clear();
        EnsureSelectionHighlightBoxCount(0);
        if (_selectionRoot != null && _selectionRoot.activeSelf)
        {
            _selectionRoot.SetActive(false);
        }
        if (_ghostPreviewVisible)
        {
            RebuildGhostPreviewNow();
        }

        Notify($"Cut {removedCount} object(s) to the clipboard.", NotificationLevel.Success, 4.2f, "Cut", publishToChat: true);
        if (_buildMode == BuildMode.Normal && skippedCount > 0)
        {
            Notify($"Skipped {skippedCount} object(s) that could not be returned to inventory.", NotificationLevel.Warning, 5f, "Cut", publishToChat: true);
        }
        else if (_buildMode == BuildMode.Unlimited && skippedCount > 0)
        {
            Notify($"Skipped {skippedCount} object(s) due to invalid save/copy data.", NotificationLevel.Warning, 5f, "Cut", publishToChat: true);
        }
    }


    private void PasteClipboard()
    {
        if (_activeLayeredPasteJob != null)
        {
            Notify("Paste is already in progress.", NotificationLevel.Warning, 3.8f, "Paste", publishToChat: true);
            return;
        }

        if (_clipboard == null || _clipboard.Entries.Count == 0)
        {
            Notify("Clipboard is empty. Copy first.", NotificationLevel.Warning, title: "Paste");
            return;
        }

        var confirmFromGhost = _ghostPreviewVisible;
        Vector3 pasteAnchor;
        if (confirmFromGhost)
        {
            pasteAnchor = _ghostPreviewAnchor;
        }
        else
        {
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                Notify("Player not found; cannot paste.", NotificationLevel.Warning, title: "Paste");
                return;
            }
            pasteAnchor = SnapPasteAnchor(player.transform.position);
        }

        var inventory = UnityEngine.Object.FindAnyObjectByType<PlayerInventory>();
        var requiresInventory = _buildMode == BuildMode.Normal;
        if (requiresInventory && inventory == null)
        {
            Notify("Inventory not found; cannot paste.", NotificationLevel.Warning, title: "Paste");
            return;
        }

        Dictionary<SavableObjectID, int>? required = null;
        List<ToolStack>? stacks = null;
        if (requiresInventory)
        {
            required = BuildRequirements(_clipboard.Entries);
            stacks = BuildToolStacks(inventory!, out var available);
            var missing = BuildMissing(required, available);
            if (missing.Count > 0)
            {
                ShowMissingItemsPopup(missing);
                return;
            }
        }

        var saving = Singleton<SavingLoadingManager>.Instance;
        if (saving == null)
        {
            Notify("SavingLoadingManager missing; cannot spawn prefabs.", NotificationLevel.Warning, title: "Paste");
            return;
        }

        if (confirmFromGhost)
        {
            HideGhostPreview(showMessage: false);
        }

        var job = CreateLayeredPasteJob(saving, inventory, stacks, requiresInventory, pasteAnchor);
        if (job == null)
        {
            Notify("Nothing valid to paste.", NotificationLevel.Warning, 4f, "Paste", publishToChat: true);
            return;
        }

        _reportedInventoryFullOnReplace = false;
        _activeLayeredPasteJob = job;
        if (job.Layers.Count > 1)
        {
            Notify($"Starting staged paste on {job.SliceAxisLabel} ({job.Layers.Count} slices at {LayeredPasteDelaySeconds:0.0}s each).", NotificationLevel.Info, 3.2f, "Paste");
        }
        ProcessNextPasteLayer(forceImmediate: true);
    }


    private LayeredPasteJob? CreateLayeredPasteJob(
        SavingLoadingManager saving,
        PlayerInventory? inventory,
        List<ToolStack>? stacks,
        bool requiresInventory,
        Vector3 pasteAnchor)
    {
        if (_clipboard == null || _clipboard.Entries.Count == 0)
        {
            return null;
        }

        var entriesWithCells = new List<(ClipboardEntry Entry, Vector3 TargetPos, Vector3Int Cell)>(_clipboard.Entries.Count);
        var xSlices = new HashSet<int>();
        var ySlices = new HashSet<int>();
        var zSlices = new HashSet<int>();
        for (var i = 0; i < _clipboard.Entries.Count; i++)
        {
            var entry = _clipboard.Entries[i];
            var targetPos = pasteAnchor + entry.RelativeOffset;
            var cell = PositionToPlacementCell(targetPos);
            entriesWithCells.Add((entry, targetPos, cell));
            xSlices.Add(cell.x);
            ySlices.Add(cell.y);
            zSlices.Add(cell.z);
        }

        if (entriesWithCells.Count == 0)
        {
            return null;
        }

        var sliceAxis = 'Y';
        var sliceAxisLabel = "Y";
        var sliceCount = ySlices.Count;
        if (xSlices.Count > sliceCount)
        {
            sliceAxis = 'X';
            sliceAxisLabel = "X";
            sliceCount = xSlices.Count;
        }
        if (zSlices.Count > sliceCount)
        {
            sliceAxis = 'Z';
            sliceAxisLabel = "Z";
        }

        var layersBySlice = new SortedDictionary<int, LayeredPasteLayer>();
        for (var i = 0; i < entriesWithCells.Count; i++)
        {
            var item = entriesWithCells[i];
            var sliceCoordinate = GetSliceCoordinate(item.Cell, sliceAxis);
            if (!layersBySlice.TryGetValue(sliceCoordinate, out var layer))
            {
                layer = new LayeredPasteLayer { SliceCoordinate = sliceCoordinate };
                layersBySlice[sliceCoordinate] = layer;
            }

            layer.Entries.Add(new LayeredPasteEntry
            {
                Entry = item.Entry,
                TargetPos = item.TargetPos
            });
        }

        if (layersBySlice.Count == 0)
        {
            return null;
        }

        var job = new LayeredPasteJob
        {
            Saving = saving,
            Inventory = inventory,
            Stacks = stacks,
            RequiresInventory = requiresInventory,
            SliceAxis = sliceAxis,
            SliceAxisLabel = sliceAxisLabel,
            NextLayerAtTime = Time.unscaledTime,
        };
        job.UndoTransaction.BuildMode = _buildMode;
        foreach (var layer in layersBySlice.Values)
        {
            job.Layers.Add(layer);
        }
        return job;
    }


    private void ProcessNextPasteLayer(bool forceImmediate = false)
    {
        if (_activeLayeredPasteJob == null)
        {
            return;
        }

        var now = Time.unscaledTime;
        if (!forceImmediate && now < _activeLayeredPasteJob.NextLayerAtTime)
        {
            return;
        }

        if (_activeLayeredPasteJob.NextLayerIndex >= _activeLayeredPasteJob.Layers.Count)
        {
            FinishLayeredPaste();
            return;
        }

        var job = _activeLayeredPasteJob;
        var layer = job.Layers[job.NextLayerIndex];
        for (var i = 0; i < layer.Entries.Count; i++)
        {
            var item = layer.Entries[i];
            var entry = item.Entry;
            var clearResult = TryClearOccupiedTarget(job.Saving, entry, item.TargetPos, job.ProtectedPlacedIds, job.UndoTransaction);
            if (clearResult == OccupiedClearResult.Failed)
            {
                job.Blocked++;
                continue;
            }
            if (clearResult == OccupiedClearResult.Cleared)
            {
                job.Replaced++;
            }

            if (!TrySpawnClipboardEntryAtPosition(job.Saving, entry, item.TargetPos, out var go))
            {
                job.SpawnFailed++;
                continue;
            }

            if (TryResolveSpawnedBuilding(go, out var building))
            {
                building.BuildingSupportsEnabled = entry.SupportsEnabled;
                if (!string.IsNullOrWhiteSpace(entry.CustomData))
                {
                    building.LoadFromSave(entry.CustomData);
                }
                building.UpdateSupportsAbove(isDestroyingThis: false);
                job.ProtectedPlacedIds.Add(building.GetInstanceID());
                job.UndoTransaction.PlacedObjects.Add(building.gameObject);
                AddConsumedRequirement(job.ConsumedOnSuccess, entry);
                job.Spawned++;
                continue;
            }

            if (TryResolveSpawnedSaveable(go, out var saveable))
            {
                if (!string.IsNullOrWhiteSpace(entry.CustomData))
                {
                    saveable.LoadFromSave(entry.CustomData);
                }
                job.UndoTransaction.PlacedObjects.Add(go);
                AddConsumedRequirement(job.ConsumedOnSuccess, entry);
                job.Spawned++;
                continue;
            }

            Destroy(go);
            job.SpawnFailed++;
        }

        job.NextLayerIndex++;
        if (job.NextLayerIndex >= job.Layers.Count)
        {
            FinishLayeredPaste();
            return;
        }

        job.NextLayerAtTime = now + LayeredPasteDelaySeconds;
        Notify($"Placed slice {job.NextLayerIndex} of {job.Layers.Count} on {job.SliceAxisLabel}.", NotificationLevel.Info, 1.6f, "Paste");
    }


    private void FinishLayeredPaste()
    {
        if (_activeLayeredPasteJob == null)
        {
            return;
        }

        var job = _activeLayeredPasteJob;
        _activeLayeredPasteJob = null;

        if (job.RequiresInventory && job.ConsumedOnSuccess.Count > 0 && job.Inventory != null && job.Stacks != null)
        {
            ConsumeRequirements(job.Inventory, job.ConsumedOnSuccess, job.Stacks);
        }

        if (job.UndoTransaction.PlacedObjects.Count > 0 || job.UndoTransaction.RemovedObjects.Count > 0)
        {
            PushUndoTransaction(job.UndoTransaction);
        }

        if (job.Spawned > 0)
        {
            Notify($"Pasted {job.Spawned} objects across {job.Layers.Count} {GetSliceLabel(job.Layers.Count)} on {job.SliceAxisLabel}.", NotificationLevel.Success, title: "Paste");
        }
        if (job.Replaced > 0)
        {
            var replaceMessage = job.UndoTransaction.BuildMode == BuildMode.Unlimited
                ? $"Replaced {job.Replaced} existing objects."
                : $"Replaced {job.Replaced} existing objects (refunded to inventory).";
            Notify(replaceMessage, NotificationLevel.Success, 3.6f, "Paste");
        }
        if (job.Blocked > 0)
        {
            Notify($"Skipped {job.Blocked} objects (blocked by nearby objects).", NotificationLevel.Warning, title: "Paste");
        }
        if (job.SpawnFailed > 0)
        {
            Notify($"Skipped {job.SpawnFailed} objects (missing prefab/component or variant mismatch).", NotificationLevel.Warning, title: "Paste");
        }
    }


    private static Dictionary<SavableObjectID, int> BuildRequirements(List<ClipboardEntry> entries)
    {
        var required = new Dictionary<SavableObjectID, int>();
        for (var i = 0; i < entries.Count; i++)
        {
            var id = entries[i].RequiredSavableObjectID != SavableObjectID.INVALID
                ? entries[i].RequiredSavableObjectID
                : entries[i].SavableObjectID;
            if (required.TryGetValue(id, out var count))
            {
                required[id] = count + 1;
            }
            else
            {
                required[id] = 1;
            }
        }
        return required;
    }


    private List<ToolStack> BuildToolStacks(PlayerInventory inventory, out Dictionary<SavableObjectID, int> available)
    {
        available = new Dictionary<SavableObjectID, int>();
        var stacks = new List<ToolStack>();
        for (var i = 0; i < inventory.Items.Count; i++)
        {
            var item = inventory.Items[i];
            if (item is not ToolBuilder tool) continue;
            if (tool.Definition == null) continue;

            var prefab = tool.Definition.GetMainPrefab();
            if (prefab == null) continue;

            var id = prefab.SavableObjectID;
            var quantity = Mathf.Max(0, tool.Quantity);
            if (quantity <= 0) continue;

            stacks.Add(new ToolStack
            {
                SlotIndex = i,
                Tool = tool,
                SavableObjectID = id
            });

            if (available.TryGetValue(id, out var count))
            {
                available[id] = count + quantity;
            }
            else
            {
                available[id] = quantity;
            }

            if (!_nameByIdCache.ContainsKey(id))
            {
                _nameByIdCache[id] = BuildDefinitionName(prefab);
            }
        }
        return stacks;
    }


    private static Dictionary<SavableObjectID, int> BuildMissing(Dictionary<SavableObjectID, int> required, Dictionary<SavableObjectID, int> available)
    {
        var missing = new Dictionary<SavableObjectID, int>();
        foreach (var kv in required)
        {
            available.TryGetValue(kv.Key, out var have);
            if (have < kv.Value)
            {
                missing[kv.Key] = kv.Value - have;
            }
        }
        return missing;
    }


    private void ConsumeRequirements(PlayerInventory inventory, Dictionary<SavableObjectID, int> required, List<ToolStack> stacks)
    {
        foreach (var need in required)
        {
            var remaining = need.Value;
            for (var i = 0; i < stacks.Count && remaining > 0; i++)
            {
                var stack = stacks[i];
                if (stack.SavableObjectID != need.Key) continue;
                if (stack.Tool == null || stack.Tool.Quantity <= 0) continue;

                var taken = Mathf.Min(stack.Tool.Quantity, remaining);
                stack.Tool.Quantity -= taken;
                remaining -= taken;

                if (stack.Tool.Quantity > 0) continue;
                if (inventory.ActiveTool == stack.Tool)
                {
                    inventory.ActiveTool = null;
                }
                inventory.Items[stack.SlotIndex] = null;
                Destroy(stack.Tool.gameObject);
            }
        }

        var updateUi = inventory.GetType().GetMethod("UpdateUI", AnyInstance);
        updateUi?.Invoke(inventory, null);
    }


    private void ShowMissingItemsPopup(Dictionary<SavableObjectID, int> missing)
    {
        var lines = missing
            .OrderBy(k => k.Key.ToString(), StringComparer.Ordinal)
            .Select(k => $"{ResolveName(k.Key)} x{k.Value}")
            .ToArray();

        var body = "Missing items:\n" + string.Join("\n", lines);
        ShowPopup("Paste Blocked", body, 8f, NotificationLevel.Warning);
        Logger.LogWarning($"{ModInfo.LOG_PREFIX} {body.Replace('\n', ' ')}");
    }


    private static string BuildDefinitionName(BuildingObject obj)
    {
        return obj.Definition != null && !string.IsNullOrWhiteSpace(obj.Definition.Name)
            ? obj.Definition.Name
            : obj.name;
    }


    private static string BuildObjectLabel(BuildingObject obj)
    {
        return $"{BuildDefinitionName(obj)} [{obj.SavableObjectID}]";
    }


    private static SavableObjectID ResolveRequiredSavableId(BuildingObject obj)
    {
        if (obj == null || obj.Definition == null) return obj != null ? obj.SavableObjectID : SavableObjectID.INVALID;
        var main = obj.Definition.GetMainPrefab();
        if (main != null && main.SavableObjectID != SavableObjectID.INVALID)
        {
            return main.SavableObjectID;
        }
        return obj.SavableObjectID;
    }

    private bool TryCreateClipboardFromSelection(string actionTitle, out ClipboardData clipboard)
    {
        clipboard = null!;
        if (!_hasPointA || !_hasPointB)
        {
            Notify("Set both selection points first.", NotificationLevel.Warning, title: actionTitle);
            return false;
        }

        RefreshSelection();
        if (_selectionObjects.Count == 0)
        {
            Notify("No saveable world objects found in selection.", NotificationLevel.Warning, title: actionTitle);
            return false;
        }

        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (player == null)
        {
            Notify($"Player not found; cannot {actionTitle.ToLowerInvariant()}.", NotificationLevel.Warning, title: actionTitle);
            return false;
        }

        var copyAnchor = SnapPasteAnchor(player.transform.position);
        clipboard = new ClipboardData
        {
            CopyAnchor = copyAnchor,
            PlayerPosition = player.transform.position
        };

        for (var i = 0; i < _selectionObjects.Count; i++)
        {
            if (TryCreateClipboardEntry(_selectionObjects[i], copyAnchor, out var entry))
            {
                clipboard.Entries.Add(entry);
            }
        }

        if (clipboard.Entries.Count == 0)
        {
            Notify("Selection contains no valid copy targets.", NotificationLevel.Warning, title: actionTitle);
            return false;
        }

        return true;
    }

    private static bool TryCreateClipboardEntry(SelectedWorldObject selected, Vector3 copyAnchor, out ClipboardEntry entry)
    {
        entry = null!;
        if (selected == null || selected.Component == null || selected.Saveable == null)
        {
            return false;
        }

        var savableId = selected.Saveable.GetSavableObjectID();
        if (savableId == SavableObjectID.INVALID)
        {
            return false;
        }

        var building = selected.Building;
        entry = new ClipboardEntry
        {
            SavableObjectID = savableId,
            RequiredSavableObjectID = building != null ? ResolveRequiredSavableId(building) : savableId,
            RelativeOffset = selected.Component.transform.position - copyAnchor,
            Rotation = selected.Component.transform.rotation,
            SupportsEnabled = building != null && building.GetBuildingSupportsEnabled(),
            CustomData = selected.Saveable.GetCustomSaveData() ?? string.Empty,
            Label = selected.Label ?? string.Empty
        };
        return true;
    }

    private static bool RemoveSelectedObjectUnlimited(SelectedWorldObject selected)
    {
        if (selected == null || selected.Component == null)
        {
            return false;
        }

        Destroy(selected.Component.gameObject);
        return true;
    }

    private bool TryReturnSelectedObjectToInventory(SelectedWorldObject selected)
    {
        if (selected == null || selected.Component == null)
        {
            return false;
        }

        if (selected.Building != null)
        {
            return selected.Building.TryAddToInventory();
        }

        var tryAddMethod = selected.Component.GetType().GetMethods(AnyInstance)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "TryAddToInventory", StringComparison.Ordinal)) return false;
                var parameters = method.GetParameters();
                return parameters.Length == 0 || (parameters.Length == 1 && parameters[0].ParameterType == typeof(int));
            });
        if (tryAddMethod == null)
        {
            return false;
        }

        var parameters = tryAddMethod.GetParameters();
        object? result;
        if (parameters.Length == 0)
        {
            result = tryAddMethod.Invoke(selected.Component, null);
        }
        else
        {
            result = tryAddMethod.Invoke(selected.Component, new object[] { ResolveStackQuantity(selected.Component) });
        }

        return result is bool ok && ok;
    }

    private static int ResolveStackQuantity(MonoBehaviour component)
    {
        var quantityField = component.GetType().GetField("Quantity", AnyInstance);
        if (quantityField?.GetValue(component) is int quantity && quantity > 0)
        {
            return quantity;
        }

        var quantityProperty = component.GetType().GetProperty("Quantity", AnyInstance);
        if (quantityProperty?.GetValue(component, null) is int propertyQuantity && propertyQuantity > 0)
        {
            return propertyQuantity;
        }

        return 1;
    }


    private bool TrySpawnClipboardEntry(SavingLoadingManager saving, ClipboardEntry entry, Vector3 playerPos, out GameObject spawned)
    {
        spawned = null!;
        var targetPos = playerPos + entry.RelativeOffset;
        return TrySpawnClipboardEntryAtPosition(saving, entry, targetPos, out spawned);
    }


    private bool TrySpawnClipboardEntryAtPosition(SavingLoadingManager saving, ClipboardEntry entry, Vector3 targetPos, out GameObject spawned)
    {
        spawned = null!;
        var requiredId = entry.RequiredSavableObjectID != SavableObjectID.INVALID
            ? entry.RequiredSavableObjectID
            : entry.SavableObjectID;

        var basePrefab = saving.GetPrefab(requiredId);
        if (basePrefab == null)
        {
            // Fallback: try exact copied ID if base lookup is unavailable.
            basePrefab = saving.GetPrefab(entry.SavableObjectID);
            if (basePrefab == null) return false;
        }

        spawned = Instantiate(basePrefab, targetPos, entry.Rotation);
        if (!spawned.TryGetComponent<BuildingObject>(out var building))
        {
            return true;
        }

        if (entry.SavableObjectID == building.SavableObjectID || building.Definition == null)
        {
            if (entry.SavableObjectID == building.SavableObjectID)
            {
                return true;
            }

            Destroy(spawned);
            spawned = null!;
            return false;
        }

        var prefabs = building.Definition.BuildingPrefabs;
        if (prefabs == null || prefabs.Count <= 1)
        {
            Destroy(spawned);
            spawned = null!;
            return false;
        }

        // Variant reconcile: place base item, then cycle variants until target id is reached.
        for (var step = 0; step < prefabs.Count; step++)
        {
            if (building.SavableObjectID == entry.SavableObjectID)
            {
                return true;
            }

            var currentIndex = prefabs.FindIndex(p => p != null && p.SavableObjectID == building.SavableObjectID);
            if (currentIndex < 0) currentIndex = 0;

            var nextIndex = (currentIndex + 1) % prefabs.Count;
            var nextPrefab = prefabs[nextIndex];
            if (nextPrefab == null || nextPrefab.gameObject == null) break;

            var oldBuilding = building;
            var nextGo = Instantiate(nextPrefab.gameObject, oldBuilding.transform.position, oldBuilding.transform.rotation);
            if (!nextGo.TryGetComponent<BuildingObject>(out var nextBuilding))
            {
                Destroy(nextGo);
                break;
            }

            nextBuilding.Definition = oldBuilding.Definition;
            nextBuilding.BuildingSupportsEnabled = oldBuilding.GetBuildingSupportsEnabled();
            Destroy(oldBuilding.gameObject);

            spawned = nextGo;
            building = nextBuilding;
        }

        if (building.SavableObjectID == entry.SavableObjectID)
        {
            return true;
        }

        Destroy(spawned);
        spawned = null!;
        return false;
    }


    private static void AddConsumedRequirement(Dictionary<SavableObjectID, int> consumed, ClipboardEntry entry)
    {
        var id = entry.RequiredSavableObjectID != SavableObjectID.INVALID
            ? entry.RequiredSavableObjectID
            : entry.SavableObjectID;

        if (consumed.TryGetValue(id, out var existing))
        {
            consumed[id] = existing + 1;
        }
        else
        {
            consumed[id] = 1;
        }
    }


    private static Dictionary<SavableObjectID, int> BuildRequirements(List<UndoObjectSnapshot> entries)
    {
        var required = new Dictionary<SavableObjectID, int>();
        for (var i = 0; i < entries.Count; i++)
        {
            var id = entries[i].RequiredSavableObjectID != SavableObjectID.INVALID
                ? entries[i].RequiredSavableObjectID
                : entries[i].SavableObjectID;
            if (required.TryGetValue(id, out var count))
            {
                required[id] = count + 1;
            }
            else
            {
                required[id] = 1;
            }
        }
        return required;
    }


    private OccupiedClearResult TryClearOccupiedTarget(SavingLoadingManager saving, ClipboardEntry entry, Vector3 targetPos, HashSet<int> protectedPlacedIds, UndoTransaction undoTransaction)
    {
        if (!IsBuildingClipboardEntry(saving, entry))
        {
            return OccupiedClearResult.Empty;
        }

        var clearedAny = false;
        const int maxPasses = 24;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            var obstructing = FindObstructingBuildings(saving, entry, targetPos, protectedPlacedIds);
            var actionable = new List<BuildingObject>(obstructing.Count);
            for (var i = 0; i < obstructing.Count; i++)
            {
                var candidate = obstructing[i];
                if (candidate == null || candidate.IsGhost) continue;
                if (protectedPlacedIds.Contains(candidate.GetInstanceID())) continue;
                actionable.Add(candidate);
            }

            if (actionable.Count == 0)
            {
                return clearedAny ? OccupiedClearResult.Cleared : OccupiedClearResult.Empty;
            }

            var removedThisPass = 0;
            for (var i = 0; i < actionable.Count; i++)
            {
                var existing = actionable[i];
                if (undoTransaction.RemovedInstanceIds.Add(existing.GetInstanceID()))
                {
                    undoTransaction.RemovedObjects.Add(new UndoObjectSnapshot
                    {
                        SavableObjectID = existing.SavableObjectID,
                        RequiredSavableObjectID = ResolveRequiredSavableId(existing),
                        Position = existing.transform.position,
                        Rotation = existing.transform.rotation,
                        SupportsEnabled = existing.GetBuildingSupportsEnabled(),
                        CustomData = existing.GetCustomSaveData() ?? string.Empty,
                        Label = BuildObjectLabel(existing)
                    });
                }

                if (_buildMode == BuildMode.Unlimited)
                {
                    clearedAny = true;
                    removedThisPass++;
                    protectedPlacedIds.Add(existing.GetInstanceID());
                    Destroy(existing.gameObject);
                    continue;
                }

                if (existing.TryAddToInventory())
                {
                    clearedAny = true;
                    removedThisPass++;
                    // TryAddToInventory can complete destruction at end-of-frame, so ignore this id now.
                    protectedPlacedIds.Add(existing.GetInstanceID());
                    continue;
                }

                if (!_reportedInventoryFullOnReplace)
                {
                    _reportedInventoryFullOnReplace = true;
                    Notify("Cannot replace occupied object: inventory is full.", NotificationLevel.Warning, 3.8f, "Paste");
                }
                return OccupiedClearResult.Failed;
            }

            if (removedThisPass == 0)
            {
                return clearedAny ? OccupiedClearResult.Cleared : OccupiedClearResult.Failed;
            }
        }

        var remaining = FindObstructingBuildings(saving, entry, targetPos, protectedPlacedIds);
        for (var i = 0; i < remaining.Count; i++)
        {
            var candidate = remaining[i];
            if (candidate == null || candidate.IsGhost) continue;
            if (protectedPlacedIds.Contains(candidate.GetInstanceID())) continue;

            if (!_reportedInventoryFullOnReplace)
            {
                _reportedInventoryFullOnReplace = true;
            }
            return OccupiedClearResult.Failed;
        }

        return clearedAny ? OccupiedClearResult.Cleared : OccupiedClearResult.Empty;
    }


    private static List<BuildingObject> FindObstructingBuildings(
        SavingLoadingManager saving,
        ClipboardEntry entry,
        Vector3 targetPos,
        HashSet<int> protectedPlacedIds)
    {
        if (!TryGetPrefabBuilding(saving, entry, out _))
        {
            return new List<BuildingObject>();
        }

        if (TryBuildPlacementOverlapBoxesForEntry(saving, entry, targetPos, out var layerMask, out var placementBoxes))
        {
            var found = new HashSet<BuildingObject>();
            for (var i = 0; i < placementBoxes.Count; i++)
            {
                var box = placementBoxes[i];
                var overlaps = Physics.OverlapBox(box.Center, box.HalfExtents, box.Rotation, layerMask, QueryTriggerInteraction.Ignore);
                for (var j = 0; j < overlaps.Length; j++)
                {
                    var col = overlaps[j];
                    if (col == null) continue;
                    var building = col.GetComponentInParent<BuildingObject>();
                    if (building == null || building.IsGhost) continue;
                    if (building.GetComponentInParent<GhostPreviewMarker>() != null) continue;
                    if (protectedPlacedIds.Contains(building.GetInstanceID())) continue;
                    found.Add(building);
                }
            }

            // Placement overlap boxes were built successfully, so this is authoritative.
            return found.ToList();
        }

        return FindObstructingBuildingsByPivotCell(targetPos, protectedPlacedIds);
    }

    private static bool TryBuildPlacementOverlapBoxesForEntry(
        SavingLoadingManager saving,
        ClipboardEntry entry,
        Vector3 targetPos,
        out LayerMask layerMask,
        out List<PlacementOverlapBox> placementBoxes)
    {
        layerMask = default;
        placementBoxes = new List<PlacementOverlapBox>();

        if (!TryGetPrefabBuilding(saving, entry, out var prefabBuilding))
        {
            return false;
        }

        var buildingManager = Singleton<BuildingManager>.Instance;
        if (buildingManager == null)
        {
            return false;
        }

        var canBePlacedInTerrain = prefabBuilding.Definition != null && prefabBuilding.Definition.CanBePlacedInTerrain;
        layerMask = prefabBuilding.PlacementNodeRequirement == PlacementNodeRequirement.None
            ? buildingManager.GetBuildingPlacementLayerMask(canBePlacedInTerrain)
            : buildingManager.CollisionLayersExcludeGround;

        var boxColliders = GetPlacementCheckBoxColliders(prefabBuilding);
        if (boxColliders.Count == 0)
        {
            return false;
        }

        var prefabRoot = prefabBuilding.transform;
        var prefabRootInverse = prefabRoot.worldToLocalMatrix;
        var rootToTarget = Matrix4x4.TRS(targetPos, entry.Rotation, Vector3.one);
        for (var i = 0; i < boxColliders.Count; i++)
        {
            var box = boxColliders[i];
            if (box == null || box.isTrigger) continue;

            var colliderToPrefabRoot = prefabRootInverse * box.transform.localToWorldMatrix;
            var colliderToTargetWorld = rootToTarget * colliderToPrefabRoot;
            if (!TryDecomposeMatrix(colliderToTargetWorld, out _, out var rotation, out var scale))
            {
                continue;
            }

            var center = colliderToTargetWorld.MultiplyPoint3x4(box.center);
            var halfExtents = Vector3.Scale(box.size * 0.5f, AbsVector(scale));
            if (halfExtents.x <= 1e-5f || halfExtents.y <= 1e-5f || halfExtents.z <= 1e-5f)
            {
                continue;
            }

            placementBoxes.Add(new PlacementOverlapBox(center, halfExtents, rotation));
        }

        return placementBoxes.Count > 0;
    }

    private static List<BoxCollider> GetPlacementCheckBoxColliders(BuildingObject prefabBuilding)
    {
        var result = new List<BoxCollider>();
        if (prefabBuilding == null)
        {
            return result;
        }

        if (prefabBuilding.BuildingPlacementColliderObject != null)
        {
            var placementBoxes = prefabBuilding.BuildingPlacementColliderObject.GetComponentsInChildren<BoxCollider>(includeInactive: true);
            for (var i = 0; i < placementBoxes.Length; i++)
            {
                var box = placementBoxes[i];
                if (box == null || box.isTrigger) continue;
                result.Add(box);
            }
        }

        if (result.Count == 0 && prefabBuilding.PhysicalColliderObject != null)
        {
            var physicalBoxes = prefabBuilding.PhysicalColliderObject.GetComponentsInChildren<BoxCollider>(includeInactive: true);
            for (var i = 0; i < physicalBoxes.Length; i++)
            {
                var box = physicalBoxes[i];
                if (box == null || box.isTrigger) continue;
                result.Add(box);
            }
        }

        return result;
    }


    private static List<BuildingObject> FindObstructingBuildingsByPivotCell(Vector3 targetPos, HashSet<int> protectedPlacedIds)
    {
        var targetCell = PositionToPlacementCell(targetPos);
        var found = new List<BuildingObject>();
        var all = UnityEngine.Object.FindObjectsByType<BuildingObject>(FindObjectsSortMode.None);
        for (var i = 0; i < all.Length; i++)
        {
            var building = all[i];
            if (building == null || building.IsGhost) continue;
            if (building.GetComponentInParent<GhostPreviewMarker>() != null) continue;
            if (protectedPlacedIds.Contains(building.GetInstanceID())) continue;
            if (PositionToPlacementCell(building.transform.position) != targetCell) continue;
            found.Add(building);
        }
        return found;
    }

    private static bool TryResolveSpawnedBuilding(GameObject go, out BuildingObject building)
    {
        building = null!;
        if (go == null) return false;
        if (go.TryGetComponent<BuildingObject>(out building) && building != null) return true;
        building = go.GetComponentInChildren<BuildingObject>(includeInactive: true);
        return building != null;
    }


    private static bool TryResolveSpawnedSaveable(GameObject go, out ISaveLoadableObject saveable)
    {
        saveable = null!;
        if (go == null) return false;
        if (go.TryGetComponent<ISaveLoadableObject>(out saveable) && saveable != null) return true;

        var behaviours = go.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is ISaveLoadableObject found)
            {
                saveable = found;
                return true;
            }
        }
        return false;
    }


    private static Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }


    private static bool TryDecomposeMatrix(Matrix4x4 matrix, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = new Vector3(matrix.m03, matrix.m13, matrix.m23);

        var xAxis = new Vector3(matrix.m00, matrix.m10, matrix.m20);
        var yAxis = new Vector3(matrix.m01, matrix.m11, matrix.m21);
        var zAxis = new Vector3(matrix.m02, matrix.m12, matrix.m22);

        scale = new Vector3(xAxis.magnitude, yAxis.magnitude, zAxis.magnitude);
        if (scale.x <= 1e-6f || scale.y <= 1e-6f || scale.z <= 1e-6f)
        {
            rotation = Quaternion.identity;
            return false;
        }

        xAxis /= scale.x;
        yAxis /= scale.y;
        zAxis /= scale.z;
        rotation = Quaternion.LookRotation(zAxis, yAxis);
        return true;
    }


    private static bool TryGetPlacementPrefab(SavingLoadingManager saving, ClipboardEntry entry, out GameObject prefab)
    {
        prefab = saving.GetPrefab(entry.SavableObjectID);
        if (prefab != null) return true;

        var requiredId = entry.RequiredSavableObjectID != SavableObjectID.INVALID
            ? entry.RequiredSavableObjectID
            : entry.SavableObjectID;
        prefab = saving.GetPrefab(requiredId);
        return prefab != null;
    }


    private static bool TryGetPrefabBuilding(SavingLoadingManager saving, ClipboardEntry entry, out BuildingObject building)
    {
        building = null!;
        if (!TryGetPlacementPrefab(saving, entry, out var prefab))
        {
            return false;
        }

        if (prefab.TryGetComponent<BuildingObject>(out building) && building != null)
        {
            return true;
        }

        building = prefab.GetComponentInChildren<BuildingObject>(includeInactive: true);
        return building != null;
    }


    private static bool IsBuildingClipboardEntry(SavingLoadingManager saving, ClipboardEntry entry)
    {
        return TryGetPrefabBuilding(saving, entry, out _);
    }


    private string ResolveName(SavableObjectID id)
    {
        if (_nameByIdCache.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var saving = Singleton<SavingLoadingManager>.Instance;
        if (saving != null)
        {
            if (TryGetPlacementPrefab(saving, new ClipboardEntry
                {
                    SavableObjectID = id,
                    RequiredSavableObjectID = id
                }, out var prefab))
            {
                string resolved;
                if (prefab.TryGetComponent<BuildingObject>(out var building) && building != null)
                {
                    resolved = BuildDefinitionName(building);
                }
                else if (TryGetInteractableName(prefab, out var interactableName))
                {
                    resolved = interactableName;
                }
                else
                {
                    resolved = prefab.name.Replace("(Clone)", string.Empty).Trim();
                }
                _nameByIdCache[id] = resolved;
                return resolved;
            }
        }

        var fallback = id.ToString();
        _nameByIdCache[id] = fallback;
        return fallback;
    }


    private static Vector3 SnapPasteAnchor(Vector3 playerPos)
    {
        return new Vector3(
            SnapToCellCenter(playerPos.x),
            SnapToWhole(playerPos.y),
            SnapToCellCenter(playerPos.z));
    }


    private static float SnapToCellCenter(float value)
    {
        return Mathf.Floor(value) + 0.5f;
    }


    private static float SnapToWhole(float value)
    {
        return Mathf.Floor(value);
    }


    private static Vector3Int PositionToPlacementCell(Vector3 value)
    {
        return new Vector3Int(
            Mathf.FloorToInt(value.x),
            Mathf.FloorToInt(value.y),
            Mathf.FloorToInt(value.z));
    }

    private static int GetSliceCoordinate(Vector3Int cell, char axis)
    {
        switch (axis)
        {
            case 'X':
                return cell.x;
            case 'Z':
                return cell.z;
            default:
                return cell.y;
        }
    }

    private static string GetSliceLabel(int count)
    {
        return count == 1 ? "slice" : "slices";
    }


}
