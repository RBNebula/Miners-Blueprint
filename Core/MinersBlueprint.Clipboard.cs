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
        if (!_hasPointA || !_hasPointB)
        {
            _toasts.Push("Set both selection points first.", ToastType.Warning);
            return;
        }

        RefreshSelection();
        if (_selectionObjects.Count == 0)
        {
            _toasts.Push("No building objects found in selection.", ToastType.Warning);
            return;
        }

        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (player == null)
        {
            _toasts.Push("Player not found; cannot copy.", ToastType.Warning);
            return;
        }
        var copyAnchor = SnapPasteAnchor(player.transform.position);

        var clipboard = new ClipboardData();
        for (var i = 0; i < _selectionObjects.Count; i++)
        {
            var obj = _selectionObjects[i];
            if (obj == null || obj.Definition == null || obj.IsGhost) continue;

            clipboard.Entries.Add(new ClipboardEntry
            {
                SavableObjectID = obj.SavableObjectID,
                RequiredSavableObjectID = ResolveRequiredSavableId(obj),
                RelativeOffset = obj.transform.position - copyAnchor,
                Rotation = obj.transform.rotation,
                SupportsEnabled = obj.GetBuildingSupportsEnabled(),
                CustomData = obj.GetCustomSaveData() ?? string.Empty,
                Label = BuildObjectLabel(obj)
            });
        }

        if (clipboard.Entries.Count == 0)
        {
            _toasts.Push("Selection contains no valid copy targets.", ToastType.Warning);
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
        if (_ghostPreviewVisible)
        {
            RebuildGhostPreviewNow();
        }
        _toasts.Push($"Copied {clipboard.Entries.Count} objects.", ToastType.Success);
    }


    private void PasteClipboard()
    {
        if (_clipboard == null || _clipboard.Entries.Count == 0)
        {
            _toasts.Push("Clipboard is empty. Copy first.", ToastType.Warning);
            return;
        }

        Vector3 pasteAnchor;
        if (_ghostPreviewVisible)
        {
            pasteAnchor = _ghostPreviewAnchor;
            HideGhostPreview();
        }
        else
        {
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                _toasts.Push("Player not found; cannot paste.", ToastType.Warning);
                return;
            }
            pasteAnchor = SnapPasteAnchor(player.transform.position);
        }

        var inventory = UnityEngine.Object.FindAnyObjectByType<PlayerInventory>();
        if (inventory == null)
        {
            _toasts.Push("Inventory not found; cannot paste.", ToastType.Warning);
            return;
        }

        var required = BuildRequirements(_clipboard.Entries);
        var stacks = BuildToolStacks(inventory, out var available);
        var missing = BuildMissing(required, available);
        if (missing.Count > 0)
        {
            ShowMissingItemsPopup(missing);
            _toasts.Push("Paste blocked: missing inventory items.", ToastType.Warning);
            return;
        }

        var saving = Singleton<SavingLoadingManager>.Instance;
        if (saving == null)
        {
            _toasts.Push("SavingLoadingManager missing; cannot spawn prefabs.", ToastType.Warning);
            return;
        }

        var spawned = 0;
        var blocked = 0;
        var spawnFailed = 0;
        var replaced = 0;
        var protectedPlacedIds = new HashSet<int>();
        var consumedOnSuccess = new Dictionary<SavableObjectID, int>();
        _reportedInventoryFullOnReplace = false;
        for (var i = 0; i < _clipboard.Entries.Count; i++)
        {
            var entry = _clipboard.Entries[i];
            var targetPos = pasteAnchor + entry.RelativeOffset;
            var clearResult = TryClearOccupiedTarget(saving, entry, targetPos, protectedPlacedIds);
            if (clearResult == OccupiedClearResult.Failed)
            {
                blocked++;
                continue;
            }
            if (clearResult == OccupiedClearResult.Cleared)
            {
                replaced++;
            }

            if (!TrySpawnClipboardEntry(saving, entry, pasteAnchor, out var go))
            {
                spawnFailed++;
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
                protectedPlacedIds.Add(building.GetInstanceID());
                AddConsumedRequirement(consumedOnSuccess, entry);
                spawned++;
                continue;
            }

            if (TryResolveSpawnedSaveable(go, out var saveable))
            {
                if (!string.IsNullOrWhiteSpace(entry.CustomData))
                {
                    saveable.LoadFromSave(entry.CustomData);
                }
                AddConsumedRequirement(consumedOnSuccess, entry);
                spawned++;
                continue;
            }

            Destroy(go);
            spawnFailed++;
        }

        if (consumedOnSuccess.Count > 0)
        {
            ConsumeRequirements(inventory, consumedOnSuccess, stacks);
        }

        if (spawned > 0)
        {
            _toasts.Push($"Pasted {spawned} objects.", ToastType.Success);
        }
        if (replaced > 0)
        {
            _toasts.Push($"Replaced {replaced} existing objects (refunded to inventory).", ToastType.Success, duration: 3.6f);
        }
        if (blocked > 0)
        {
            _toasts.Push($"Skipped {blocked} objects (blocked by nearby objects).", ToastType.Warning);
        }
        if (spawnFailed > 0)
        {
            _toasts.Push($"Skipped {spawnFailed} objects (missing prefab/component).", ToastType.Warning);
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
        ShowPopup("Paste Blocked", body, 8f);
        _toasts.Push(body, ToastType.Warning, duration: 4.5f);
    }


    private void ShowPopup(string title, string body, float duration)
    {
        _popupTitle = title ?? string.Empty;
        _popupBody = body ?? string.Empty;
        _popupUntilTime = Time.unscaledTime + Mathf.Max(1.5f, duration);
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


    private bool TrySpawnClipboardEntry(SavingLoadingManager saving, ClipboardEntry entry, Vector3 playerPos, out GameObject spawned)
    {
        spawned = null!;
        var targetPos = playerPos + entry.RelativeOffset;
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
            return true;
        }

        var prefabs = building.Definition.BuildingPrefabs;
        if (prefabs == null || prefabs.Count <= 1)
        {
            return true;
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

        return true;
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


    private OccupiedClearResult TryClearOccupiedTarget(SavingLoadingManager saving, ClipboardEntry entry, Vector3 targetPos, HashSet<int> protectedPlacedIds)
    {
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
                    _toasts.Push("Cannot replace occupied object: inventory is full.", ToastType.Warning, duration: 3.8f);
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

        if (!TryGetPlacementPrefab(saving, entry, out var prefab))
        {
            return false;
        }
        if (!prefab.TryGetComponent<BuildingObject>(out var prefabBuilding) || prefabBuilding == null)
        {
            prefabBuilding = prefab.GetComponentInChildren<BuildingObject>(includeInactive: true);
            if (prefabBuilding == null)
            {
                return false;
            }
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


    private string ResolveName(SavableObjectID id)
    {
        if (_nameByIdCache.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var saving = Singleton<SavingLoadingManager>.Instance;
        if (saving != null)
        {
            var prefab = saving.GetPrefab(id);
            if (prefab != null && prefab.TryGetComponent<BuildingObject>(out var building))
            {
                var resolved = BuildDefinitionName(building);
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


}
