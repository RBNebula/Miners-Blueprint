namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private const KeyCode DefaultSetStartKey = KeyCode.Alpha1;
    private const KeyCode DefaultSetEndKey = KeyCode.Alpha2;
    private const KeyCode DefaultCopyKey = KeyCode.Alpha3;
    private const KeyCode DefaultPasteKey = KeyCode.Alpha4;
    private const KeyCode DefaultToggleGhostPreviewKey = KeyCode.Alpha5;
    private const KeyCode DefaultGhostMoveXMinusKey = KeyCode.LeftArrow;
    private const KeyCode DefaultGhostMoveXPlusKey = KeyCode.RightArrow;
    private const KeyCode DefaultGhostMoveZMinusKey = KeyCode.DownArrow;
    private const KeyCode DefaultGhostMoveZPlusKey = KeyCode.UpArrow;
    private const KeyCode DefaultGhostMoveYMinusKey = KeyCode.Minus;
    private const KeyCode DefaultGhostMoveYPlusKey = KeyCode.Equals;
    private const KeyCode DefaultToggleWindowKey = KeyCode.RightBracket;
    private static readonly KeyboardShortcut DefaultSetStartShortcut = new(DefaultSetStartKey, KeyCode.LeftShift);
    private static readonly KeyboardShortcut DefaultSetEndShortcut = new(DefaultSetEndKey, KeyCode.LeftShift);
    private static readonly KeyboardShortcut DefaultCopyShortcut = new(DefaultCopyKey, KeyCode.LeftShift);
    private static readonly KeyboardShortcut DefaultPasteShortcut = new(DefaultPasteKey, KeyCode.LeftShift);
    private static readonly KeyboardShortcut DefaultToggleGhostPreviewShortcut = new(DefaultToggleGhostPreviewKey, KeyCode.LeftShift);

    private readonly UiToastQueue _toasts = new();
    private readonly List<BuildingObject> _selectionObjects = new();
    private readonly Vector3[] _selectionCorners = new Vector3[8];
    private readonly LineRenderer[] _selectionEdges = new LineRenderer[12];
    private readonly Dictionary<SavableObjectID, string> _nameByIdCache = new();
    private readonly List<IDisposable> _rebindHandles = new();
    private bool _rebindRegistered;
    private float _nextRebindAttemptTime;
    private bool _reportedInventoryFullOnReplace;
    private int _hotkeyGateFrame = -1;
    private bool _hotkeyGateEnabled;

    private ConfigEntry<KeyboardShortcut> _setStartKey = null!;
    private ConfigEntry<KeyboardShortcut> _setEndKey = null!;
    private ConfigEntry<KeyboardShortcut> _copyKey = null!;
    private ConfigEntry<KeyboardShortcut> _pasteKey = null!;
    private ConfigEntry<KeyboardShortcut> _toggleGhostPreviewKey = null!;
    private ConfigEntry<KeyboardShortcut> _ghostMoveXMinusKey = null!;
    private ConfigEntry<KeyboardShortcut> _ghostMoveXPlusKey = null!;
    private ConfigEntry<KeyboardShortcut> _ghostMoveZMinusKey = null!;
    private ConfigEntry<KeyboardShortcut> _ghostMoveZPlusKey = null!;
    private ConfigEntry<KeyboardShortcut> _ghostMoveYMinusKey = null!;
    private ConfigEntry<KeyboardShortcut> _ghostMoveYPlusKey = null!;
    private ConfigEntry<KeyboardShortcut> _toggleWindowKey = null!;
    private ConfigEntry<float> _lookDistance = null!;
    private ConfigEntry<float> _cellSize = null!;
    private Vector3 _pointA;
    private Vector3 _pointB;
    private Vector3Int _cellA;
    private Vector3Int _cellB;
    private Vector3Int _selectionCellMin;
    private Vector3Int _selectionCellMax;
    private bool _hasPointA;
    private bool _hasPointB;
    private Bounds _selectionBounds;
    private ClipboardData? _clipboard;
    private bool _showDebugWindow;
    private Vector2 _windowScroll;
    private Rect _windowRect = new(20f, 80f, 460f, 460f);
    private GUIStyle? _toastLabelStyle;
    private GUIStyle? _popupTitleStyle;
    private GUIStyle? _popupBodyStyle;
    private GameObject? _selectionRoot;
    private GameObject? _ghostPreviewRoot;
    private GameObject? _ghostBlockerPreviewRoot;
    private Material? _lineMaterial;
    private Material? _ghostMaterial;
    private Material? _ghostBlockerLineMaterial;
    private bool _ghostPreviewVisible;
    private readonly List<GhostPreviewInstance> _ghostPreviewInstances = new();
    private readonly List<GhostBlockerBox> _ghostBlockerBoxes = new();
    private float _nextGhostBlockerRefreshTime;
    private Vector3 _ghostPreviewAnchor = new(float.NaN, float.NaN, float.NaN);
    private string _popupTitle = string.Empty;
    private string _popupBody = string.Empty;
    private float _popupUntilTime;

    private static readonly BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class ClipboardData
    {
        public readonly List<ClipboardEntry> Entries = new();
    }

    private sealed class ClipboardEntry
    {
        public SavableObjectID SavableObjectID;
        public SavableObjectID RequiredSavableObjectID;
        public Vector3 RelativeOffset;
        public Quaternion Rotation;
        public bool SupportsEnabled;
        public string CustomData = string.Empty;
        public string Label = string.Empty;
    }

    private sealed class ToolStack
    {
        public int SlotIndex;
        public ToolBuilder Tool = null!;
        public SavableObjectID SavableObjectID;
    }

    private sealed class GhostPreviewInstance
    {
        public GameObject Root = null!;
        public Vector3 RelativeOffset;
    }

    private sealed class GhostPreviewMarker : MonoBehaviour
    {
    }

    private sealed class GhostBlockerBox
    {
        public GameObject Root = null!;
        public LineRenderer[] Edges = new LineRenderer[12];
    }

    private enum OccupiedClearResult
    {
        Empty,
        Cleared,
        Failed
    }
}
