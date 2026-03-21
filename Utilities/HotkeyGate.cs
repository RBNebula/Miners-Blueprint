namespace MinersBlueprint;

internal static class HotkeyGate
{
    private static int _lastCheckedFrame = -1;
    private static bool _lastFrameResult;
    private static float _nextPlayerContextCheckTime;
    private static bool _cachedPlayerContext;

    public static bool IsHotkeyInputEnabled()
    {
        try
        {
            int frame = Time.frameCount;
            if (_lastCheckedFrame == frame)
            {
                return _lastFrameResult;
            }

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool result =
                scene.IsValid() &&
                scene.isLoaded &&
                !string.IsNullOrWhiteSpace(scene.name) &&
                !string.Equals(scene.name, "MainMenu", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scene.name, "Bootstrap", StringComparison.OrdinalIgnoreCase) &&
                HasPlayerContext();

            _lastCheckedFrame = frame;
            _lastFrameResult = result;
            return result;
        }
        catch
        {
            _lastCheckedFrame = Time.frameCount;
            _lastFrameResult = false;
            return false;
        }
    }

    private static bool HasPlayerContext()
    {
        float now = Time.unscaledTime;
        if (now < _nextPlayerContextCheckTime)
        {
            return _cachedPlayerContext;
        }

        _nextPlayerContextCheckTime = now + 0.25f;
        _cachedPlayerContext = HasPlayerContextSlow();
        return _cachedPlayerContext;
    }

    private static bool HasPlayerContextSlow()
    {
        try
        {
            GameObject playerTagged = GameObject.FindWithTag("Player");
            if (playerTagged != null)
            {
                return true;
            }
        }
        catch
        {
        }

        Type? playerControllerType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(a => a.GetType("PlayerController", throwOnError: false))
            .FirstOrDefault(t => t != null);

        if (playerControllerType == null)
        {
            return false;
        }

        return TryGetSingletonInstance(playerControllerType) != null;
    }

    private static object? TryGetSingletonInstance(Type type)
    {
        BindingFlags anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        string[] singletonNames = { "Instance", "instance", "Singleton", "Current", "current" };

        for (int i = 0; i < singletonNames.Length; i++)
        {
            PropertyInfo? property = type.GetProperty(singletonNames[i], anyStatic);
            if (property != null && property.CanRead)
            {
                try
                {
                    return property.GetValue(null, null);
                }
                catch
                {
                }
            }

            FieldInfo? field = type.GetField(singletonNames[i], anyStatic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(null);
                }
                catch
                {
                }
            }
        }

#pragma warning disable CS0618
        return UnityEngine.Object.FindObjectOfType(type);
#pragma warning restore CS0618
    }
}
