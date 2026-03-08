namespace MinersBlueprint;

public sealed partial class MinersBlueprint
{
    private void ToggleGhostPreview()
    {
        if (_ghostPreviewVisible)
        {
            HideGhostPreview(showToast: true);
            return;
        }

        PlaceGhostAtPlayerAnchor(showToast: true);
    }


    private void RebuildGhostPreviewNow()
    {
        if (!_ghostPreviewVisible) return;
        if (float.IsNaN(_ghostPreviewAnchor.x) || float.IsNaN(_ghostPreviewAnchor.y) || float.IsNaN(_ghostPreviewAnchor.z))
        {
            return;
        }
        ShowGhostPreviewAtAnchor(_ghostPreviewAnchor, showToast: false);
    }


    private void PlaceGhostAtPlayerAnchor(bool showToast)
    {
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (player == null)
        {
            if (showToast)
            {
                _toasts.Push("Player not found; cannot place ghost.", ToastType.Warning);
            }
            return;
        }

        var anchor = SnapPasteAnchor(player.transform.position);
        ShowGhostPreviewAtAnchor(anchor, showToast);
    }


    private void ShowGhostPreviewAtAnchor(Vector3 anchor, bool showToast)
    {
        if (_clipboard == null || _clipboard.Entries.Count == 0)
        {
            if (showToast)
            {
                _toasts.Push("Clipboard is empty. Copy first.", ToastType.Warning);
            }
            _ghostPreviewVisible = false;
            DestroyGhostPreviewVisuals();
            return;
        }

        var saving = Singleton<SavingLoadingManager>.Instance;
        if (saving == null)
        {
            if (showToast)
            {
                _toasts.Push("SavingLoadingManager missing; cannot show ghost preview.", ToastType.Warning);
            }
            _ghostPreviewVisible = false;
            DestroyGhostPreviewVisuals();
            return;
        }

        anchor = new Vector3(
            SnapToCellCenter(anchor.x),
            SnapToWhole(anchor.y),
            SnapToCellCenter(anchor.z));

        DestroyGhostPreviewVisuals();
        _ghostPreviewRoot = new GameObject("MinersBlueprintGhostPreview");
        DontDestroyOnLoad(_ghostPreviewRoot);

        var spawned = 0;
        for (var i = 0; i < _clipboard.Entries.Count; i++)
        {
            var entry = _clipboard.Entries[i];
            if (!TryGetPlacementPrefab(saving, entry, out var prefab))
            {
                continue;
            }

            var ghost = Instantiate(prefab, anchor + entry.RelativeOffset, entry.Rotation, _ghostPreviewRoot.transform);
            ghost.name = $"Ghost_{prefab.name}_{i}";
            PrepareGhostVisual(ghost);
            _ghostPreviewInstances.Add(new GhostPreviewInstance
            {
                Root = ghost,
                RelativeOffset = entry.RelativeOffset
            });
            spawned++;
        }

        if (spawned <= 0)
        {
            _ghostPreviewVisible = false;
            DestroyGhostPreviewVisuals();
            if (showToast)
            {
                _toasts.Push("No valid prefabs for ghost preview.", ToastType.Warning);
            }
            return;
        }

        _ghostPreviewVisible = true;
        _ghostPreviewAnchor = anchor;
        _nextGhostBlockerRefreshTime = 0f;
        RefreshGhostBlockerPreview(force: true);
        if (showToast)
        {
            _toasts.Push($"Ghost anchor placed ({spawned} objects). Use arrows / +/- to move.", ToastType.Success);
        }
    }


    private void HideGhostPreview(bool showToast = false)
    {
        if (!_ghostPreviewVisible && _ghostPreviewInstances.Count == 0) return;
        _ghostPreviewVisible = false;
        DestroyGhostPreviewVisuals();
        if (showToast)
        {
            _toasts.Push("Ghost preview hidden.", ToastType.Success);
        }
    }


    private void DestroyGhostPreviewVisuals()
    {
        for (var i = 0; i < _ghostPreviewInstances.Count; i++)
        {
            var root = _ghostPreviewInstances[i].Root;
            if (root != null)
            {
                Destroy(root);
            }
        }
        _ghostPreviewInstances.Clear();

        if (_ghostPreviewRoot != null)
        {
            Destroy(_ghostPreviewRoot);
            _ghostPreviewRoot = null;
        }

        _ghostPreviewAnchor = new Vector3(float.NaN, float.NaN, float.NaN);
        DestroyGhostBlockerPreviewVisuals();
    }


    private void UpdateGhostPreviewVisual()
    {
        if (!_ghostPreviewVisible) return;
        if (_clipboard == null || _clipboard.Entries.Count == 0)
        {
            HideGhostPreview();
            return;
        }
        if (_ghostPreviewInstances.Count == 0)
        {
            RebuildGhostPreviewNow();
            return;
        }

        var delta = Vector3.zero;
        if (WasPressed(_ghostMoveXMinusKey)) delta.x -= 1f;
        if (WasPressed(_ghostMoveXPlusKey)) delta.x += 1f;
        if (WasPressed(_ghostMoveZMinusKey)) delta.z -= 1f;
        if (WasPressed(_ghostMoveZPlusKey)) delta.z += 1f;
        if (WasPressed(_ghostMoveYMinusKey)) delta.y -= 1f;
        if (WasPressed(_ghostMoveYPlusKey)) delta.y += 1f;

        if (delta.sqrMagnitude <= 0.000001f)
        {
            RefreshGhostBlockerPreview(force: false);
            return;
        }

        _ghostPreviewAnchor += delta;
        _ghostPreviewAnchor = new Vector3(
            SnapToCellCenter(_ghostPreviewAnchor.x),
            SnapToWhole(_ghostPreviewAnchor.y),
            SnapToCellCenter(_ghostPreviewAnchor.z));

        ApplyGhostPreviewAnchor();
        RefreshGhostBlockerPreview(force: true);
    }


    private void ApplyGhostPreviewAnchor()
    {
        for (var i = 0; i < _ghostPreviewInstances.Count; i++)
        {
            var ghost = _ghostPreviewInstances[i];
            if (ghost.Root == null) continue;
            ghost.Root.transform.position = _ghostPreviewAnchor + ghost.RelativeOffset;
        }
    }


    private void RefreshGhostBlockerPreview(bool force)
    {
        if (!_ghostPreviewVisible || _clipboard == null || _clipboard.Entries.Count == 0 || _ghostPreviewInstances.Count == 0)
        {
            DestroyGhostBlockerPreviewVisuals();
            return;
        }

        var now = Time.unscaledTime;
        if (!force && now < _nextGhostBlockerRefreshTime)
        {
            return;
        }
        _nextGhostBlockerRefreshTime = now + 0.12f;

        var saving = Singleton<SavingLoadingManager>.Instance;
        if (saving == null)
        {
            DestroyGhostBlockerPreviewVisuals();
            return;
        }

        var blockers = new HashSet<BuildingObject>();
        var protectedPlacedIds = new HashSet<int>();
        for (var i = 0; i < _clipboard.Entries.Count; i++)
        {
            var entry = _clipboard.Entries[i];
            var targetPos = _ghostPreviewAnchor + entry.RelativeOffset;
            var obstructing = FindObstructingBuildings(saving, entry, targetPos, protectedPlacedIds);
            for (var j = 0; j < obstructing.Count; j++)
            {
                var b = obstructing[j];
                if (b == null || b.IsGhost) continue;
                if (b.GetComponentInParent<GhostPreviewMarker>() != null) continue;
                blockers.Add(b);
            }
        }

        if (blockers.Count == 0)
        {
            EnsureGhostBlockerBoxCount(0);
            return;
        }

        var boundsList = new List<Bounds>(blockers.Count);
        foreach (var blocker in blockers)
        {
            if (!TryGetBlockerBounds(blocker, out var bounds)) continue;
            bounds.Expand(0.04f);
            boundsList.Add(bounds);
        }

        EnsureGhostBlockerBoxCount(boundsList.Count);
        for (var i = 0; i < boundsList.Count; i++)
        {
            SetGhostBlockerBoxBounds(_ghostBlockerBoxes[i], boundsList[i]);
            if (!_ghostBlockerBoxes[i].Root.activeSelf)
            {
                _ghostBlockerBoxes[i].Root.SetActive(true);
            }
        }
    }


    private bool TryGetBlockerBounds(BuildingObject blocker, out Bounds bounds)
    {
        bounds = default;
        var has = false;

        var cols = blocker.GetComponentsInChildren<Collider>(includeInactive: true);
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (col == null || col.isTrigger) continue;
            var b = col.bounds;
            if (b.size.sqrMagnitude <= 0.000001f) continue;
            if (!has)
            {
                bounds = b;
                has = true;
            }
            else
            {
                bounds.Encapsulate(b.min);
                bounds.Encapsulate(b.max);
            }
        }

        if (has) return true;

        var renderers = blocker.GetComponentsInChildren<Renderer>(includeInactive: true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var b = r.bounds;
            if (b.size.sqrMagnitude <= 0.000001f) continue;
            if (!has)
            {
                bounds = b;
                has = true;
            }
            else
            {
                bounds.Encapsulate(b.min);
                bounds.Encapsulate(b.max);
            }
        }

        if (has) return true;

        bounds = new Bounds(new Vector3(
            SnapToCellCenter(blocker.transform.position.x),
            SnapToWhole(blocker.transform.position.y) + 0.5f,
            SnapToCellCenter(blocker.transform.position.z)), Vector3.one * 0.98f);
        return true;
    }


    private void EnsureGhostBlockerBoxCount(int count)
    {
        if (count < 0) count = 0;

        while (_ghostBlockerBoxes.Count < count)
        {
            _ghostBlockerBoxes.Add(CreateGhostBlockerBox(_ghostBlockerBoxes.Count));
        }

        for (var i = 0; i < _ghostBlockerBoxes.Count; i++)
        {
            var active = i < count;
            if (_ghostBlockerBoxes[i].Root.activeSelf != active)
            {
                _ghostBlockerBoxes[i].Root.SetActive(active);
            }
        }
    }


    private GhostBlockerBox CreateGhostBlockerBox(int index)
    {
        if (_ghostBlockerPreviewRoot == null)
        {
            _ghostBlockerPreviewRoot = new GameObject("MinersBlueprintGhostBlockers");
            DontDestroyOnLoad(_ghostBlockerPreviewRoot);
        }

        var box = new GhostBlockerBox();
        box.Root = new GameObject($"Blocker_{index}");
        box.Root.transform.SetParent(_ghostBlockerPreviewRoot.transform, worldPositionStays: false);

        var material = GetGhostBlockerLineMaterial();
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
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.startColor = new Color(1f, 0.22f, 0.22f, 0.95f);
            lr.endColor = new Color(1f, 0.22f, 0.22f, 0.95f);
            lr.material = material;
            box.Edges[i] = lr;
        }

        return box;
    }


    private Material GetGhostBlockerLineMaterial()
    {
        if (_ghostBlockerLineMaterial != null)
        {
            return _ghostBlockerLineMaterial;
        }

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        _ghostBlockerLineMaterial = new Material(shader);
        _ghostBlockerLineMaterial.hideFlags = HideFlags.HideAndDontSave;
        if (_ghostBlockerLineMaterial.HasProperty("_Color"))
        {
            _ghostBlockerLineMaterial.SetColor("_Color", new Color(1f, 0.22f, 0.22f, 0.95f));
        }
        return _ghostBlockerLineMaterial;
    }


    private static void SetGhostBlockerBoxBounds(GhostBlockerBox box, Bounds bounds)
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


    private static void SetGhostEdge(LineRenderer lr, Vector3 a, Vector3 b)
    {
        if (lr == null) return;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }


    private void DestroyGhostBlockerPreviewVisuals()
    {
        for (var i = 0; i < _ghostBlockerBoxes.Count; i++)
        {
            var box = _ghostBlockerBoxes[i];
            if (box?.Root != null)
            {
                Destroy(box.Root);
            }
        }
        _ghostBlockerBoxes.Clear();

        if (_ghostBlockerPreviewRoot != null)
        {
            Destroy(_ghostBlockerPreviewRoot);
            _ghostBlockerPreviewRoot = null;
        }
        _nextGhostBlockerRefreshTime = 0f;
    }


    private void PrepareGhostVisual(GameObject root)
    {
        if (root == null) return;
        if (root.GetComponent<GhostPreviewMarker>() == null)
        {
            root.AddComponent<GhostPreviewMarker>();
        }
        SetLayerRecursively(root.transform, LayerMask.NameToLayer("Ignore Raycast"));

        var colliders = root.GetComponentsInChildren<Collider>(includeInactive: true);
        for (var i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        var rigidBodies = root.GetComponentsInChildren<Rigidbody>(includeInactive: true);
        for (var i = 0; i < rigidBodies.Length; i++)
        {
            rigidBodies[i].isKinematic = true;
            rigidBodies[i].detectCollisions = false;
        }

        var behaviours = root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null) continue;
            behaviour.enabled = false;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        var ghostMaterial = GetGhostMaterial();
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null) continue;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                renderer.sharedMaterial = ghostMaterial;
                continue;
            }

            for (var m = 0; m < mats.Length; m++)
            {
                mats[m] = ghostMaterial;
            }
            renderer.sharedMaterials = mats;
        }
    }


    private Material GetGhostMaterial()
    {
        if (_ghostMaterial != null)
        {
            return _ghostMaterial;
        }

        var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        _ghostMaterial = new Material(shader);
        _ghostMaterial.hideFlags = HideFlags.HideAndDontSave;

        var ghostColor = new Color(0.2f, 0.9f, 1f, 0.38f);
        if (_ghostMaterial.HasProperty("_Color"))
        {
            _ghostMaterial.SetColor("_Color", ghostColor);
        }
        if (_ghostMaterial.HasProperty("_BaseColor"))
        {
            _ghostMaterial.SetColor("_BaseColor", ghostColor);
        }
        ConfigureMaterialForTransparency(_ghostMaterial);
        return _ghostMaterial;
    }


    private static void ConfigureMaterialForTransparency(Material mat)
    {
        if (mat == null) return;

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
        }
        if (mat.HasProperty("_SrcBlend"))
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }
        if (mat.HasProperty("_DstBlend"))
        {
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }
        if (mat.HasProperty("_ZWrite"))
        {
            mat.SetFloat("_ZWrite", 0f);
        }
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }


    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null || layer < 0) return;
        root.gameObject.layer = layer;
        for (var i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
