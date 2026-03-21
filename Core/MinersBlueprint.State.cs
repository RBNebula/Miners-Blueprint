namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private enum NotificationLevel
    {
        Info,
        Success,
        Warning
    }

    private enum BuildMode
    {
        Normal,
        Unlimited
    }

    private enum PendingConfirmAction
    {
        None,
        BlueprintDelete,
        SelectionRemove
    }

    private const KeyCode DefaultSetStartKey = KeyCode.None;
    private const KeyCode DefaultSetEndKey = KeyCode.None;
    private const KeyCode DefaultCopyKey = KeyCode.None;
    private const KeyCode DefaultPasteKey = KeyCode.None;
    private const KeyCode DefaultToggleGhostPreviewKey = KeyCode.None;
    private const KeyCode DefaultGhostMoveXMinusKey = KeyCode.LeftArrow;
    private const KeyCode DefaultGhostMoveXPlusKey = KeyCode.RightArrow;
    private const KeyCode DefaultGhostMoveZMinusKey = KeyCode.DownArrow;
    private const KeyCode DefaultGhostMoveZPlusKey = KeyCode.UpArrow;
    private const KeyCode DefaultGhostMoveYMinusKey = KeyCode.Minus;
    private const KeyCode DefaultGhostMoveYPlusKey = KeyCode.Equals;
    private const KeyCode DefaultToggleWindowKey = KeyCode.None;
    private const int MaxUndoHistoryEntries = 20;
    private const float LayeredPasteDelaySeconds = 0.1f;
    private static readonly KeyboardShortcut DefaultSetStartShortcut = KeyboardShortcut.Empty;
    private static readonly KeyboardShortcut DefaultSetEndShortcut = KeyboardShortcut.Empty;
    private static readonly KeyboardShortcut DefaultCopyShortcut = KeyboardShortcut.Empty;
    private static readonly KeyboardShortcut DefaultPasteShortcut = KeyboardShortcut.Empty;
    private static readonly KeyboardShortcut DefaultToggleGhostPreviewShortcut = KeyboardShortcut.Empty;

    private readonly List<SelectedWorldObject> _selectionObjects = new();
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
    private ConfigEntry<string> _buildModeConfig = null!;
    private BuildMode _buildMode;
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
    private GUIStyle? _popupTitleStyle;
    private GUIStyle? _popupBodyStyle;
    private Color _popupAccentColor = new(0.95f, 0.73f, 0.25f, 0.96f);
    private GameObject? _selectionRoot;
    private GameObject? _selectionHighlightRoot;
    private GameObject? _ghostPreviewRoot;
    private GameObject? _ghostBlockerPreviewRoot;
    private Material? _lineMaterial;
    private Material? _selectionHighlightLineMaterial;
    private Material? _ghostMaterial;
    private Material? _ghostBlockerLineMaterial;
    private bool _ghostPreviewVisible;
    private readonly List<GhostPreviewInstance> _ghostPreviewInstances = new();
    private readonly List<GhostBlockerBox> _ghostBlockerBoxes = new();
    private readonly List<SelectionHighlightBox> _selectionHighlightBoxes = new();
    private float _nextGhostBlockerRefreshTime;
    private readonly Stack<UndoTransaction> _undoHistory = new();
    private LayeredPasteJob? _activeLayeredPasteJob;
    private Vector3 _ghostPreviewAnchor = new(float.NaN, float.NaN, float.NaN);
    private string _popupTitle = string.Empty;
    private string _popupBody = string.Empty;
    private float _popupShownTime;
    private float _popupUntilTime;
    private PendingConfirmAction _pendingConfirmAction;
    private string? _pendingDeleteBlueprintPath;
    private string? _pendingDeleteBlueprintName;
    private readonly List<PendingSelectionRemovalTarget> _pendingSelectionRemovalTargets = new();
    private int _pendingSelectionRemovalCount;

    private static readonly BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class ClipboardData
    {
        public Vector3 CopyAnchor;
        public Vector3 PlayerPosition;
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

    private sealed class SelectionToolMarker : MonoBehaviour
    {
    }

    private sealed class GhostBlockerBox
    {
        public GameObject Root = null!;
        public LineRenderer[] Edges = new LineRenderer[12];
    }

    private sealed class SelectionHighlightBox
    {
        public GameObject Root = null!;
        public LineRenderer[] Edges = new LineRenderer[12];
    }

    private sealed class UndoTransaction
    {
        public readonly List<GameObject> PlacedObjects = new();
        public readonly List<UndoObjectSnapshot> RemovedObjects = new();
        public readonly HashSet<int> RemovedInstanceIds = new();
        public BuildMode BuildMode;
    }

    private readonly struct UndoExecutionResult
    {
        public UndoExecutionResult(int restored, int restoreFailed, int refunded, int destroyed, int refundFailed)
        {
            Restored = restored;
            RestoreFailed = restoreFailed;
            Refunded = refunded;
            Destroyed = destroyed;
            RefundFailed = refundFailed;
        }

        public int Restored { get; }
        public int RestoreFailed { get; }
        public int Refunded { get; }
        public int Destroyed { get; }
        public int RefundFailed { get; }
    }

    private sealed class UndoObjectSnapshot
    {
        public SavableObjectID SavableObjectID;
        public SavableObjectID RequiredSavableObjectID;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool SupportsEnabled;
        public string CustomData = string.Empty;
        public string Label = string.Empty;
    }

    private sealed class LayeredPasteEntry
    {
        public ClipboardEntry Entry = null!;
        public Vector3 TargetPos;
    }

    private sealed class LayeredPasteLayer
    {
        public int SliceCoordinate;
        public readonly List<LayeredPasteEntry> Entries = new();
    }

    private sealed class LayeredPasteJob
    {
        public SavingLoadingManager Saving = null!;
        public PlayerInventory? Inventory;
        public List<ToolStack>? Stacks;
        public bool RequiresInventory;
        public readonly List<LayeredPasteLayer> Layers = new();
        public readonly HashSet<int> ProtectedPlacedIds = new();
        public readonly Dictionary<SavableObjectID, int> ConsumedOnSuccess = new();
        public readonly UndoTransaction UndoTransaction = new();
        public char SliceAxis;
        public string SliceAxisLabel = string.Empty;
        public int NextLayerIndex;
        public float NextLayerAtTime;
        public int Spawned;
        public int Blocked;
        public int SpawnFailed;
        public int Replaced;
    }

    private sealed class SelectedWorldObject
    {
        public MonoBehaviour Component = null!;
        public ISaveLoadableObject Saveable = null!;
        public BuildingObject? Building;
        public SavableObjectID SavableObjectID;
        public string Label = string.Empty;
    }

    private sealed class PendingSelectionRemovalTarget
    {
        public MonoBehaviour Component = null!;
        public UndoObjectSnapshot Snapshot = null!;
    }

    private enum OccupiedClearResult
    {
        Empty,
        Cleared,
        Failed
    }
}
