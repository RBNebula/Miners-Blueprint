namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private const float DefaultPopupDuration = 1.5f;
    private const float PopupFadeDuration = 0.22f;

    private void Notify(string message, NotificationLevel level = NotificationLevel.Info, float duration = DefaultPopupDuration, string? title = null, bool publishToChat = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        title ??= level switch
        {
            NotificationLevel.Success => "Success",
            NotificationLevel.Warning => "Warning",
            _ => "Info"
        };

        ShowPopup(title, message, duration, level);

        switch (level)
        {
            case NotificationLevel.Warning:
                Logger.LogWarning($"{ModInfo.LOG_PREFIX} {message}");
                if (publishToChat)
                {
                    PublishMbError(message);
                }
                break;
            default:
                Logger.LogInfo($"{ModInfo.LOG_PREFIX} {message}");
                if (publishToChat)
                {
                    PublishMbInfo(message);
                }
                break;
        }
    }

    private void ShowPopup(string title, string body, float duration, NotificationLevel level = NotificationLevel.Info)
    {
        _popupTitle = title ?? string.Empty;
        _popupBody = body ?? string.Empty;
        _popupShownTime = Time.unscaledTime;
        _popupUntilTime = _popupShownTime + Mathf.Max(DefaultPopupDuration, duration);
        _popupAccentColor = level switch
        {
            NotificationLevel.Success => new Color(0.20f, 0.82f, 0.48f, 0.96f),
            NotificationLevel.Warning => new Color(0.95f, 0.73f, 0.25f, 0.96f),
            _ => new Color(0.29f, 0.63f, 0.95f, 0.96f)
        };
    }
}
