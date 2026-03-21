namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private void OnGUI()
    {
        _popupTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            normal = { textColor = Color.white }
        };
        _popupBodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.93f, 0.95f, 0.97f, 0.98f) }
        };

        DrawPopupOverlay();

        if (!_showDebugWindow) return;
        _windowRect = GUI.Window(923771, _windowRect, DrawDebugWindow, "Miner's Blueprint Debug");
    }


    private void DrawPopupOverlay()
    {
        if (Time.unscaledTime >= _popupUntilTime) return;
        if (string.IsNullOrWhiteSpace(_popupBody) && string.IsNullOrWhiteSpace(_popupTitle)) return;

        var remaining = _popupUntilTime - Time.unscaledTime;
        var fadeAlpha = remaining <= PopupFadeDuration
            ? Mathf.Clamp01(remaining / PopupFadeDuration)
            : 1f;
        if (fadeAlpha <= 0.001f) return;

        var width = Mathf.Min(360f, Screen.width - 24f);
        var height = 74f;
        var rect = new Rect(Screen.width - width - 12f, 12f, width, height);
        var background = new Color(0.03f, 0.05f, 0.07f, 0.90f * fadeAlpha);
        var accent = new Color(_popupAccentColor.r, _popupAccentColor.g, _popupAccentColor.b, _popupAccentColor.a * fadeAlpha);
        UiDrawUtils.DrawSolidRect(rect, background);
        UiDrawUtils.DrawSolidRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        UiDrawUtils.DrawSolidRect(new Rect(rect.x + 12f, rect.y + 26f, rect.width - 24f, 1f), new Color(1f, 1f, 1f, 0.06f * fadeAlpha));

        var previousColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, fadeAlpha);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 6f, rect.width - 24f, 18f), _popupTitle, _popupTitleStyle);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 30f, rect.width - 24f, 34f), _popupBody, _popupBodyStyle);
        GUI.color = previousColor;
    }


    private void DrawDebugWindow(int id)
    {
        GUILayout.BeginVertical();

        GUILayout.Label($"Start ({GetBindingText(_setStartKey)}): {(_hasPointA ? FormatVec(_pointA) : "not set")}");
        GUILayout.Label($"End ({GetBindingText(_setEndKey)}): {(_hasPointB ? FormatVec(_pointB) : "not set")}");
        GUILayout.Label($"Copy ({GetBindingText(_copyKey)})  Ghost/Paste ({GetBindingText(_pasteKey)})");
        GUILayout.Label($"Ghost ({GetBindingText(_toggleGhostPreviewKey)}): {(_ghostPreviewVisible ? "ON" : "OFF")}");
        GUILayout.Label($"Ghost Move X- ({GetBindingText(_ghostMoveXMinusKey)})  X+ ({GetBindingText(_ghostMoveXPlusKey)})");
        GUILayout.Label($"Ghost Move Z- ({GetBindingText(_ghostMoveZMinusKey)})  Z+ ({GetBindingText(_ghostMoveZPlusKey)})");
        GUILayout.Label($"Ghost Elevation - ({GetBindingText(_ghostMoveYMinusKey)})  + ({GetBindingText(_ghostMoveYPlusKey)})");

        if (_hasPointA && _hasPointB)
        {
            GUILayout.Label($"Selection Size: {FormatVec(_selectionBounds.size)}");
            GUILayout.Label($"Selected Objects: {_selectionObjects.Count}");
        }

        GUILayout.Space(6f);
        GUILayout.Label("Selection Contents:");
        _windowScroll = GUILayout.BeginScrollView(_windowScroll, GUILayout.Height(220f));

        if (_selectionObjects.Count == 0)
        {
            GUILayout.Label("- none -");
        }
        else
        {
            var grouped = _selectionObjects
                .Where(o => o != null)
                .GroupBy(o => o.SavableObjectID)
                .OrderBy(g => ResolveName(g.Key), StringComparer.Ordinal);

            foreach (var group in grouped)
            {
                GUILayout.Label($"{ResolveName(group.Key)} [{group.Key}] x{group.Count()}");
            }
        }

        GUILayout.EndScrollView();

        GUILayout.Space(6f);
        var clipCount = _clipboard?.Entries.Count ?? 0;
        GUILayout.Label($"Clipboard Objects: {clipCount}");
        if (clipCount > 0)
        {
            var grouped = _clipboard!.Entries
                .GroupBy(e => e.SavableObjectID)
                .OrderBy(g => ResolveName(g.Key), StringComparer.Ordinal);
            foreach (var group in grouped)
            {
                GUILayout.Label($"{ResolveName(group.Key)} x{group.Count()}");
            }
        }

        GUILayout.Space(6f);
        if (GUILayout.Button("Refresh Selection"))
        {
            RefreshSelection();
        }
        if (GUILayout.Button("Hide Window"))
        {
            _showDebugWindow = false;
        }

        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }


    private static string FormatVec(Vector3 v)
    {
        return $"{v.x:0.00}, {v.y:0.00}, {v.z:0.00}";
    }


}
