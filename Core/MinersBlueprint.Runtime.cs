namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private void Update()
    {
        UpdateChatCommandsRegistration();

        if (!_rebindRegistered && Time.unscaledTime >= _nextRebindAttemptTime)
        {
            _rebindRegistered = TryRegisterRebindKeybinds();
            _nextRebindAttemptTime = Time.unscaledTime + 1f;
        }

        UpdateSelectionVisual();
        if (!CanProcessHotkeys())
        {
            return;
        }

        if (WasPressed(_toggleWindowKey))
        {
            _showDebugWindow = !_showDebugWindow;
        }

        if (WasPressed(_setStartKey))
        {
            SetStartPoint();
        }

        if (WasPressed(_setEndKey))
        {
            SetEndPoint();
        }

        if (WasPressed(_copyKey))
        {
            CopySelection();
        }

        if (WasPressed(_pasteKey))
        {
            if (_ghostPreviewVisible)
            {
                PasteClipboard();
            }
            else
            {
                PlaceGhostAtPlayerAnchor(showToast: true);
            }
        }

        if (WasPressed(_toggleGhostPreviewKey))
        {
            ToggleGhostPreview();
        }

        UpdateGhostPreviewVisual();
    }
}
