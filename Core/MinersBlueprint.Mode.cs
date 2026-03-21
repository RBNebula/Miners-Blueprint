namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private static BuildMode ParseBuildMode(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, ignoreCase: true, out BuildMode parsed))
        {
            return parsed;
        }

        return BuildMode.Normal;
    }

    private void SetBuildMode(BuildMode mode, bool publishToChat)
    {
        _buildMode = mode;
        _buildModeConfig.Value = mode.ToString();
        Config.Save();

        var message = mode == BuildMode.Unlimited
            ? "Build mode set to Unlimited. Paste no longer requires or consumes inventory items."
            : "Build mode set to Normal. Paste now requires and consumes inventory items again.";
        Notify(message, NotificationLevel.Success, 3.6f, "Mode", publishToChat);
    }
}
