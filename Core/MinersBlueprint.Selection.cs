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
            _toasts.Push("No valid target for start point.", ToastType.Warning);
            return;
        }

        _pointA = point;
        _cellA = WorldToCell(point);
        _pointA = CellCenter(_cellA);
        _hasPointA = true;
        _hasPointB = false;
        _selectionObjects.Clear();
        _toasts.Push($"Start point set: {FormatVec(_pointA)}", ToastType.Success);
    }


    private void SetEndPoint()
    {
        if (!_hasPointA)
        {
            _toasts.Push("Set start point first (Key 1).", ToastType.Warning);
            return;
        }
        if (!TryGetLookedPoint(out var point, out _))
        {
            _toasts.Push("No valid target for end point.", ToastType.Warning);
            return;
        }

        _pointB = point;
        _cellB = WorldToCell(point);
        _pointB = CellCenter(_cellB);
        _hasPointB = true;
        RebuildSelectionBounds();
        RefreshSelection();
        _toasts.Push($"Selection ready: {_selectionObjects.Count} objects.", ToastType.Success);
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
        if (!_hasPointA || !_hasPointB) return;

        var all = UnityEngine.Object.FindObjectsByType<BuildingObject>(FindObjectsSortMode.None);
        for (var i = 0; i < all.Length; i++)
        {
            var obj = all[i];
            if (obj == null || obj.IsGhost) continue;
            if (!IsObjectInSelection(obj)) continue;
            _selectionObjects.Add(obj);
            var id = obj.SavableObjectID;
            if (!_nameByIdCache.ContainsKey(id))
            {
                _nameByIdCache[id] = BuildObjectLabel(obj);
            }
        }
    }


    private bool IsPointInSelection(Vector3 point)
    {
        var min = _selectionBounds.min;
        var max = _selectionBounds.max;
        return point.x >= min.x && point.y >= min.y && point.z >= min.z &&
               point.x <= max.x && point.y <= max.y && point.z <= max.z;
    }


    private bool IsObjectInSelection(BuildingObject obj)
    {
        var cell = WorldToCell(obj.transform.position);
        return cell.x >= _selectionCellMin.x && cell.x <= _selectionCellMax.x &&
               cell.y >= _selectionCellMin.y && cell.y <= _selectionCellMax.y &&
               cell.z >= _selectionCellMin.z && cell.z <= _selectionCellMax.z;
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

}
