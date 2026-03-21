namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private void RotateClipboard(int degrees)
    {
        if (!TryGetTransformableClipboard(out var clipboard))
        {
            return;
        }

        var normalizedDegrees = NormalizeRightAngleDegrees(degrees);
        if (normalizedDegrees == 0)
        {
            Notify("Rotation must be 90, 180, or 270 degrees.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        var yaw = Quaternion.Euler(0f, normalizedDegrees, 0f);
        for (var i = 0; i < clipboard.Entries.Count; i++)
        {
            var entry = clipboard.Entries[i];
            entry.RelativeOffset = RoundClipboardOffset(yaw * entry.RelativeOffset);
            entry.Rotation = NormalizeQuaternion(yaw * entry.Rotation);
        }

        OnClipboardTransformed($"Rotated clipboard {normalizedDegrees} degrees.");
    }

    private void MirrorClipboard()
    {
        if (!TryGetTransformableClipboard(out var clipboard))
        {
            return;
        }

        for (var i = 0; i < clipboard.Entries.Count; i++)
        {
            var entry = clipboard.Entries[i];
            entry.RelativeOffset = MirrorOffsetAcrossLocalX(entry.RelativeOffset);
            entry.Rotation = MirrorRotationAcrossLocalX(entry.Rotation);
        }

        OnClipboardTransformed("Mirrored clipboard across local X.");
    }

    private void StackClipboard(int count, string rawDirection, int gap)
    {
        if (!TryGetTransformableClipboard(out var clipboard))
        {
            return;
        }

        if (!TryResolveStackDirection(rawDirection, out var direction, out var directionLabel))
        {
            Notify("Stack direction must be north, south, east, west, up, or down.", NotificationLevel.Warning, 4.6f, publishToChat: true);
            return;
        }

        if (!TryComputeClipboardBounds(clipboard, out var min, out var max))
        {
            Notify("Clipboard is empty. Copy or import something first.", NotificationLevel.Warning, 4.6f, title: "Clipboard", publishToChat: true);
            return;
        }

        var stepDistance = ResolveStackStepDistance(direction, min, max, gap);
        var baseEntries = clipboard.Entries
            .Select(entry => CloneClipboardEntry(entry))
            .ToArray();

        for (var copyIndex = 1; copyIndex <= count; copyIndex++)
        {
            var stepOffset = RoundClipboardOffset(direction * (stepDistance * copyIndex));
            for (var i = 0; i < baseEntries.Length; i++)
            {
                var source = baseEntries[i];
                clipboard.Entries.Add(new ClipboardEntry
                {
                    SavableObjectID = source.SavableObjectID,
                    RequiredSavableObjectID = source.RequiredSavableObjectID,
                    RelativeOffset = RoundClipboardOffset(source.RelativeOffset + stepOffset),
                    Rotation = source.Rotation,
                    SupportsEnabled = source.SupportsEnabled,
                    CustomData = source.CustomData,
                    Label = source.Label
                });
            }
        }

        OnClipboardTransformed($"Stacked clipboard {count} extra time(s) {directionLabel} with gap {gap}.");
    }

    private bool TryGetTransformableClipboard(out ClipboardData clipboard)
    {
        clipboard = _clipboard!;
        if (_clipboard == null || _clipboard.Entries.Count == 0)
        {
            Notify("Clipboard is empty. Copy or import something first.", NotificationLevel.Warning, 4.6f, title: "Clipboard", publishToChat: true);
            return false;
        }

        clipboard = _clipboard;
        return true;
    }

    private void OnClipboardTransformed(string message)
    {
        if (_ghostPreviewVisible)
        {
            RebuildGhostPreviewNow();
            Notify($"{message} Ghost preview updated.", NotificationLevel.Success, title: "Clipboard", publishToChat: true);
            return;
        }

        Notify($"{message} Use /mb paste to preview it.", NotificationLevel.Success, title: "Clipboard", publishToChat: true);
    }

    private static int NormalizeRightAngleDegrees(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        return normalized switch
        {
            90 => 90,
            180 => 180,
            270 => 270,
            _ => 0
        };
    }

    private static Vector3 MirrorOffsetAcrossLocalX(Vector3 value)
    {
        return RoundClipboardOffset(new Vector3(-value.x, value.y, value.z));
    }

    private static Quaternion MirrorRotationAcrossLocalX(Quaternion rotation)
    {
        var mirroredForward = MirrorDirectionAcrossLocalX(rotation * Vector3.forward);
        var mirroredUp = MirrorDirectionAcrossLocalX(rotation * Vector3.up);

        if (mirroredForward.sqrMagnitude <= 1e-6f || mirroredUp.sqrMagnitude <= 1e-6f)
        {
            return rotation;
        }

        return NormalizeQuaternion(Quaternion.LookRotation(mirroredForward.normalized, mirroredUp.normalized));
    }

    private static Vector3 MirrorDirectionAcrossLocalX(Vector3 value)
    {
        return new Vector3(-value.x, value.y, value.z);
    }

    private static Quaternion NormalizeQuaternion(Quaternion value)
    {
        var magnitude = Mathf.Sqrt((value.x * value.x) + (value.y * value.y) + (value.z * value.z) + (value.w * value.w));
        if (magnitude <= 1e-6f)
        {
            return Quaternion.identity;
        }

        return new Quaternion(value.x / magnitude, value.y / magnitude, value.z / magnitude, value.w / magnitude);
    }

    private static ClipboardEntry CloneClipboardEntry(ClipboardEntry entry)
    {
        return new ClipboardEntry
        {
            SavableObjectID = entry.SavableObjectID,
            RequiredSavableObjectID = entry.RequiredSavableObjectID,
            RelativeOffset = entry.RelativeOffset,
            Rotation = entry.Rotation,
            SupportsEnabled = entry.SupportsEnabled,
            CustomData = entry.CustomData,
            Label = entry.Label
        };
    }

    private static bool TryResolveStackDirection(string rawDirection, out Vector3 direction, out string label)
    {
        direction = Vector3.zero;
        label = string.Empty;

        switch ((rawDirection ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "north":
            case "n":
                direction = Vector3.forward;
                label = "north";
                return true;
            case "south":
            case "s":
                direction = Vector3.back;
                label = "south";
                return true;
            case "east":
            case "e":
                direction = Vector3.right;
                label = "east";
                return true;
            case "west":
            case "w":
                direction = Vector3.left;
                label = "west";
                return true;
            case "up":
            case "u":
                direction = Vector3.up;
                label = "up";
                return true;
            case "down":
            case "d":
                direction = Vector3.down;
                label = "down";
                return true;
            default:
                return false;
        }
    }

    private static bool TryComputeClipboardBounds(ClipboardData clipboard, out Vector3 min, out Vector3 max)
    {
        min = Vector3.zero;
        max = Vector3.zero;
        if (clipboard == null || clipboard.Entries.Count == 0)
        {
            return false;
        }

        min = clipboard.Entries[0].RelativeOffset;
        max = clipboard.Entries[0].RelativeOffset;
        for (var i = 1; i < clipboard.Entries.Count; i++)
        {
            var pos = clipboard.Entries[i].RelativeOffset;
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
        }

        return true;
    }

    private static float ResolveStackStepDistance(Vector3 direction, Vector3 min, Vector3 max, int gap)
    {
        float span;
        if (Mathf.Abs(direction.x) > 0.5f)
        {
            span = Mathf.Round(max.x - min.x) + 1f;
        }
        else if (Mathf.Abs(direction.y) > 0.5f)
        {
            span = Mathf.Round(max.y - min.y) + 1f;
        }
        else
        {
            span = Mathf.Round(max.z - min.z) + 1f;
        }

        return Mathf.Max(1f, span + gap);
    }

    private static Vector3 RoundClipboardOffset(Vector3 value)
    {
        return new Vector3(
            RoundClipboardComponent(value.x),
            RoundClipboardComponent(value.y),
            RoundClipboardComponent(value.z));
    }

    private static float RoundClipboardComponent(float value)
    {
        return Mathf.Round(value * 1000f) / 1000f;
    }
}
