using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>2D 칸. 빈 칸 / 꽂힘 스프라이트를 갈아끼우고, 안내는 점선 사각형으로 표시합니다.</summary>
public class StreamsUiSlot : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
{
    public Image targetImage;

    [Header("스프라이트")]
    public Sprite emptySprite;
    [Tooltip("사용하지 않습니다. 안내는 점선 사각형으로 표시합니다.")]
    public Sprite hintSprite;
    public Sprite filledSprite;

    [Header("숫자")]
    public TextMeshProUGUI numberText;

    [Header("안내 사각형")]
    [Tooltip("점선 색. 기본은 검은색.")]
    public Color hintColor = Color.black;

    [Header("클릭 범위 (512 스프라이트 기준 여백)")]
    [Tooltip("켜면 카드가 놓이는 영역만 클릭됩니다. 끄면 512 전체입니다.")]
    public bool useCustomHitArea = true;
    [Tooltip("왼쪽 여백(px). 확장 전 기준값.")]
    public float hitPaddingLeft = 120f;
    [Tooltip("오른쪽 여백(px). 확장 전 기준값.")]
    public float hitPaddingRight = 120f;
    [Tooltip("위 여백(px). 확장 전 기준값.")]
    public float hitPaddingTop = 104f;
    [Tooltip("아래 여백(px). 확장 전 기준값.")]
    public float hitPaddingBottom = 192f;
    [Tooltip("클릭 칸을 스프라이트 기준으로 한 변당 넓히는 픽셀.")]
    public float hitExpand = 24f;

    const float VisualSpriteSize = 512f;
    const float VisualPadLeft = 120f;
    const float VisualPadRight = 120f;
    const float VisualPadTop = 104f;
    const float VisualPadBottom = 232f;

    [Header("클릭 범위 확인")]
    [Tooltip("잠시 켜 두면 클릭 칸이 반투명으로 보입니다.")]
    public bool debugShowHitArea = false;

    public bool isFilled { get; private set; }
    public string cardValue { get; private set; }

    public bool IsHoldingPreview
    {
        get
        {
            Transform parent = transform.parent;
            return parent != null && parent.name == "PlayerBoard";
        }
    }

    const float FrontRestoreDelay = 0.2f;

    Image _hitArea;
    int _slotIndex = -1;
    System.Action<int> _onPlace;
    Button _button;
    int _restSibling;
    bool _orderStored;
    Coroutine _restoreOrder;
    static StreamsUiSlot _frontSlot;

    void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
        if (numberText != null)
            numberText.raycastTarget = false;
        EnsureHitArea();
        Transform legacyHint = transform.Find("HintRect");
        if (legacyHint != null)
            Destroy(legacyHint.gameObject);
        SetEmpty();
    }

    void OnDisable()
    {
        StopRestoreOrder();
        RestoreDrawOrder();
        StreamsDashedRectHint.SetSlotHint(this, false, hintColor);
    }

    public void BindPlacement(int index, System.Action<int> onPlace, bool playerControlled)
    {
        _slotIndex = index;
        _onPlace = onPlace;
        EnsureHitArea();
        _button = GetComponent<Button>();
        if (_button == null)
            _button = gameObject.AddComponent<Button>();
        _button.targetGraphic = targetImage != null ? targetImage : _button.targetGraphic;
        _button.onClick.RemoveListener(HandlePlaceClick);
        _button.onClick.AddListener(HandlePlaceClick);
        _button.interactable = playerControlled;
    }

    void HandlePlaceClick()
    {
        if (StreamsCardDrawCinematic.IsBlockingPlacement)
            return;
        if (IsHoldingPreview || isFilled)
            return;
        _onPlace?.Invoke(_slotIndex);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanBringToFront())
            return;
        StopRestoreOrder();
        BringToFront();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ScheduleRestoreDrawOrder();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ScheduleRestoreDrawOrder();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        HandlePlaceClick();
    }

    public void SetEmpty()
    {
        isFilled = false;
        cardValue = null;
        ApplySprite(emptySprite);
        SetNumber("");
        ShowHint(false);
    }

    public void SetHint()
    {
        if (isFilled || IsOnAiBoard() || IsHoldingPreview)
            return;
        ApplySprite(emptySprite);
        SetNumber("");
        ShowHint(true);
    }

    public void SetFilled(string value)
    {
        isFilled = true;
        cardValue = value;
        ApplySprite(filledSprite);
        SetNumber(string.IsNullOrEmpty(value) ? "" : value);
        ShowHint(false);
    }

    void ShowHint(bool on)
    {
        StreamsDashedRectHint.SetSlotHint(this, on, hintColor);
        EnsureHitArea();
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            return;
        EnsureHitArea();
    }

    void EnsureHitArea()
    {
        if (targetImage == null)
            return;

        targetImage.raycastPadding = Vector4.zero;

        if (!useCustomHitArea)
        {
            targetImage.raycastTarget = true;
            if (_hitArea != null)
                _hitArea.gameObject.SetActive(false);
            return;
        }

        targetImage.raycastTarget = false;
        if (_hitArea == null)
            _hitArea = CreateHitArea();

        _hitArea.gameObject.SetActive(true);
        _hitArea.raycastTarget = true;
        _hitArea.canvasRenderer.cullTransparentMesh = false;
        _hitArea.color = debugShowHitArea && !IsHoldingPreview
            ? new Color(1f, 0.2f, 0.7f, 0.35f)
            : new Color(1f, 1f, 1f, 1f / 255f);
        float expand = Mathf.Max(0f, hitExpand);
        FitSpriteRect(
            _hitArea.rectTransform,
            Mathf.Max(0f, hitPaddingLeft - expand),
            Mathf.Max(0f, hitPaddingRight - expand),
            Mathf.Max(0f, hitPaddingTop - expand),
            Mathf.Max(0f, hitPaddingBottom - expand));
        _hitArea.transform.SetAsLastSibling();
    }

    Image CreateHitArea()
    {
        Transform existing = transform.Find("HitArea");
        if (existing == null)
            existing = transform.Find("DebugHitArea");
        if (existing != null)
        {
            var image = existing.GetComponent<Image>();
            if (image == null)
                image = existing.gameObject.AddComponent<Image>();
            existing.name = "HitArea";
            return image;
        }

        var go = new GameObject("HitArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(transform, false);
        return go.GetComponent<Image>();
    }

    bool CanBringToFront()
    {
        return isActiveAndEnabled
            && !StreamsCardDrawCinematic.IsBlockingPlacement
            && !IsHoldingPreview
            && (_button == null || _button.interactable);
    }

    void BringToFront()
    {
        if (_frontSlot != null && _frontSlot != this)
            _frontSlot.RestoreDrawOrder();

        if (!_orderStored)
        {
            _restSibling = transform.GetSiblingIndex();
            _orderStored = true;
        }

        transform.SetAsLastSibling();
        _frontSlot = this;
    }

    void ScheduleRestoreDrawOrder()
    {
        if (!_orderStored)
            return;

        StopRestoreOrder();
        _restoreOrder = StartCoroutine(RestoreDrawOrderAfterDelay());
    }

    System.Collections.IEnumerator RestoreDrawOrderAfterDelay()
    {
        yield return new WaitForSecondsRealtime(FrontRestoreDelay);
        RestoreDrawOrder();
        _restoreOrder = null;
    }

    void RestoreDrawOrder()
    {
        if (!_orderStored)
            return;

        transform.SetSiblingIndex(_restSibling);
        _orderStored = false;
        if (_frontSlot == this)
            _frontSlot = null;
    }

    void StopRestoreOrder()
    {
        if (_restoreOrder == null)
            return;
        StopCoroutine(_restoreOrder);
        _restoreOrder = null;
    }

    static void FitSpriteRect(RectTransform rt, float left, float right, float top, float bottom)
    {
        const float sprite = 512f;
        var parent = rt.parent as RectTransform;
        float width = parent != null ? parent.rect.width : sprite;
        float height = parent != null ? parent.rect.height : sprite;
        float sx = width / sprite;
        float sy = height / sprite;

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.offsetMin = new Vector2(left * sx, bottom * sy);
        rt.offsetMax = new Vector2(-right * sx, -top * sy);
    }

    static bool IsOnAiBoard(Transform start)
    {
        for (Transform t = start; t != null; t = t.parent)
        {
            if (t.name == "AIBoard")
                return true;
        }

        return false;
    }

    bool IsOnAiBoard() => IsOnAiBoard(transform);

    void ApplySprite(Sprite sprite)
    {
        if (targetImage == null || sprite == null)
            return;
        targetImage.sprite = sprite;
    }

    void SetNumber(string value)
    {
        if (numberText == null)
            return;
        numberText.text = value;
    }

    /// <summary>카드 그림이 있는 영역(512 스프라이트에서 투명 여백을 뺀 네 모서리, 월드).</summary>
    public bool TryGetVisualWorldCorners(Vector3[] corners)
    {
        if (corners == null || corners.Length < 4)
            return false;

        var rt = transform as RectTransform;
        if (rt == null)
            return false;

        Rect sr = rt.rect;
        float sx = sr.width / VisualSpriteSize;
        float sy = sr.height / VisualSpriteSize;
        corners[0] = rt.TransformPoint(new Vector3(sr.xMin + VisualPadLeft * sx, sr.yMin + VisualPadBottom * sy, 0f));
        corners[1] = rt.TransformPoint(new Vector3(sr.xMin + VisualPadLeft * sx, sr.yMax - VisualPadTop * sy, 0f));
        corners[2] = rt.TransformPoint(new Vector3(sr.xMax - VisualPadRight * sx, sr.yMax - VisualPadTop * sy, 0f));
        corners[3] = rt.TransformPoint(new Vector3(sr.xMax - VisualPadRight * sx, sr.yMin + VisualPadBottom * sy, 0f));
        return true;
    }
}
