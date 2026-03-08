namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private void OnGUI()
    {
        _toastLabelStyle ??= new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = 14,
            wordWrap = true
        };
        _popupTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            normal = { textColor = Color.white }
        };
        _popupBodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true,
            normal = { textColor = new Color(0.93f, 0.95f, 0.97f, 0.98f) }
        };

        _toasts.Draw(_toastLabelStyle);
        DrawPopupOverlay();

        if (!_showDebugWindow) return;
        _windowRect = GUI.Window(923771, _windowRect, DrawDebugWindow, "Miner's Blueprint Debug");
    }


    private void DrawPopupOverlay()
    {
        if (Time.unscaledTime >= _popupUntilTime) return;
        if (string.IsNullOrWhiteSpace(_popupBody) && string.IsNullOrWhiteSpace(_popupTitle)) return;

        var width = Mathf.Min(560f, Screen.width - 40f);
        var rect = new Rect((Screen.width - width) * 0.5f, Mathf.Max(50f, Screen.height * 0.22f), width, 210f);
        UiDrawUtils.DrawSolidRect(rect, new Color(0.03f, 0.05f, 0.07f, 0.94f));
        UiDrawUtils.DrawSolidRect(new Rect(rect.x, rect.y, rect.width, 3f), new Color(0.95f, 0.73f, 0.25f, 0.96f));
        UiDrawUtils.DrawSolidRect(new Rect(rect.x + 12f, rect.y + 42f, rect.width - 24f, 1f), new Color(1f, 1f, 1f, 0.08f));

        GUI.Label(new Rect(rect.x + 14f, rect.y + 12f, rect.width - 28f, 26f), _popupTitle, _popupTitleStyle);
        GUI.Label(new Rect(rect.x + 14f, rect.y + 50f, rect.width - 28f, rect.height - 88f), _popupBody, _popupBodyStyle);

        if (GUI.Button(new Rect(rect.x + rect.width - 88f, rect.y + rect.height - 34f, 74f, 24f), "Close"))
        {
            _popupUntilTime = 0f;
        }
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
