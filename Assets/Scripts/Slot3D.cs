using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// 3D 슬롯 오브젝트에 직접 붙여서 사용할 컴포넌트입니다.
public class Slot3D : MonoBehaviour
{
    public Transform modelContainer;
    public MeshRenderer slotRenderer;
    [Min(0.01f)]
    public float placedModelExtraScale = 10f;

    [Header("AI 확률 표시")]
    public bool showProbabilityLabel = true;
    [Tooltip("판 안쪽으로 일직선 이동(행·열 고정 방향, 월드 거리).")]
    [FormerlySerializedAs("probabilityOutsidePadding")]
    public float probabilityInsidePadding = 2f;
    [Tooltip("타일 윗면 위로 띄우는 높이(월드).")]
    public float probabilityLiftAboveTile = 2f;
    [Tooltip("3D TextMesh characterSize.")]
    public float probabilityCharacterSize = 4f;
    [Tooltip("3D TextMesh fontSize(해상도).")]
    public int probabilityFontSize = 42;

    const int ProbabilityRankMinOpacity = 30;
    const int ProbabilityRankMaxOpacity = 255;
    static readonly Color ProbabilityRankFirstColor = Color.black;              // #000000 (1등)
    static readonly Color ProbabilityRankLowColor = new Color(0f, 0f, 1f, 1f);   // #0000FF (꼴등)
    static readonly Color ProbabilityRankHighColor = new Color(0f, 1f, 1f, 1f); // #00FFFF (2등)
    static readonly Color ProbabilityPlacedLabelColor = Color.white;

    static readonly Vector3 ProbabilityLabelEulerHorizontal = new Vector3(90f, 0f, 0f);
    static readonly Vector3 ProbabilityLabelEulerFirstVertical = new Vector3(90f, -90f, 0f);
    static readonly Vector3 ProbabilityLabelEulerBackVertical = new Vector3(90f, 90f, 0f);

    private GameObject currentNumberModel;
    TextMesh _probabilityLabel;
    Transform _probabilityLabelRoot;
    num_path _board;
    int _slotIndex = -1;

    public System.Action onSlotClick;

    public bool isFilled { get; private set; }
    public string cardValue { get; private set; }

    static readonly Color emptyColor = Color.white;
    static readonly Color filledColor = new Color(0.9f, 0.9f, 0.9f);

    bool _useDimmedPalette;

    public void SetDimmedPalette(bool useDimmedPalette) => _useDimmedPalette = useDimmedPalette;

    public void BindProbabilityLabelRoot(Transform labelRoot, num_path board)
    {
        _probabilityLabelRoot = labelRoot;
        _board = board;

        if (_probabilityLabel != null && labelRoot != null)
            _probabilityLabel.transform.SetParent(labelRoot, true);
    }

    public void SetSlotIndex(int index) => _slotIndex = index;

    /// <summary>AI 보드: 방금 놓은 카드만 원본 색으로 밝게 / 이전 카드는 다시 어둡게.</summary>
    public void SetCardModelBright(bool bright)
    {
        if (!_useDimmedPalette || currentNumberModel == null)
            return;

        var dimmer = StreamsBoardBackgroundDimmer.Instance;
        if (dimmer == null)
            return;

        dimmer.ApplyRelativeBrightnessHierarchy(currentNumberModel.transform, bright ? 1f : 0f);
    }

    void OnMouseDown() => onSlotClick?.Invoke();

    public void SetEmpty()
    {
        isFilled = false;
        cardValue = null;

        if (currentNumberModel != null) Destroy(currentNumberModel);

        if (slotRenderer != null)
            slotRenderer.enabled = true;
        else
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = true;
        }

        foreach (Transform child in transform)
            child.gameObject.SetActive(true);

        Image image = GetComponent<Image>();
        if (image != null) image.enabled = true;

        CanvasRenderer canvasRenderer = GetComponent<CanvasRenderer>();
        if (canvasRenderer != null) canvasRenderer.SetAlpha(1f);

        if (!_useDimmedPalette)
            SetColor(emptyColor);

        ClearProbabilityLabel();
    }

    public void SetProbabilityPercent(float percent)
    {
        SetProbabilityPercent(percent, 1f, 0f, true);
    }

    /// <param name="opacityRelative">이번 턴 빈 칸 순위 기준 0(꼴등)~1(1등).</param>
    /// <param name="colorRelative">2등~꼴등 색 보간 0(#0000FF)~1(#00FFFF). 1등은 무시.</param>
    /// <param name="isHighestRank">1등이면 #000000.</param>
    public void SetProbabilityPercent(float percent, float opacityRelative, float colorRelative, bool isHighestRank)
    {
        if (!showProbabilityLabel)
            return;

        EnsureProbabilityLabel();
        if (_probabilityLabel == null)
            return;

        _probabilityLabel.gameObject.SetActive(true);
        ApplyProbabilityLabelStyle(opacityRelative, colorRelative, isHighestRank);
        _probabilityLabel.text = $"{percent:F5}%";
        RefreshProbabilityLabelTransform();
    }

    void ApplyProbabilityLabelStyle(float opacityRelative, float colorRelative, bool isHighestRank)
    {
        float t = Mathf.Clamp01(opacityRelative);
        int opacityByte = Mathf.RoundToInt(Mathf.Lerp(ProbabilityRankMinOpacity, ProbabilityRankMaxOpacity, t));

        Color tint = isHighestRank
            ? ProbabilityRankFirstColor
            : Color.Lerp(ProbabilityRankLowColor, ProbabilityRankHighColor, Mathf.Clamp01(colorRelative));
        tint.a = opacityByte / 255f;
        _probabilityLabel.color = tint;
    }

    public void RefreshProbabilityLabelIfVisible()
    {
        if (_probabilityLabel == null || !_probabilityLabel.gameObject.activeSelf)
            return;

        RefreshProbabilityLabelTransform();
    }

    public void ClearProbabilityLabel()
    {
        if (_probabilityLabel != null)
            _probabilityLabel.gameObject.SetActive(false);
    }

    /// <summary>타일 설치 턴: 확률 텍스트를 흰색으로 유지. 다음 턴 ShowSlotProbabilities에서 숨김.</summary>
    public void ShowPlacedProbabilityLabel()
    {
        if (_probabilityLabel == null || !_probabilityLabel.gameObject.activeSelf)
            return;

        Color tint = ProbabilityPlacedLabelColor;
        tint.a = 1f;
        _probabilityLabel.color = tint;
        RefreshProbabilityLabelTransform();
    }

    void EnsureProbabilityLabel()
    {
        if (_probabilityLabel != null)
            return;

        if (_probabilityLabelRoot == null)
        {
            if (_board == null)
                _board = GetComponentInParent<num_path>();

            if (_board != null)
                _probabilityLabelRoot = _board.GetProbabilityLabelRoot();
        }

        if (!num_path.IsValidProbabilityLabelRoot(_probabilityLabelRoot) && _board != null)
            _probabilityLabelRoot = _board.GetProbabilityLabelRoot();

        if (_probabilityLabelRoot == null)
        {
            Debug.LogWarning($"Slot3D {name}: AiProbability 루트가 없어 확률 라벨을 만들지 않습니다.");
            return;
        }

        var go = new GameObject($"AiProbabilityLabel_{name}");
        go.transform.SetParent(_probabilityLabelRoot, false);
        go.transform.localScale = Vector3.one;

        _probabilityLabel = go.AddComponent<TextMesh>();
        _probabilityLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _probabilityLabel.characterSize = probabilityCharacterSize;
        _probabilityLabel.fontSize = probabilityFontSize;
        _probabilityLabel.anchor = TextAnchor.MiddleCenter;
        _probabilityLabel.alignment = TextAlignment.Center;
        _probabilityLabel.color = ProbabilityRankFirstColor;
        _probabilityLabel.text = "";
        SetupTransparentLabelMaterial(_probabilityLabel);
        go.SetActive(false);
    }

    static void SetupTransparentLabelMaterial(TextMesh text)
    {
        if (text == null)
            return;

        var renderer = text.GetComponent<MeshRenderer>();
        if (renderer == null || text.font == null)
            return;

        Texture fontTexture = text.font.material != null ? text.font.material.mainTexture : null;

        Shader shader = Shader.Find("GUI/Text Shader");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        if (fontTexture != null)
            mat.mainTexture = fontTexture;

        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        SetMaterialTintWhite(mat);
        renderer.material = mat;
        renderer.sortingOrder = 5000;
    }

    static void SetMaterialTintWhite(Material mat)
    {
        if (mat == null)
            return;

        mat.color = Color.white;
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
    }

    void RefreshProbabilityLabelTransform()
    {
        if (_probabilityLabel == null)
            return;

        if (_board == null && _probabilityLabelRoot != null)
            _board = _probabilityLabelRoot.GetComponentInParent<num_path>();

        Vector3 worldPos = ComputeProbabilityLabelWorldPosition();

        _probabilityLabel.transform.SetParent(_probabilityLabelRoot, true);
        _probabilityLabel.transform.position = worldPos;
        _probabilityLabel.transform.rotation = GetProbabilityLabelRotation();
        _probabilityLabel.transform.localScale = Vector3.one;
    }

    Vector3 ComputeProbabilityLabelWorldPosition()
    {
        Transform slotAnchor = modelContainer != null ? modelContainer : transform;
        Transform boardAnchor = null;

        if (_board != null)
            boardAnchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(_board.transform);
        if (boardAnchor == null)
            boardAnchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(transform);

        Vector3 worldPos;

        if (boardAnchor != null)
        {
            Vector3 slotLocal = boardAnchor.InverseTransformPoint(slotAnchor.position);
            Vector3 inwardLocal = GetInwardOffsetLocal(ResolveSlotIndex(), probabilityInsidePadding);
            Vector3 labelLocal = slotLocal + inwardLocal;
            worldPos = boardAnchor.TransformPoint(labelLocal);
        }
        else
        {
            Vector3 center = _board != null ? _board.GetBoardCenterWorld() : transform.position;
            Vector3 toCenter = center - slotAnchor.position;
            toCenter.y = 0f;
            float dist = toCenter.magnitude;
            worldPos = slotAnchor.position;
            if (dist > 0.0001f)
            {
                float move = Mathf.Clamp(probabilityInsidePadding, 0f, dist);
                worldPos += toCenter.normalized * move;
            }
        }

        worldPos.y = GetProbabilityLabelWorldY();
        return worldPos;
    }

    /// <summary>보드 로컬 XZ에서 판 안쪽(일직선) 오프셋. 슬롯 행·열과 num_path.IsVerticalSlot 구간과 동일.</summary>
    static Vector3 GetInwardOffsetLocal(int slotIndex, float padding)
    {
        if (padding <= 0f || slotIndex < 0)
            return Vector3.zero;

        // 왼쪽 세로 0~4 → +X (오른쪽)
        if (slotIndex >= 0 && slotIndex <= 4)
            return new Vector3(padding, 0f, 0f);

        // 위 가로 5~9 → -Z (아래)
        if (slotIndex >= 5 && slotIndex <= 9)
            return new Vector3(0f, 0f, -padding);

        // 오른쪽 세로 10~15 → -X (왼쪽)
        if (slotIndex >= 10 && slotIndex <= 15)
            return new Vector3(-padding, 0f, 0f);

        // 아래 가로 16~19 → +Z (위)
        if (slotIndex >= 16 && slotIndex <= 19)
            return new Vector3(0f, 0f, padding);

        return Vector3.zero;
    }

    Quaternion GetProbabilityLabelRotation()
    {
        int idx = ResolveSlotIndex();

        if (idx >= 0 && idx <= 4)
            return Quaternion.Euler(ProbabilityLabelEulerFirstVertical);

        if (idx >= 10 && idx <= 15)
            return Quaternion.Euler(ProbabilityLabelEulerBackVertical);

        return Quaternion.Euler(ProbabilityLabelEulerHorizontal);
    }

    int ResolveSlotIndex()
    {
        if (_slotIndex >= 0)
            return _slotIndex;

        if (_board?.slots == null)
            return -1;

        return _board.slots.IndexOf(this);
    }

    float GetProbabilityLabelWorldY()
    {
        Transform anchor = modelContainer != null ? modelContainer : transform;
        return anchor.position.y + probabilityLiftAboveTile;
    }

    public void SetCard(GameObject prefab, string value)
    {
        isFilled = true;
        cardValue = value;

        if (currentNumberModel != null) Destroy(currentNumberModel);
        if (prefab != null && modelContainer != null)
        {
            Vector3 prefabLocalScale = prefab.transform.localScale;
            currentNumberModel = Instantiate(prefab, modelContainer.position, modelContainer.rotation);
            currentNumberModel.transform.SetParent(modelContainer, true);
            StreamsCardModelScale.Apply(currentNumberModel.transform, modelContainer, prefabLocalScale, placedModelExtraScale);
        }

        Image image = GetComponent<Image>();
        if (image != null) image.enabled = false;

        CanvasRenderer canvasRenderer = GetComponent<CanvasRenderer>();
        if (canvasRenderer != null) canvasRenderer.SetAlpha(0f);

        SetColor(filledColor);
    }

    public void PlaceExistingObject(GameObject obj, string value, Quaternion rotation)
    {
        isFilled = true;
        cardValue = value;

        if (currentNumberModel != null) Destroy(currentNumberModel);

        currentNumberModel = obj;
        Transform parent = modelContainer != null ? modelContainer : transform;
        Vector3 prefabLocalScale = obj.transform.localScale;
        Quaternion worldRot = parent.rotation * rotation;
        currentNumberModel.transform.SetPositionAndRotation(parent.position, worldRot);
        currentNumberModel.transform.SetParent(parent, true);
        StreamsCardModelScale.Apply(currentNumberModel.transform, parent, prefabLocalScale, placedModelExtraScale);

        if (slotRenderer != null)
            slotRenderer.enabled = false;
        else
        {
            var myRenderer = GetComponent<MeshRenderer>();
            if (myRenderer != null) myRenderer.enabled = false;
        }

        foreach (Transform child in transform)
        {
            if (child != modelContainer && child != obj.transform)
                child.gameObject.SetActive(false);
        }

        ShowPlacedProbabilityLabel();

        if (_useDimmedPalette)
        {
            var dimmer = StreamsBoardBackgroundDimmer.Instance;
            if (dimmer != null)
            {
                dimmer.CaptureHierarchyPalette(currentNumberModel.transform);
                dimmer.ApplyRelativeBrightnessHierarchy(currentNumberModel.transform, 0f);
            }
        }
        else
        {
            SetColor(filledColor);
        }
    }

    public void SetColor(Color color)
    {
        var dimmer = _useDimmedPalette ? StreamsBoardBackgroundDimmer.Instance : null;

        if (dimmer != null)
        {
            // AI 보드 빈 슬롯 배경은 StreamsBoardBackgroundDimmer.Apply()가 균일하게 처리합니다.
            if (currentNumberModel != null)
                ApplyTintToModel(dimmer, color);

            return;
        }

        if (slotRenderer != null && !isFilled)
            SetMaterialColor(slotRenderer.material, color);

        if (currentNumberModel == null)
            return;

        foreach (var r in currentNumberModel.GetComponentsInChildren<Renderer>(true))
        {
            if (ShouldSkipTintingDigits(r))
                continue;

            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && ShouldSkipTintingMaterial(mats[i]))
                    continue;
                SetMaterialColor(mats[i], color);
            }
            r.materials = mats;
        }
    }

    void ApplyTintToModel(StreamsBoardBackgroundDimmer dimmer, Color color)
    {
        foreach (var r in currentNumberModel.GetComponentsInChildren<Renderer>(true))
        {
            if (ShouldSkipTintingDigits(r))
                continue;

            if (_useDimmedPalette)
            {
                dimmer.ApplyHighlightTint(r, color);
                continue;
            }

            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && ShouldSkipTintingMaterial(mats[i]))
                    continue;
                SetMaterialColor(mats[i], color);
            }
            r.materials = mats;
        }
    }

    static void SetMaterialColor(Material mat, Color color)
    {
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
        mat.color = color;
    }

    static bool ShouldSkipTintingMaterial(Material mat)
    {
        if (mat == null || mat.shader == null)
            return false;
        string n = mat.shader.name;
        if (n.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("TMPro", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("GUI/Text", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return false;
    }

    static bool ContainsTextModelName(string objName) =>
        !string.IsNullOrEmpty(objName)
        && objName.IndexOf("textmodel", StringComparison.OrdinalIgnoreCase) >= 0;

    static bool LooksLikeDedicatedTextMeshName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Contains("텍스트")) return true;
        if (string.Equals(name, "Text", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(name, "text", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_Text", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("Text_", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("Text", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    bool ShouldSkipTintingDigits(Renderer r)
    {
        if (r.GetComponent<TextMesh>() != null)
            return true;

        foreach (var comp in r.GetComponents<Component>())
        {
            if (comp == null) continue;
            string tn = comp.GetType().Name;
            if (tn == "TextMeshPro" || tn == "TextMeshProUGUI" || tn.Contains("TMP_Text"))
                return true;
        }

        for (Transform t = r.transform; t != null && t != transform; t = t.parent)
        {
            if (ContainsTextModelName(t.name) || LooksLikeDedicatedTextMeshName(t.name))
                return true;
        }

        return false;
    }
}
