namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private void PushUndoTransaction(UndoTransaction transaction)
    {
        if (transaction.PlacedObjects.Count == 0 && transaction.RemovedObjects.Count == 0)
        {
            return;
        }

        _undoHistory.Push(transaction);
        while (_undoHistory.Count > MaxUndoHistoryEntries)
        {
            TrimOldestUndoTransaction();
        }
    }

    private void TrimOldestUndoTransaction()
    {
        if (_undoHistory.Count <= MaxUndoHistoryEntries)
        {
            return;
        }

        var retained = _undoHistory.Reverse().Skip(1).ToArray();
        _undoHistory.Clear();
        for (var i = retained.Length - 1; i >= 0; i--)
        {
            _undoHistory.Push(retained[i]);
        }
    }

    private void UndoLastPaste(int count = 1)
    {
        if (_activeLayeredPasteJob != null)
        {
            Notify("Wait for the current layered paste to finish before undoing.", NotificationLevel.Warning, 4.2f, "Undo", publishToChat: true);
            return;
        }

        if (count <= 0)
        {
            Notify("Undo count must be a positive whole number.", NotificationLevel.Warning, 4.2f, "Undo", publishToChat: true);
            return;
        }

        if (_undoHistory.Count == 0)
        {
            Notify("There is no action to undo.", NotificationLevel.Warning, 4f, "Undo", publishToChat: true);
            return;
        }

        var saving = Singleton<SavingLoadingManager>.Instance;
        if (saving == null)
        {
            Notify("SavingLoadingManager missing; cannot undo the last action.", NotificationLevel.Warning, 4.6f, "Undo", publishToChat: true);
            return;
        }

        var inventory = UnityEngine.Object.FindAnyObjectByType<PlayerInventory>();
        var requested = count;
        var executed = 0;
        var totalRestored = 0;
        var totalRestoreFailed = 0;
        var totalRefunded = 0;
        var totalDestroyed = 0;
        var totalRefundFailed = 0;

        while (executed < requested && _undoHistory.Count > 0)
        {
            var result = UndoSingleTransaction(_undoHistory.Pop(), saving, inventory);
            totalRestored += result.Restored;
            totalRestoreFailed += result.RestoreFailed;
            totalRefunded += result.Refunded;
            totalDestroyed += result.Destroyed;
            totalRefundFailed += result.RefundFailed;
            executed++;
        }

        if (totalRestored > 0)
        {
            Notify($"Undo restored {totalRestored} removed/replaced object(s).", NotificationLevel.Success, 4f, "Undo", publishToChat: true);
        }
        if (totalRefunded > 0)
        {
            Notify($"Undo refunded {totalRefunded} pasted object(s) back into inventory.", NotificationLevel.Success, 4f, "Undo");
        }
        if (totalDestroyed > 0)
        {
            Notify($"Undo removed {totalDestroyed} pasted object(s) directly.", NotificationLevel.Success, 4f, "Undo");
        }
        if (totalRefundFailed > 0)
        {
            Notify($"Undo could not refund {totalRefundFailed} pasted object(s) because inventory was full.", NotificationLevel.Warning, 4.6f, "Undo", publishToChat: true);
        }
        if (totalRestoreFailed > 0)
        {
            Notify($"Undo failed to restore {totalRestoreFailed} replaced object(s).", NotificationLevel.Warning, 4.6f, "Undo", publishToChat: true);
        }
        if (requested > 1)
        {
            if (executed == requested)
            {
                Notify($"Undo completed {executed} step(s).", NotificationLevel.Success, 3.8f, "Undo", publishToChat: true);
            }
            else
            {
                Notify($"Undo completed {executed} of {requested} requested step(s); no more undo history is available.", NotificationLevel.Warning, 4.8f, "Undo", publishToChat: true);
            }
        }
    }

    private UndoExecutionResult UndoSingleTransaction(UndoTransaction transaction, SavingLoadingManager saving, PlayerInventory? inventory)
    {
        var refunded = 0;
        var destroyed = 0;
        var refundFailed = 0;
        for (var i = 0; i < transaction.PlacedObjects.Count; i++)
        {
            var go = transaction.PlacedObjects[i];
            if (go == null) continue;

            if (transaction.BuildMode == BuildMode.Normal && TryResolveSpawnedBuilding(go, out var building) && building != null)
            {
                if (building.TryAddToInventory())
                {
                    refunded++;
                    continue;
                }
            }

            Destroy(go);
            destroyed++;
            if (transaction.BuildMode == BuildMode.Normal)
            {
                refundFailed++;
            }
        }

        if (transaction.BuildMode == BuildMode.Normal && inventory != null && transaction.RemovedObjects.Count > 0)
        {
            var restoreRequirements = BuildRequirements(transaction.RemovedObjects);
            var stacks = BuildToolStacks(inventory, out _);
            ConsumeRequirements(inventory, restoreRequirements, stacks);
        }

        var restored = 0;
        var restoreFailed = 0;
        for (var i = 0; i < transaction.RemovedObjects.Count; i++)
        {
            var snapshot = transaction.RemovedObjects[i];
            var entry = new ClipboardEntry
            {
                SavableObjectID = snapshot.SavableObjectID,
                RequiredSavableObjectID = snapshot.RequiredSavableObjectID,
                RelativeOffset = snapshot.Position,
                Rotation = snapshot.Rotation,
                SupportsEnabled = snapshot.SupportsEnabled,
                CustomData = snapshot.CustomData,
                Label = snapshot.Label
            };

            if (!TrySpawnClipboardEntry(saving, entry, Vector3.zero, out var go))
            {
                restoreFailed++;
                continue;
            }

            if (TryResolveSpawnedBuilding(go, out var building))
            {
                building.BuildingSupportsEnabled = snapshot.SupportsEnabled;
                if (!string.IsNullOrWhiteSpace(snapshot.CustomData))
                {
                    building.LoadFromSave(snapshot.CustomData);
                }
                building.UpdateSupportsAbove(isDestroyingThis: false);
                restored++;
                continue;
            }

            if (TryResolveSpawnedSaveable(go, out var saveable))
            {
                if (!string.IsNullOrWhiteSpace(snapshot.CustomData))
                {
                    saveable.LoadFromSave(snapshot.CustomData);
                }
                restored++;
                continue;
            }

            Destroy(go);
            restoreFailed++;
        }

        return new UndoExecutionResult(restored, restoreFailed, refunded, destroyed, refundFailed);
    }
}
