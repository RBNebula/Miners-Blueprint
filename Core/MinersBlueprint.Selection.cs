namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private bool CanProcessHotkeys()
    {
        var ui = Singleton<UIManager>.Instance;
        return ui == null || !ui.IsInAnyMenu();
    }


    private void SetStartPoint()
    {
        if (!TryGetLookedPoint(out var point, out _))
        {
            Notify("No valid target for start point.", NotificationLevel.Warning);
            return;
        }

        _pointA = point;
        _cellA = WorldToCell(point);
        _pointA = CellCenter(_cellA);
        _hasPointA = true;
        _hasPointB = false;
        _selectionObjects.Clear();
        EnsureSelectionHighlightBoxCount(0);
        Notify($"Start point set: {FormatVec(_pointA)}", NotificationLevel.Success, title: "Selection");
    }


    private void SetEndPoint()
    {
        if (!_hasPointA)
        {
            Notify("Set a start point first with /mb set pos 1.", NotificationLevel.Warning, title: "Selection");
            return;
        }
        if (!TryGetLookedPoint(out var point, out _))
        {
            Notify("No valid target for end point.", NotificationLevel.Warning);
            return;
        }

        _pointB = point;
        _cellB = WorldToCell(point);
        _pointB = CellCenter(_cellB);
        _hasPointB = true;
        RebuildSelectionBounds();
        RefreshSelection();
        Notify($"Selection ready: {_selectionObjects.Count} objects.", NotificationLevel.Success, title: "Selection");
    }


    private void ShiftSelection(string rawDirection, int amount)
    {
        if (!_hasPointA || !_hasPointB)
        {
            Notify("Create a full selection before shifting it.", NotificationLevel.Warning, 4.6f, "Selection", publishToChat: true);
            return;
        }

        if (!TryResolveStackDirection(rawDirection, out var direction, out var directionLabel))
        {
            Notify("Shift direction must be north, south, east, west, up, or down.", NotificationLevel.Warning, 4.6f, "Selection", publishToChat: true);
            return;
        }

        var cellDelta = new Vector3Int(
            Mathf.RoundToInt(direction.x * amount),
            Mathf.RoundToInt(direction.y * amount),
            Mathf.RoundToInt(direction.z * amount));
        if (cellDelta == Vector3Int.zero)
        {
            Notify("Shift amount must move the selection by at least one block.", NotificationLevel.Warning, 4.6f, "Selection", publishToChat: true);
            return;
        }

        _cellA += cellDelta;
        _cellB += cellDelta;
        _pointA = CellCenter(_cellA);
        _pointB = CellCenter(_cellB);
        RebuildSelectionBounds();
        RefreshSelection();

        Notify($"Shifted selection {directionLabel} by {amount}.", NotificationLevel.Success, 4f, "Selection", publishToChat: true);
        Notify($"Selection now contains {_selectionObjects.Count} object(s).", NotificationLevel.Info, 3.8f, "Selection");
    }


    private void GrowSelection(int amount)
    {
        if (!TryResizeSelection(amount, out var changedAmount))
        {
            Notify("Create a full selection before growing it.", NotificationLevel.Warning, 4.6f, "Selection", publishToChat: true);
            return;
        }

        Notify($"Grew selection by {changedAmount}.", NotificationLevel.Success, 4f, "Selection", publishToChat: true);
        Notify($"Selection now contains {_selectionObjects.Count} object(s).", NotificationLevel.Info, 3.8f, "Selection");
    }


    private void ShrinkSelection(int amount)
    {
        if (!_hasPointA || !_hasPointB)
        {
            Notify("Create a full selection before shrinking it.", NotificationLevel.Warning, 4.6f, "Selection", publishToChat: true);
            return;
        }

        var clampedAmount = ResolveMaxShrinkAmount(amount);
        if (clampedAmount <= 0)
        {
            Notify("Selection is already at its minimum size.", NotificationLevel.Warning, 4.6f, "Selection", publishToChat: true);
            return;
        }

        TryResizeSelection(-clampedAmount, out _);
        Notify($"Shrank selection by {clampedAmount}.", NotificationLevel.Success, 4f, "Selection", publishToChat: true);
        Notify($"Selection now contains {_selectionObjects.Count} object(s).", NotificationLevel.Info, 3.8f, "Selection");
    }


    private void ExpandSelection(string rawDirection, int amount)
    {
        if (!TryAdjustSelectionFace(rawDirection, amount, out var directionLabel, out _, out var message))
        {
            Notify(message, NotificationLevel.Warning, 4.6f, "Selection", publishToChat: true);
            return;
        }

        Notify($"Expanded selection {directionLabel} by {amount}.", NotificationLevel.Success, 4f, "Selection", publishToChat: true);
        Notify($"Selection now contains {_selectionObjects.Count} object(s).", NotificationLevel.Info, 3.8f, "Selection");
    }


    private void ContractSelection(string rawDirection, int amount)
    {
        if (!TryAdjustSelectionFace(rawDirection, -amount, out var directionLabel, out var actualAmount, out var message))
        {
            Notify(message, NotificationLevel.Warning, 4.6f, "Selection", publishToChat: true);
            return;
        }

        Notify($"Contracted selection {directionLabel} by {actualAmount}.", NotificationLevel.Success, 4f, "Selection", publishToChat: true);
        Notify($"Selection now contains {_selectionObjects.Count} object(s).", NotificationLevel.Info, 3.8f, "Selection");
    }


    private bool TryAdjustSelectionFace(string rawDirection, int signedAmount, out string directionLabel, out int actualAmount, out string message)
    {
        directionLabel = string.Empty;
        actualAmount = 0;
        message = "Create a full selection first.";
        if (!_hasPointA || !_hasPointB)
        {
            return false;
        }

        if (!TryResolveStackDirection(rawDirection, out var direction, out directionLabel))
        {
            message = "Direction must be north, south, east, west, up, or down.";
            return false;
        }

        if (signedAmount == 0)
        {
            message = "Amount must move the selection by at least one block.";
            return false;
        }

        var cellMin = new Vector3Int(
            Mathf.Min(_cellA.x, _cellB.x),
            Mathf.Min(_cellA.y, _cellB.y),
            Mathf.Min(_cellA.z, _cellB.z));
        var cellMax = new Vector3Int(
            Mathf.Max(_cellA.x, _cellB.x),
            Mathf.Max(_cellA.y, _cellB.y),
            Mathf.Max(_cellA.z, _cellB.z));

        var amount = Mathf.Abs(signedAmount);
        if (signedAmount < 0)
        {
            amount = ResolveMaxFaceContractAmount(direction, amount, cellMin, cellMax);
            if (amount <= 0)
            {
                message = "Selection cannot contract further in that direction.";
                return false;
            }
        }

        if (Mathf.Abs(direction.x) > 0.5f)
        {
            if (direction.x > 0f)
            {
                cellMax.x += signedAmount > 0 ? amount : -amount;
            }
            else
            {
                cellMin.x -= signedAmount > 0 ? amount : -amount;
            }
        }
        else if (Mathf.Abs(direction.y) > 0.5f)
        {
            if (direction.y > 0f)
            {
                cellMax.y += signedAmount > 0 ? amount : -amount;
            }
            else
            {
                cellMin.y -= signedAmount > 0 ? amount : -amount;
            }
        }
        else
        {
            if (direction.z > 0f)
            {
                cellMax.z += signedAmount > 0 ? amount : -amount;
            }
            else
            {
                cellMin.z -= signedAmount > 0 ? amount : -amount;
            }
        }

        ApplySelectionCellsFromMinMax(cellMin, cellMax);
        actualAmount = amount;
        message = string.Empty;
        return true;
    }


    private bool TryResizeSelection(int delta, out int changedAmount)
    {
        changedAmount = 0;
        if (!_hasPointA || !_hasPointB)
        {
            return false;
        }

        var cellMin = new Vector3Int(
            Mathf.Min(_cellA.x, _cellB.x),
            Mathf.Min(_cellA.y, _cellB.y),
            Mathf.Min(_cellA.z, _cellB.z));
        var cellMax = new Vector3Int(
            Mathf.Max(_cellA.x, _cellB.x),
            Mathf.Max(_cellA.y, _cellB.y),
            Mathf.Max(_cellA.z, _cellB.z));

        cellMin -= new Vector3Int(delta, delta, delta);
        cellMax += new Vector3Int(delta, delta, delta);

        changedAmount = Mathf.Abs(delta);
        ApplySelectionCellsFromMinMax(cellMin, cellMax);
        return true;
    }


    private int ResolveMaxShrinkAmount(int requestedAmount)
    {
        var sizeX = Mathf.Abs(_cellB.x - _cellA.x) + 1;
        var sizeY = Mathf.Abs(_cellB.y - _cellA.y) + 1;
        var sizeZ = Mathf.Abs(_cellB.z - _cellA.z) + 1;

        var maxShrinkX = Mathf.Max(0, (sizeX - 1) / 2);
        var maxShrinkY = Mathf.Max(0, (sizeY - 1) / 2);
        var maxShrinkZ = Mathf.Max(0, (sizeZ - 1) / 2);
        return Mathf.Max(0, Mathf.Min(requestedAmount, maxShrinkX, maxShrinkY, maxShrinkZ));
    }


    private static int ResolveMaxFaceContractAmount(Vector3 direction, int requestedAmount, Vector3Int cellMin, Vector3Int cellMax)
    {
        int axisSize;
        if (Mathf.Abs(direction.x) > 0.5f)
        {
            axisSize = Mathf.Abs(cellMax.x - cellMin.x) + 1;
        }
        else if (Mathf.Abs(direction.y) > 0.5f)
        {
            axisSize = Mathf.Abs(cellMax.y - cellMin.y) + 1;
        }
        else
        {
            axisSize = Mathf.Abs(cellMax.z - cellMin.z) + 1;
        }

        return Mathf.Max(0, Mathf.Min(requestedAmount, axisSize - 1));
    }


    private void ApplySelectionCellsFromMinMax(Vector3Int cellMin, Vector3Int cellMax)
    {
        _cellA = cellMin;
        _cellB = cellMax;
        _pointA = CellCenter(_cellA);
        _pointB = CellCenter(_cellB);
        RebuildSelectionBounds();
        RefreshSelection();
    }


    private void RebuildSelectionBounds()
    {
        var cellMin = new Vector3Int(
            Mathf.Min(_cellA.x, _cellB.x),
            Mathf.Min(_cellA.y, _cellB.y),
            Mathf.Min(_cellA.z, _cellB.z));
        var cellMax = new Vector3Int(
            Mathf.Max(_cellA.x, _cellB.x),
            Mathf.Max(_cellA.y, _cellB.y),
            Mathf.Max(_cellA.z, _cellB.z));
        _selectionCellMin = cellMin;
        _selectionCellMax = cellMax;

        var size = Mathf.Max(0.01f, _cellSize.Value);
        var min = new Vector3(cellMin.x * size, cellMin.y * size, cellMin.z * size);
        var max = new Vector3((cellMax.x + 1) * size, (cellMax.y + 1) * size, (cellMax.z + 1) * size);
        var eps = 0.005f;
        min -= new Vector3(eps, eps, eps);
        max += new Vector3(eps, eps, eps);
        _selectionBounds.SetMinMax(min, max);
        BuildCorners(min, max);
    }


    private void RefreshSelection()
    {
        _selectionObjects.Clear();
        if (!_hasPointA || !_hasPointB)
        {
            EnsureSelectionHighlightBoxCount(0);
            return;
        }

        var all = FindSelectableWorldObjects();
        for (var i = 0; i < all.Count; i++)
        {
            var obj = all[i];
            if (obj == null || obj.Component == null) continue;
            if (!IsPositionInSelection(obj.Component.transform.position)) continue;
            _selectionObjects.Add(obj);
            var id = obj.SavableObjectID;
            if (!_nameByIdCache.ContainsKey(id))
            {
                _nameByIdCache[id] = obj.Label;
            }
        }

        RefreshSelectionHighlightVisuals();
    }

    private bool IsPositionInSelection(Vector3 position)
    {
        var cell = WorldToCell(position);
        return cell.x >= _selectionCellMin.x && cell.x <= _selectionCellMax.x &&
               cell.y >= _selectionCellMin.y && cell.y <= _selectionCellMax.y &&
               cell.z >= _selectionCellMin.z && cell.z <= _selectionCellMax.z;
    }


    private List<SelectedWorldObject> FindSelectableWorldObjects()
    {
        var result = new List<SelectedWorldObject>();
        var all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        var seen = new HashSet<int>();
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] is not ISaveLoadableObject saveable) continue;
            var component = all[i];
            if (component == null || !component.isActiveAndEnabled) continue;
            if (component.GetComponentInParent<GhostPreviewMarker>() != null) continue;
            if (component.GetComponentInParent<PlayerController>() != null) continue;
            if (component.GetComponentInParent<PlayerInventory>() != null) continue;

            if (component is BuildingObject building && building.IsGhost)
            {
                continue;
            }

            try
            {
                if (!saveable.ShouldBeSaved())
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            var savableId = saveable.GetSavableObjectID();
            if (savableId == SavableObjectID.INVALID)
            {
                continue;
            }

            var instanceId = component.GetInstanceID();
            if (!seen.Add(instanceId))
            {
                continue;
            }

            result.Add(new SelectedWorldObject
            {
                Component = component,
                Saveable = saveable,
                Building = component as BuildingObject,
                SavableObjectID = savableId,
                Label = BuildSaveableLabel(component, saveable)
            });
        }

        return result;
    }


    private bool TryGetLookedPoint(out Vector3 point, out BuildingObject? lookedObject)
    {
        point = default;
        lookedObject = null;
        var cam = Camera.main;
        if (cam == null) return false;

        var hits = Physics.RaycastAll(
            cam.transform.position,
            cam.transform.forward,
            Mathf.Max(1f, _lookDistance.Value),
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        RaycastHit? chosen = null;
        for (var i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null) continue;
            chosen = hits[i];
            break;
        }

        if (chosen == null) return false;
        var hit = chosen.Value;

        lookedObject = hit.collider.GetComponentInParent<BuildingObject>();

        // Move slightly inward from the face so grid snapping lands in the intended touched cell.
        var inwardEps = Mathf.Max(0.005f, _cellSize.Value * 0.02f);
        var candidate = hit.point - (hit.normal * inwardEps);

        if (lookedObject != null)
        {
            var hitCell = WorldToCell(candidate);
            var objectCell = WorldToCell(lookedObject.transform.position);

            // Prevent top-face picks from drifting into the air cell above thin objects (e.g. conveyors).
            if (hit.normal.y > 0.25f && hitCell.y > objectCell.y)
            {
                hitCell.y = objectCell.y;
                candidate = CellCenter(hitCell);
            }
        }

        point = candidate;
        return true;
    }


    private void SetupSelectionRenderer()
    {
        _selectionRoot = new GameObject("MinersBlueprintSelectionBounds");
        DontDestroyOnLoad(_selectionRoot);
        _selectionRoot.SetActive(false);

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (shader != null)
        {
            _lineMaterial = new Material(shader);
            _lineMaterial.color = new Color(0.25f, 1f, 0.25f, 0.9f);
            _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        for (var i = 0; i < _selectionEdges.Length; i++)
        {
            var edgeGo = new GameObject("Edge_" + i);
            edgeGo.transform.SetParent(_selectionRoot.transform, worldPositionStays: false);
            var lr = edgeGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.startColor = new Color(0.25f, 1f, 0.25f, 0.92f);
            lr.endColor = new Color(0.25f, 1f, 0.25f, 0.92f);
            if (_lineMaterial != null) lr.material = _lineMaterial;
            _selectionEdges[i] = lr;
        }
    }


    private void UpdateSelectionVisual()
    {
        if (_selectionRoot == null) return;
        var visible = _hasPointA;
        if (_selectionRoot.activeSelf != visible) _selectionRoot.SetActive(visible);
        if (!visible) return;

        if (_hasPointA && !_hasPointB)
        {
            GetCellBounds(_cellA, out var min, out var max);
            BuildCorners(min, max);
        }

        // Bottom ring.
        SetEdge(0, 0, 1);
        SetEdge(1, 1, 2);
        SetEdge(2, 2, 3);
        SetEdge(3, 3, 0);
        // Top ring.
        SetEdge(4, 4, 5);
        SetEdge(5, 5, 6);
        SetEdge(6, 6, 7);
        SetEdge(7, 7, 4);
        // Vertical edges.
        SetEdge(8, 0, 4);
        SetEdge(9, 1, 5);
        SetEdge(10, 2, 6);
        SetEdge(11, 3, 7);
    }


    private void RefreshSelectionHighlightVisuals()
    {
        if (!_hasPointA || !_hasPointB || _selectionObjects.Count == 0)
        {
            EnsureSelectionHighlightBoxCount(0);
            return;
        }

        var boundsList = new List<Bounds>(_selectionObjects.Count);
        for (var i = 0; i < _selectionObjects.Count; i++)
        {
            if (!TryGetSelectionHighlightBounds(_selectionObjects[i], out var bounds))
            {
                continue;
            }

            bounds.Expand(0.03f);
            boundsList.Add(bounds);
        }

        EnsureSelectionHighlightBoxCount(boundsList.Count);
        for (var i = 0; i < boundsList.Count; i++)
        {
            SetSelectionHighlightBoxBounds(_selectionHighlightBoxes[i], boundsList[i]);
            if (!_selectionHighlightBoxes[i].Root.activeSelf)
            {
                _selectionHighlightBoxes[i].Root.SetActive(true);
            }
        }
    }

    private bool TryGetSelectionHighlightBounds(SelectedWorldObject selected, out Bounds bounds)
    {
        bounds = default;
        if (selected?.Component == null)
        {
            return false;
        }

        var has = false;
        var colliders = selected.Component.GetComponentsInChildren<Collider>(includeInactive: true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || collider.isTrigger) continue;
            var colliderBounds = collider.bounds;
            if (colliderBounds.size.sqrMagnitude <= 0.000001f) continue;
            if (!has)
            {
                bounds = colliderBounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(colliderBounds.min);
                bounds.Encapsulate(colliderBounds.max);
            }
        }

        if (has)
        {
            return true;
        }

        var renderers = selected.Component.GetComponentsInChildren<Renderer>(includeInactive: true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null) continue;
            var rendererBounds = renderer.bounds;
            if (rendererBounds.size.sqrMagnitude <= 0.000001f) continue;
            if (!has)
            {
                bounds = rendererBounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds.min);
                bounds.Encapsulate(rendererBounds.max);
            }
        }

        if (has)
        {
            return true;
        }

        bounds = new Bounds(new Vector3(
            SnapToCellCenter(selected.Component.transform.position.x),
            SnapToWhole(selected.Component.transform.position.y) + 0.5f,
            SnapToCellCenter(selected.Component.transform.position.z)), Vector3.one * 0.96f);
        return true;
    }

    private void EnsureSelectionHighlightBoxCount(int count)
    {
        if (count < 0) count = 0;

        while (_selectionHighlightBoxes.Count < count)
        {
            _selectionHighlightBoxes.Add(CreateSelectionHighlightBox(_selectionHighlightBoxes.Count));
        }

        for (var i = 0; i < _selectionHighlightBoxes.Count; i++)
        {
            var active = i < count;
            if (_selectionHighlightBoxes[i].Root.activeSelf != active)
            {
                _selectionHighlightBoxes[i].Root.SetActive(active);
            }
        }
    }

    private SelectionHighlightBox CreateSelectionHighlightBox(int index)
    {
        if (_selectionHighlightRoot == null)
        {
            _selectionHighlightRoot = new GameObject("MinersBlueprintSelectionHighlights");
            DontDestroyOnLoad(_selectionHighlightRoot);
        }

        var box = new SelectionHighlightBox();
        box.Root = new GameObject($"Selection_{index}");
        box.Root.transform.SetParent(_selectionHighlightRoot.transform, worldPositionStays: false);

        var material = GetSelectionHighlightLineMaterial();
        for (var i = 0; i < box.Edges.Length; i++)
        {
            var edge = new GameObject($"Edge_{i}");
            edge.transform.SetParent(box.Root.transform, worldPositionStays: false);
            var lr = edge.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
            lr.startWidth = 0.028f;
            lr.endWidth = 0.028f;
            lr.startColor = new Color(0.25f, 1f, 0.25f, 0.92f);
            lr.endColor = new Color(0.25f, 1f, 0.25f, 0.92f);
            lr.material = material;
            box.Edges[i] = lr;
        }

        return box;
    }

    private Material GetSelectionHighlightLineMaterial()
    {
        if (_selectionHighlightLineMaterial != null)
        {
            return _selectionHighlightLineMaterial;
        }

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        _selectionHighlightLineMaterial = new Material(shader);
        _selectionHighlightLineMaterial.hideFlags = HideFlags.HideAndDontSave;
        if (_selectionHighlightLineMaterial.HasProperty("_Color"))
        {
            _selectionHighlightLineMaterial.SetColor("_Color", new Color(0.25f, 1f, 0.25f, 0.92f));
        }

        return _selectionHighlightLineMaterial;
    }

    private static void SetSelectionHighlightBoxBounds(SelectionHighlightBox box, Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;
        var corners = new Vector3[8];
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(max.x, min.y, min.z);
        corners[2] = new Vector3(max.x, min.y, max.z);
        corners[3] = new Vector3(min.x, min.y, max.z);
        corners[4] = new Vector3(min.x, max.y, min.z);
        corners[5] = new Vector3(max.x, max.y, min.z);
        corners[6] = new Vector3(max.x, max.y, max.z);
        corners[7] = new Vector3(min.x, max.y, max.z);

        SetGhostEdge(box.Edges[0], corners[0], corners[1]);
        SetGhostEdge(box.Edges[1], corners[1], corners[2]);
        SetGhostEdge(box.Edges[2], corners[2], corners[3]);
        SetGhostEdge(box.Edges[3], corners[3], corners[0]);
        SetGhostEdge(box.Edges[4], corners[4], corners[5]);
        SetGhostEdge(box.Edges[5], corners[5], corners[6]);
        SetGhostEdge(box.Edges[6], corners[6], corners[7]);
        SetGhostEdge(box.Edges[7], corners[7], corners[4]);
        SetGhostEdge(box.Edges[8], corners[0], corners[4]);
        SetGhostEdge(box.Edges[9], corners[1], corners[5]);
        SetGhostEdge(box.Edges[10], corners[2], corners[6]);
        SetGhostEdge(box.Edges[11], corners[3], corners[7]);
    }

    private void DestroySelectionHighlightVisuals()
    {
        for (var i = 0; i < _selectionHighlightBoxes.Count; i++)
        {
            var box = _selectionHighlightBoxes[i];
            if (box?.Root != null)
            {
                Destroy(box.Root);
            }
        }
        _selectionHighlightBoxes.Clear();

        if (_selectionHighlightRoot != null)
        {
            Destroy(_selectionHighlightRoot);
            _selectionHighlightRoot = null;
        }
    }


    private void SetEdge(int edgeIndex, int cornerA, int cornerB)
    {
        var lr = _selectionEdges[edgeIndex];
        lr.SetPosition(0, _selectionCorners[cornerA]);
        lr.SetPosition(1, _selectionCorners[cornerB]);
    }


    private void BuildCorners(Vector3 min, Vector3 max)
    {
        _selectionCorners[0] = new Vector3(min.x, min.y, min.z);
        _selectionCorners[1] = new Vector3(max.x, min.y, min.z);
        _selectionCorners[2] = new Vector3(max.x, min.y, max.z);
        _selectionCorners[3] = new Vector3(min.x, min.y, max.z);
        _selectionCorners[4] = new Vector3(min.x, max.y, min.z);
        _selectionCorners[5] = new Vector3(max.x, max.y, min.z);
        _selectionCorners[6] = new Vector3(max.x, max.y, max.z);
        _selectionCorners[7] = new Vector3(min.x, max.y, max.z);
    }


    private Vector3Int WorldToCell(Vector3 world)
    {
        var size = Mathf.Max(0.01f, _cellSize.Value);
        return new Vector3Int(
            Mathf.FloorToInt(world.x / size),
            Mathf.FloorToInt(world.y / size),
            Mathf.FloorToInt(world.z / size));
    }


    private Vector3 CellCenter(Vector3Int cell)
    {
        var size = Mathf.Max(0.01f, _cellSize.Value);
        return new Vector3(
            (cell.x + 0.5f) * size,
            (cell.y + 0.5f) * size,
            (cell.z + 0.5f) * size);
    }


    private void GetCellBounds(Vector3Int cell, out Vector3 min, out Vector3 max)
    {
        var size = Mathf.Max(0.01f, _cellSize.Value);
        min = new Vector3(cell.x * size, cell.y * size, cell.z * size);
        max = new Vector3((cell.x + 1) * size, (cell.y + 1) * size, (cell.z + 1) * size);
    }


    private static string BuildSaveableLabel(MonoBehaviour component, ISaveLoadableObject saveable)
    {
        if (component is BuildingObject building)
        {
            return BuildObjectLabel(building);
        }

        var baseName = TryGetInteractableName(component.gameObject, out var interactableName)
            ? interactableName
            : component.gameObject.name.Replace("(Clone)", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = component.GetType().Name;
        }

        return $"{baseName} [{saveable.GetSavableObjectID()}]";
    }


    private static bool TryGetInteractableName(GameObject root, out string name)
    {
        name = string.Empty;
        if (root == null)
        {
            return false;
        }

        var behaviours = root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is not IInteractable interactable)
            {
                continue;
            }

            var objectName = interactable.GetObjectName();
            if (string.IsNullOrWhiteSpace(objectName))
            {
                continue;
            }

            name = objectName.Trim();
            return true;
        }

        return false;
    }

}
