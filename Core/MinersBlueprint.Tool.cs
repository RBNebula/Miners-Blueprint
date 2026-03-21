namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private const string WandToolMarkerName = "minersblueprint.tool";

    private void GiveSelectionTool()
    {
        var inventory = UnityEngine.Object.FindAnyObjectByType<PlayerInventory>();
        if (inventory == null)
        {
            Notify("Player inventory not found; cannot create the selection tool.", NotificationLevel.Warning, 4.6f, "Tool", publishToChat: true);
            return;
        }

        var saving = Singleton<SavingLoadingManager>.Instance;
        if (saving == null)
        {
            Notify("SavingLoadingManager not found; cannot create the selection tool.", NotificationLevel.Warning, 4.6f, "Tool", publishToChat: true);
            return;
        }

        if (!TryResolveSelectionToolPrefab(saving, out var prefab))
        {
            Notify("Could not find a pickaxe prefab for the selection tool.", NotificationLevel.Warning, 4.6f, "Tool", publishToChat: true);
            return;
        }

        var beforeIds = GetInventoryItemIds(inventory);
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        var spawnPos = player != null ? player.transform.position : Vector3.zero;
        var instance = Instantiate(prefab, spawnPos, Quaternion.identity);

        // Mark the spawned pickup first so the tag survives if the same object gets moved into inventory.
        MarkSelectionTool(instance);

        if (!TryInvokeAddToInventory(instance, out var addedComponent))
        {
            Destroy(instance);
            Notify("Failed to add the selection tool to inventory.", NotificationLevel.Warning, 4.6f, "Tool", publishToChat: true);
            return;
        }

        var tagged = ResolveTaggedInventoryTool(inventory, beforeIds, addedComponent);
        if (tagged == null)
        {
            Notify("A pickaxe was added, but it could not be marked as the MineMogul tool. Try again with a free slot or without stacking pickaxes.", NotificationLevel.Warning, 6f, "Tool", publishToChat: true);
            return;
        }

        MarkSelectionTool(tagged.gameObject);
        Notify("Added a MineMogul selection tool. Hold that tagged pickaxe and use left/right click for pos 1/2 and middle click to copy.", NotificationLevel.Success, 5f, "Tool", publishToChat: true);
    }

    private void UpdateSelectionToolInput()
    {
        if (!TryGetActiveSelectionTool(out _))
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            SetStartPoint();
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            SetEndPoint();
            return;
        }

        if (Input.GetMouseButtonDown(2))
        {
            CopySelection();
        }
    }

    private bool TryGetActiveSelectionTool(out Component tool)
    {
        tool = null!;
        var inventory = UnityEngine.Object.FindAnyObjectByType<PlayerInventory>();
        if (inventory == null || inventory.ActiveTool == null)
        {
            return false;
        }

        if (inventory.ActiveTool is not Component activeTool)
        {
            return false;
        }

        if (activeTool.GetComponentInChildren<SelectionToolMarker>(includeInactive: true) == null &&
            activeTool.GetComponent<SelectionToolMarker>() == null)
        {
            return false;
        }

        tool = activeTool;
        return true;
    }

    private static void MarkSelectionTool(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        if (root.GetComponentInChildren<SelectionToolMarker>(includeInactive: true) != null)
        {
            return;
        }

        var markerRoot = new GameObject(WandToolMarkerName);
        markerRoot.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
        markerRoot.transform.SetParent(root.transform, worldPositionStays: false);
        markerRoot.AddComponent<SelectionToolMarker>();
    }

    private static HashSet<int> GetInventoryItemIds(PlayerInventory inventory)
    {
        var ids = new HashSet<int>();
        if (inventory == null)
        {
            return ids;
        }

        for (var i = 0; i < inventory.Items.Count; i++)
        {
            if (inventory.Items[i] is Component item)
            {
                ids.Add(item.GetInstanceID());
            }
        }

        return ids;
    }

    private static Component? ResolveTaggedInventoryTool(PlayerInventory inventory, HashSet<int> beforeIds, Component? addedComponent)
    {
        if (addedComponent != null && addedComponent.GetComponentInChildren<SelectionToolMarker>(includeInactive: true) != null)
        {
            return addedComponent;
        }

        Component? newMatch = null;
        for (var i = 0; i < inventory.Items.Count; i++)
        {
            if (inventory.Items[i] is not Component item)
            {
                continue;
            }

            if (item.GetComponentInChildren<SelectionToolMarker>(includeInactive: true) == null &&
                item.GetComponent<SelectionToolMarker>() == null)
            {
                continue;
            }

            if (!beforeIds.Contains(item.GetInstanceID()))
            {
                return item;
            }

            newMatch ??= item;
        }

        return null;
    }

    private static bool TryInvokeAddToInventory(GameObject instance, out Component? addedComponent)
    {
        addedComponent = null;
        if (instance == null)
        {
            return false;
        }

        var behaviours = instance.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            var method = behaviour.GetType().GetMethods(AnyInstance)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, "TryAddToInventory", StringComparison.Ordinal)) return false;
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 0 || (parameters.Length == 1 && parameters[0].ParameterType == typeof(int));
                });
            if (method == null)
            {
                continue;
            }

            object? result;
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                result = method.Invoke(behaviour, null);
            }
            else
            {
                result = method.Invoke(behaviour, new object[] { ResolveStackQuantity(behaviour) });
            }

            if (result is bool ok && ok)
            {
                addedComponent = behaviour;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveSelectionToolPrefab(SavingLoadingManager saving, out GameObject prefab)
    {
        prefab = null!;
        foreach (SavableObjectID id in Enum.GetValues(typeof(SavableObjectID)))
        {
            if (id == SavableObjectID.INVALID)
            {
                continue;
            }

            var candidate = saving.GetPrefab(id);
            if (candidate == null)
            {
                continue;
            }

            if (candidate.GetComponentInChildren<ToolPickaxe>(includeInactive: true) != null)
            {
                prefab = candidate;
                return true;
            }

            var name = candidate.name ?? string.Empty;
            if (name.IndexOf("pickaxe", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefab = candidate;
                return true;
            }
        }

        return false;
    }
}
