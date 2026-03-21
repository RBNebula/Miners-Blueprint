namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private void RemoveSelectionObjects()
    {
        if (!_hasPointA || !_hasPointB)
        {
            Notify("Set both selection points first.", NotificationLevel.Warning, 4f, "Remove", publishToChat: true);
            return;
        }

        RefreshSelection();
        if (_selectionObjects.Count == 0)
        {
            Notify("No saveable world objects were found in the current selection.", NotificationLevel.Warning, 4.6f, "Remove", publishToChat: true);
            return;
        }

        ClearPendingConfirmAction();

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
                continue;
            }

            if (_pendingSelectionRemovalTargets.Any(target => target.Component == selected.Component))
            {
                continue;
            }

            _pendingSelectionRemovalTargets.Add(new PendingSelectionRemovalTarget
            {
                Component = selected.Component,
                Snapshot = snapshot
            });
        }

        _pendingSelectionRemovalCount = _pendingSelectionRemovalTargets.Count;
        if (_pendingSelectionRemovalCount <= 0)
        {
            Notify("Selection remove did not find any removable objects.", NotificationLevel.Warning, 4.6f, "Remove", publishToChat: true);
            return;
        }

        _pendingConfirmAction = PendingConfirmAction.SelectionRemove;
        Notify($"You are about to remove {_pendingSelectionRemovalCount} item(s). Type /mb confirm to confirm this action.", NotificationLevel.Warning, 5f, "Remove", publishToChat: true);
    }

    private void ConfirmPendingSelectionRemoval()
    {
        if (_pendingConfirmAction != PendingConfirmAction.SelectionRemove || _pendingSelectionRemovalTargets.Count == 0)
        {
            Notify("There is no pending selection removal to confirm.", NotificationLevel.Warning, 4.2f, "Remove", publishToChat: true);
            return;
        }

        var undoTransaction = new UndoTransaction
        {
            BuildMode = BuildMode.Unlimited
        };

        var removed = 0;
        for (var i = 0; i < _pendingSelectionRemovalTargets.Count; i++)
        {
            var target = _pendingSelectionRemovalTargets[i];
            if (target == null || target.Component == null || target.Snapshot == null)
            {
                continue;
            }

            var instanceId = target.Component.GetInstanceID();
            if (!undoTransaction.RemovedInstanceIds.Add(instanceId))
            {
                continue;
            }

            undoTransaction.RemovedObjects.Add(target.Snapshot);
            Destroy(target.Component.gameObject);
            removed++;
        }

        if (removed > 0)
        {
            PushUndoTransaction(undoTransaction);
        }
        ClearPendingConfirmAction();
        RefreshSelection();

        if (removed <= 0)
        {
            Notify("No queued selection objects were still available to remove.", NotificationLevel.Warning, 4.6f, "Remove", publishToChat: true);
            return;
        }

        Notify($"Removed {removed} object(s) from the selection.", NotificationLevel.Success, 4f, "Remove", publishToChat: true);
        Notify("Use /mb undo to restore them.", NotificationLevel.Info, 4f, "Remove");
    }

    private void ClearPendingConfirmAction()
    {
        _pendingConfirmAction = PendingConfirmAction.None;
        _pendingDeleteBlueprintPath = null;
        _pendingDeleteBlueprintName = null;
        _pendingSelectionRemovalTargets.Clear();
        _pendingSelectionRemovalCount = 0;
    }

    private static UndoObjectSnapshot? BuildUndoSnapshot(SelectedWorldObject selected)
    {
        if (selected == null || selected.Component == null || selected.Saveable == null)
        {
            return null;
        }

        var building = selected.Building;
        var requiredSavableId = building != null
            ? ResolveRequiredSavableId(building)
            : selected.SavableObjectID;

        return new UndoObjectSnapshot
        {
            SavableObjectID = selected.SavableObjectID,
            RequiredSavableObjectID = requiredSavableId,
            Position = selected.Component.transform.position,
            Rotation = selected.Component.transform.rotation,
            SupportsEnabled = building != null && building.GetBuildingSupportsEnabled(),
            CustomData = selected.Saveable.GetCustomSaveData() ?? string.Empty,
            Label = selected.Label ?? string.Empty
        };
    }
}
