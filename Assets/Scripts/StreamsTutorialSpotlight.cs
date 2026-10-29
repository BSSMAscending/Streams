using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼용 화면 딤. 지정한 RectTransform만 구멍으로 남기고 안내 문구를 띄웁니다.
/// </summary>
[DefaultExecutionOrder(2000)]
public class StreamsTutorialSpotlight : MonoBehaviour, IPointerClickHandler
{
    const int SortingOrder = 40;
    const float DimAlpha = 0.80f;
    const float HolePadding = 16f;
    const float SlotSpritePadBottomExtra = 32f;
    const float TmpHolePadding = 8f;
    const float ImageHolePadding = 10f;
    const float ButtonHolePadding = 12f;
    const float CoachFontSize = 42f;
    const float CoachGap = 12f;
    const float CoachMinSide = 80f;
    const float CoachMaxLineWidth = 420f;
    const float ClickIconSize = 128f;
    const float ClickIconBobPx = 9f;
    const float ClickIconBobSpeed = 6.4f;
    const float ClickIconLift = 48f;
    const float ClickIconBelowTextGap = 28f;
    const string ClickIconSpritePath = "Assets/Resources/ClickIcon.png";

    static StreamsTutorialSpotlight _instance;

    Canvas _canvas;
    RectTransform _root;
    RectTransform _dimRoot;
    RectTransform _catcher;
    RectTransform _coachRoot;
    TextMeshProUGUI _coach;
    RectTransform _clickIcon;
    Image _clickIconImage;
    Vector2 _clickIconBase;
    Camera _worldCamera;
    bool _tapped;
    bool _dimRaycast;
    bool _showClickIcon;
    bool _clickOnHighlight;
    Coroutine _rebuild;

    readonly List<RectTransform> _holes = new List<RectTransform>();
    RectTransform _coachAnchor;
    string _coachSource;
    bool _mergeHoles;
    bool _followTargets;
    float _barRestPlayer;
    float _barRestAi;
    RectTransform _centerOn;
    Vector2 _laidOutSize;
    int _screenW = -1;
    int _screenH = -1;
    float _uiFit = 1f;
    bool _rebuilding;

    public static StreamsTutorialSpotlight Ensure()
    {
        if (_instance != null)
            return _instance;

        var go = new GameObject(nameof(StreamsTutorialSpotlight));
        return go.AddComponent<StreamsTutorialSpotlight>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        if (_canvas == null)
            Build();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void Build()
    {
        _canvas = gameObject.GetComponent<Canvas>();
        if (_canvas == null)
            _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;
        ApplyGameScaler();

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        _root = transform as RectTransform;
        if (_root == null)
            _root = gameObject.AddComponent<RectTransform>();

        _catcher = CreateChild("Catcher", _root);
        var catcherImage = _catcher.gameObject.AddComponent<Image>();
        catcherImage.color = Color.clear;
        catcherImage.raycastTarget = true;
        Stretch(_catcher);
        var catcherButton = _catcher.gameObject.AddComponent<Button>();
        catcherButton.transition = Selectable.Transition.None;
        catcherButton.onClick.AddListener(NotifyTapped);

        _dimRoot = CreateChild("DimRoot", _root);
        Stretch(_dimRoot);

        var coachRootGo = new GameObject("CoachText", typeof(RectTransform));
        _coachRoot = coachRootGo.GetComponent<RectTransform>();
        _coachRoot.SetParent(_dimRoot, false);
        _coachRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _coachRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _coachRoot.pivot = new Vector2(0f, 1f);
        _coachRoot.sizeDelta = new Vector2(640f, 120f);

        var bgGo = new GameObject("CoachBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGo.transform.SetParent(_coachRoot, false);
        var bgRt = bgGo.GetComponent<RectTransform>();
        Stretch(bgRt);
        var bg = bgGo.GetComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.06f, 0.88f);
        bg.raycastTarget = false;

        var coachGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        coachGo.transform.SetParent(_coachRoot, false);
        var coachRt = coachGo.GetComponent<RectTransform>();
        Stretch(coachRt);
        coachRt.offsetMin = new Vector2(14f, 10f);
        coachRt.offsetMax = new Vector2(-14f, -10f);
        _coach = coachGo.GetComponent<TextMeshProUGUI>();
        _coach.alignment = TextAlignmentOptions.TopLeft;
        _coach.fontSize = CoachFontSize;
        _coach.color = Color.white;
        _coach.enableWordWrapping = true;
        _coach.raycastTarget = false;
        var font = ResolveUiFont();
        if (font != null)
        {
            _coach.font = font;
            _coach.fontSharedMaterial = font.material;
        }

        _coachRoot.gameObject.SetActive(false);
        _catcher.gameObject.SetActive(false);
        EnsureClickIcon();
        _canvas.enabled = false;
    }

    void Update()
    {
        if (_clickIcon == null || !_clickIcon.gameObject.activeSelf)
            return;

        float bob = Mathf.Sin(Time.unscaledTime * ClickIconBobSpeed) * ClickIconBobPx * _uiFit;
        _clickIcon.anchoredPosition = _clickIconBase + new Vector2(0f, bob);
    }

    void LateUpdate()
    {
        if (_canvas == null || !_canvas.enabled || _root == null)
            return;

        bool screenChanged = Screen.width != _screenW || Screen.height != _screenH;
        bool fitChanged = Mathf.Abs(UiFit(_root != null ? _root.rect : new Rect()) - _uiFit) > 0.01f;
        if (screenChanged)
        {
            ApplyGameScaler();
            RefreshScaler(gameObject);
            Canvas.ForceUpdateCanvases();
        }

        Vector2 size = _root.rect.size;
        bool rectChanged = (_laidOutSize - size).sqrMagnitude > 0.25f;
        if (!screenChanged && !rectChanged && !fitChanged)
            return;
        if (size.x < 2f || size.y < 2f)
            return;

        RebuildDim();
    }

    void ApplyGameScaler()
    {
        var scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();

        CanvasScaler source = FindGameScaler();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = source != null ? source.referenceResolution : new Vector2(1920f, 1080f);
        scaler.screenMatchMode = source != null
            ? source.screenMatchMode
            : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = source != null ? source.matchWidthOrHeight : 0f;
        scaler.referencePixelsPerUnit = source != null ? source.referencePixelsPerUnit : 100f;
    }

    static CanvasScaler FindGameScaler()
    {
        CanvasScaler fallback = null;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas == null)
                continue;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                continue;

            if (canvas.name == "BoardCanvas")
                return scaler;
            if (fallback == null)
                fallback = scaler;
        }

        return fallback;
    }

    static void RefreshScaler(GameObject go)
    {
        if (go == null)
            return;

        var scaler = go.GetComponent<CanvasScaler>();
        if (scaler == null || !scaler.enabled)
            return;

        scaler.enabled = false;
        scaler.enabled = true;
    }

    public void RaiseAbove(Canvas other)
    {
        if (_canvas == null)
            Build();
        int otherOrder = other != null ? other.sortingOrder : SortingOrder;
        _canvas.sortingOrder = otherOrder + 10;
    }

    public void SetGraphBarRestHeights(float playerRest, float aiRest)
    {
        _barRestPlayer = Mathf.Max(0f, playerRest);
        _barRestAi = Mathf.Max(0f, aiRest);
    }

    public void SetHoleCenter(RectTransform target)
    {
        _centerOn = target;
    }

    public void Show(
        IList<RectTransform> holes,
        string message,
        bool blockClicks,
        Camera worldCamera,
        RectTransform coachAnchor = null,
        bool mergeHoles = false,
        bool followTargets = false)
    {
        if (_canvas == null)
            Build();

        ApplyGameScaler();
        _worldCamera = worldCamera;
        _coachAnchor = coachAnchor;
        _mergeHoles = mergeHoles;
        _followTargets = followTargets;
        _holes.Clear();
        if (holes != null)
        {
            for (int i = 0; i < holes.Count; i++)
            {
                if (holes[i] != null)
                    _holes.Add(holes[i]);
            }
        }

        gameObject.SetActive(true);
        _canvas.enabled = true;
        _tapped = false;
        _dimRaycast = !blockClicks;
        _showClickIcon = true;
        _clickOnHighlight = !blockClicks;
        _coachSource = message ?? "";
        _coach.text = _coachSource;
        _coach.overflowMode = TextOverflowModes.Overflow;
        _coach.enableWordWrapping = true;
        _coach.gameObject.SetActive(!string.IsNullOrEmpty(_coachSource));
        if (_coachRoot != null)
            _coachRoot.gameObject.SetActive(!string.IsNullOrEmpty(_coachSource));
        _catcher.gameObject.SetActive(blockClicks);
        RebuildDim();

        if (_rebuild != null)
            StopCoroutine(_rebuild);
        _rebuild = StartCoroutine(RebuildForLayout());
    }

    public void Hide()
    {
        if (_rebuild != null)
        {
            StopCoroutine(_rebuild);
            _rebuild = null;
        }

        _holes.Clear();
        _coachAnchor = null;
        _mergeHoles = false;
        _followTargets = false;
        _barRestPlayer = 0f;
        _barRestAi = 0f;
        _centerOn = null;
        _tapped = false;
        _showClickIcon = false;
        _clickOnHighlight = false;
        if (_dimRoot != null)
            ClearChildren(_dimRoot);
        if (_coach != null)
        {
            _coachSource = "";
            _coach.text = "";
            _coach.gameObject.SetActive(false);
        }

        if (_coachRoot != null)
            _coachRoot.gameObject.SetActive(false);

        if (_clickIcon != null)
            _clickIcon.gameObject.SetActive(false);

        if (_catcher != null)
            _catcher.gameObject.SetActive(false);

        if (_canvas != null)
        {
            _canvas.sortingOrder = SortingOrder;
            _canvas.enabled = false;
        }
    }

    public IEnumerator WaitForTap()
    {
        yield return WaitForPointerUp();
        _tapped = false;
        while (!_tapped)
        {
            if (_followTargets)
                RebuildDim();
            yield return null;
        }
        yield return WaitForPointerUp();
    }

    public void OnPointerClick(PointerEventData eventData) => NotifyTapped();

    void NotifyTapped() => _tapped = true;

    IEnumerator RebuildForLayout()
    {
        for (int i = 0; i < 8; i++)
        {
            RebuildDim();
            yield return null;
        }

        _rebuild = null;
    }

    void RebuildDim()
    {
        if (_rebuilding || _dimRoot == null)
            return;

        _rebuilding = true;
        Canvas.ForceUpdateCanvases();

        Rect full = _dimRoot.rect;
        if (full.width < 2f || full.height < 2f)
        {
            _rebuilding = false;
            return;
        }

        _uiFit = UiFit(full);
        Rect canvasLocal = StreamsAspectFit.Fit(full);

        ClearChildren(_dimRoot);
        var dark = new List<Rect> { canvasLocal };
        var highlightHoles = new List<Rect>();
        SubtractTargets(_holes, canvasLocal, dark, highlightHoles);

        for (int i = 0; i < dark.Count; i++)
            CreateDimPanel(dark[i]);

        LayoutCoach(canvasLocal, highlightHoles);
        LayoutClickIcon(canvasLocal, highlightHoles);
        _laidOutSize = canvasLocal.size;
        _screenW = Screen.width;
        _screenH = Screen.height;
        _rebuilding = false;
    }

    static float UiFit(Rect area)
    {
        if (area.width < 2f || area.height < 2f)
            return 1f;
        return Mathf.Clamp(Mathf.Min(area.width / StreamsAspectFit.DesignWidth, area.height / StreamsAspectFit.DesignHeight), 0.35f, 1f);
    }

    void SubtractTargets(List<RectTransform> targets, Rect canvasLocal, List<Rect> dark, List<Rect> collected)
    {
        if (targets == null)
            return;

        var slotHoles = new List<(int index, Rect rect)>();
        var barHoles = new List<Rect>();
        var toCut = new List<Rect>();
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null)
                continue;

            Rect hole = GetLocalRect(targets[i], out float padX, out float padTop, out float padBottom);
            hole = Pad(hole, padX, padTop, padBottom);
            hole = Intersect(canvasLocal, hole);
            if (hole.width < 1f || hole.height < 1f)
                continue;

            toCut.Add(hole);
            collected?.Add(hole);
            if (IsGraphBar(targets[i]))
                barHoles.Add(hole);

            int slotIndex = SlotIndexOf(targets[i]);
            if (slotIndex >= 0)
                slotHoles.Add((slotIndex, hole));
        }

        AddTrainJoins(slotHoles, canvasLocal, toCut, collected);

        if (_mergeHoles && toCut.Count > 0)
        {
            Rect merged = Union(toCut, toCut[0]);
            if (_centerOn != null)
            {
                Rect screen = GetLocalRect(_centerOn, out float padX, out float padTop, out float padBottom);
                screen = Pad(screen, padX, padTop, padBottom);
                merged = CenterOn(merged, screen.center);
            }
            else if (barHoles.Count > 0)
                merged = CenterLengthOn(merged, Union(barHoles, barHoles[0]));
            toCut.Clear();
            toCut.Add(merged);
            if (collected != null)
            {
                collected.Clear();
                collected.Add(merged);
            }
        }

        for (int i = 0; i < toCut.Count; i++)
        {
            var next = new List<Rect>();
            for (int d = 0; d < dark.Count; d++)
                Subtract(dark[d], toCut[i], next);
            dark.Clear();
            dark.AddRange(next);
        }
    }

    static int SlotIndexOf(RectTransform target)
    {
        if (target == null)
            return -1;
        var slot = target.GetComponent<StreamsUiSlot>();
        if (slot == null)
            slot = target.GetComponentInParent<StreamsUiSlot>();
        return slot != null ? slot.SlotIndex : -1;
    }

    static bool IsVerticalSlot(int idx) => (idx >= 0 && idx <= 4) || (idx >= 12 && idx <= 16);

    static void AddTrainJoins(
        List<(int index, Rect rect)> slotHoles,
        Rect canvasLocal,
        List<Rect> toCut,
        List<Rect> collected)
    {
        if (slotHoles == null || slotHoles.Count < 2)
            return;

        slotHoles.Sort((a, b) => a.index.CompareTo(b.index));
        for (int i = 0; i < slotHoles.Count - 1; i++)
        {
            var a = slotHoles[i];
            var b = slotHoles[i + 1];
            if (b.index != a.index + 1)
                continue;

            AddJoinRects(a.rect, b.rect, a.index, canvasLocal, toCut, collected);
        }
    }

    static void AddJoinRects(
        Rect a,
        Rect b,
        int fromIndex,
        Rect canvasLocal,
        List<Rect> toCut,
        List<Rect> collected)
    {
        float dx = b.center.x - a.center.x;
        float dy = b.center.y - a.center.y;
        float ax = Mathf.Abs(dx);
        float ay = Mathf.Abs(dy);

        void Add(Rect join)
        {
            join = Intersect(canvasLocal, join);
            if (join.width < 1f || join.height < 1f)
                return;
            toCut.Add(join);
            collected?.Add(join);
        }

        if (ay < ax * 0.35f || ax < ay * 0.35f)
        {
            Add(Rect.MinMaxRect(
                Mathf.Min(a.xMin, b.xMin),
                Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax),
                Mathf.Max(a.yMax, b.yMax)));
            return;
        }

        if (IsVerticalSlot(fromIndex))
        {
            Add(Rect.MinMaxRect(a.xMin, Mathf.Min(a.yMin, b.yMin), a.xMax, Mathf.Max(a.yMax, b.yMax)));
            Add(Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), b.yMin, Mathf.Max(a.xMax, b.xMax), b.yMax));
        }
        else
        {
            Add(Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), a.yMin, Mathf.Max(a.xMax, b.xMax), a.yMax));
            Add(Rect.MinMaxRect(b.xMin, Mathf.Min(a.yMin, b.yMin), b.xMax, Mathf.Max(a.yMax, b.yMax)));
        }
    }

    void LayoutCoach(Rect canvasLocal, List<Rect> holes)
    {
        if (_coach == null)
            return;

        if (string.IsNullOrEmpty(_coachSource))
        {
            _coach.gameObject.SetActive(false);
            if (_coachRoot != null)
                _coachRoot.gameObject.SetActive(false);
            return;
        }

        Rect highlight = Union(holes, canvasLocal);
        Rect placement = PlacementRect(canvasLocal, highlight);

        float fit = Mathf.Max(0.35f, _uiFit);
        float font = CoachFontSize * fit;
        float gap = CoachGap * fit;
        float maxLine = CoachMaxLineWidth * fit;
        float minSide = CoachMinSide * fit;
        _coach.fontSize = font;

        float wrapWidth = Mathf.Min(maxLine, Mathf.Max(minSide, canvasLocal.width * 0.42f));
        string wrapped = WrapToWidth(_coach, _coachSource, wrapWidth);
        _coach.text = wrapped;
        _coach.enableWordWrapping = true;

        Vector2 pref = _coach.GetPreferredValues(wrapped, wrapWidth, 0f);
        float boxW = Mathf.Clamp(pref.x + 28f * fit, 56f * fit, wrapWidth + 28f * fit);
        float boxH = Mathf.Max(pref.y + 20f * fit, font * 1.2f);
        boxW = Mathf.Min(boxW, Mathf.Max(56f * fit, canvasLocal.width - gap * 2f));
        boxH = Mathf.Min(boxH, Mathf.Max(font, canvasLocal.height - gap * 2f));

        bool onRight;
        Vector2 pivotPos = ChooseCoachPivot(
            canvasLocal,
            holes,
            placement,
            placement.center,
            boxW,
            boxH,
            gap,
            _coachAnchor == null,
            out onRight);
        _coach.alignment = onRight ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;

        var rt = _coachRoot != null ? _coachRoot : _coach.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = onRight ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(boxW, boxH);
        rt.anchoredPosition = pivotPos;

        _coach.gameObject.SetActive(true);
        if (_coachRoot != null)
        {
            _coachRoot.gameObject.SetActive(true);
            _coachRoot.SetAsLastSibling();
        }
        else
            _coach.transform.SetAsLastSibling();
    }

    void EnsureClickIcon()
    {
        if (_clickIcon != null)
            return;

        Sprite sprite = ResolveClickIconSprite();
        var go = new GameObject("ClickIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _clickIcon = go.GetComponent<RectTransform>();
        _clickIcon.SetParent(_root != null ? _root : transform, false);
        _clickIcon.anchorMin = new Vector2(0.5f, 0.5f);
        _clickIcon.anchorMax = new Vector2(0.5f, 0.5f);
        _clickIcon.pivot = new Vector2(0.5f, 1f);
        _clickIcon.sizeDelta = new Vector2(ClickIconSize, ClickIconSize);

        _clickIconImage = go.GetComponent<Image>();
        _clickIconImage.raycastTarget = false;
        _clickIconImage.preserveAspect = true;
        _clickIconImage.sprite = sprite;
        _clickIconImage.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        _clickIcon.gameObject.SetActive(false);
    }

    void LayoutClickIcon(Rect canvasLocal, List<Rect> holes)
    {
        EnsureClickIcon();
        if (_clickIcon == null)
            return;

        if (_clickIconImage != null && _clickIconImage.sprite == null)
        {
            _clickIconImage.sprite = ResolveClickIconSprite();
            if (_clickIconImage.sprite != null)
                _clickIconImage.color = Color.white;
        }

        if (!_showClickIcon || (_clickIconImage != null && _clickIconImage.sprite == null))
        {
            _clickIcon.gameObject.SetActive(false);
            return;
        }

        float fit = Mathf.Max(0.35f, _uiFit);
        float size = ClickIconSize * fit;
        if (_clickIconImage != null && _clickIconImage.sprite != null)
        {
            Rect sr = _clickIconImage.sprite.rect;
            if (sr.height > 1f)
            {
                float aspect = sr.width / sr.height;
                size = ClickIconSize * fit;
                _clickIcon.sizeDelta = new Vector2(size * aspect, size);
            }
        }

        Rect target = default;
        bool hasTarget = false;
        if (!_clickOnHighlight && TryGetAnchorHighlightRect(canvasLocal, out target))
            hasTarget = true;
        else if (holes != null && holes.Count > 0)
        {
            target = Union(holes, canvasLocal);
            hasTarget = true;
        }

        if (!hasTarget)
        {
            _clickIcon.gameObject.SetActive(false);
            return;
        }

        float lift = (_clickOnHighlight ? ClickIconLift : ClickIconLift - ClickIconBelowTextGap) * fit;
        float x = target.center.x;
        float y = target.yMin + lift;

        float minY = canvasLocal.yMin + CoachGap * fit + size + ClickIconBobPx * fit;
        if (y < minY)
            y = minY;

        _clickIconBase = new Vector2(x, y);
        float bob = Mathf.Sin(Time.unscaledTime * ClickIconBobSpeed) * ClickIconBobPx * fit;
        _clickIcon.anchoredPosition = _clickIconBase + new Vector2(0f, bob);
        _clickIcon.gameObject.SetActive(true);
        _clickIcon.SetAsLastSibling();
    }

    bool TryGetAnchorHighlightRect(Rect canvasLocal, out Rect rect)
    {
        rect = default;
        if (_coachAnchor == null)
            return false;

        Rect hole = GetLocalRect(_coachAnchor, out float padX, out float padTop, out float padBottom);
        hole = Pad(hole, padX, padTop, padBottom);
        hole = Intersect(canvasLocal, hole);
        if (hole.width < 1f || hole.height < 1f)
            return false;

        rect = hole;
        return true;
    }

    static Vector2 ChooseCoachPivot(
        Rect canvas,
        List<Rect> holes,
        Rect highlight,
        Vector2 focus,
        float boxW,
        float boxH,
        float gap,
        bool considerEachHole,
        out bool onRight)
    {
        bool chosenRight = true;
        Vector2 bestPos = new Vector2(highlight.xMax + gap, highlight.yMax);
        float bestScore = float.NegativeInfinity;

        void Consider(float pivotX, float pivotY, bool right)
        {
            Rect box = CoachBox(new Vector2(pivotX, pivotY), boxW, boxH, right);
            box = ClampRectToCanvas(box, canvas, gap);
            float score = RateCoachBox(box, canvas, holes, focus, highlight, gap);
            if (score <= bestScore)
                return;

            bestScore = score;
            bestPos = PivotFromBox(box, right);
            chosenRight = right;
        }

        float midY = highlight.center.y + boxH * 0.5f;
        Consider(highlight.xMax + gap, midY, true);
        Consider(highlight.xMin - gap, midY, false);
        Consider(highlight.xMax + gap, highlight.yMax, true);
        Consider(highlight.xMin - gap, highlight.yMax, false);
        Consider(highlight.xMax + gap, highlight.yMin, true);
        Consider(highlight.xMin - gap, highlight.yMin, false);

        if (considerEachHole && holes != null)
        {
            for (int i = 0; i < holes.Count; i++)
            {
                Rect hole = holes[i];
                float holeMidY = hole.center.y + boxH * 0.5f;
                Consider(hole.xMax + gap, holeMidY, true);
                Consider(hole.xMin - gap, holeMidY, false);
                Consider(hole.xMax + gap, hole.yMax, true);
                Consider(hole.xMin - gap, hole.yMax, false);
            }
        }

        onRight = chosenRight;
        return bestPos;
    }

    static Rect CoachBox(Vector2 pivot, float width, float height, bool onRight)
    {
        float xMin = onRight ? pivot.x : pivot.x - width;
        return Rect.MinMaxRect(xMin, pivot.y - height, xMin + width, pivot.y);
    }

    static Vector2 PivotFromBox(Rect box, bool onRight)
    {
        return onRight ? new Vector2(box.xMin, box.yMax) : new Vector2(box.xMax, box.yMax);
    }

    static Rect ClampRectToCanvas(Rect box, Rect canvas, float gap)
    {
        Rect limit = InsetRect(canvas, gap);
        if (limit.width < 8f || limit.height < 8f)
            limit = canvas;

        float w = box.width;
        float h = box.height;
        float x = box.xMin;
        float y = box.yMin;
        if (x + w > limit.xMax)
            x = limit.xMax - w;
        if (y + h > limit.yMax)
            y = limit.yMax - h;
        if (x < limit.xMin)
            x = limit.xMin;
        if (y < limit.yMin)
            y = limit.yMin;
        return new Rect(x, y, w, h);
    }

    static Rect InsetRect(Rect rect, float pad)
    {
        return Rect.MinMaxRect(rect.xMin + pad, rect.yMin + pad, rect.xMax - pad, rect.yMax - pad);
    }

    Rect PlacementRect(Rect canvasLocal, Rect fallback)
    {
        if (_coachAnchor == null)
            return fallback;

        Rect hole = GetLocalRect(_coachAnchor, out float padX, out float padTop, out float padBottom);
        hole = Pad(hole, padX, padTop, padBottom);
        hole = Intersect(canvasLocal, hole);
        if (hole.width < 1f || hole.height < 1f)
            return fallback;
        return hole;
    }

    static float RateCoachBox(Rect box, Rect canvas, List<Rect> holes, Vector2 focus, Rect highlight, float gap)
    {
        float fit = CoachGap > 0.01f ? gap / CoachGap : 1f;
        Rect vis = Intersect(box, InsetRect(canvas, gap));
        float area = Mathf.Max(1f, box.width * box.height);
        float visible = Mathf.Max(0f, vis.width * vis.height);
        if (visible < area * 0.45f)
            return float.NegativeInfinity;

        float overlap = 0f;
        if (holes != null)
        {
            for (int i = 0; i < holes.Count; i++)
            {
                Rect hit = Intersect(box, holes[i]);
                overlap += Mathf.Max(0f, hit.width * hit.height);
            }
        }

        Rect placeHit = Intersect(box, highlight);
        overlap += Mathf.Max(0f, placeHit.width * placeHit.height);

        bool rightOf = box.xMin >= highlight.xMax - 8f * fit;
        bool leftOf = box.xMax <= highlight.xMin + 8f * fit;
        bool midY = Mathf.Abs(box.center.y - highlight.center.y) <= Mathf.Max(24f * fit, highlight.height * 0.25f);
        bool below = box.yMax <= highlight.yMin + 24f * fit;
        float sideBonus = 0f;
        if ((rightOf || leftOf) && midY)
            sideBonus = 240f;
        else if (rightOf || leftOf)
            sideBonus = 80f;
        else if (below)
            sideBonus = -80f;

        float dist = Vector2.Distance(box.center, focus);
        float off = (area - visible) / area;
        return -overlap * 12f - dist * 0.12f - off * area * 4f + sideBonus;
    }

    static string WrapToWidth(TMP_Text tmp, string source, float maxWidth)
    {
        if (tmp == null || string.IsNullOrEmpty(source))
            return source ?? "";

        source = source.Replace("\r\n", "\n").Trim();
        if (tmp.GetPreferredValues(source).x <= maxWidth)
            return source;

        var result = new StringBuilder();
        var line = new StringBuilder();
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\n')
            {
                result.Append(line);
                result.Append('\n');
                line.Length = 0;
                continue;
            }

            line.Append(c);
            if (tmp.GetPreferredValues(line.ToString()).x <= maxWidth)
                continue;

            line.Length--;
            int breakAt = LastWrapIndex(line);
            if (breakAt > 0)
            {
                result.Append(line.ToString(0, breakAt).TrimEnd());
                result.Append('\n');
                string rest = line.ToString(breakAt, line.Length - breakAt).TrimStart();
                line.Length = 0;
                line.Append(rest);
            }
            else if (line.Length > 0)
            {
                result.Append(line);
                result.Append('\n');
                line.Length = 0;
            }

            line.Append(c);
        }

        result.Append(line);
        return result.ToString();
    }

    static int LastWrapIndex(StringBuilder line)
    {
        for (int i = line.Length - 1; i > 0; i--)
        {
            char c = line[i];
            if (c == ' ' || c == '!' || c == '.' || c == '?' || c == ',')
                return c == ' ' ? i : i + 1;
        }

        return line.Length;
    }

    static Rect Union(List<Rect> rects, Rect fallback)
    {
        if (rects == null || rects.Count == 0)
            return fallback;

        float xMin = rects[0].xMin;
        float yMin = rects[0].yMin;
        float xMax = rects[0].xMax;
        float yMax = rects[0].yMax;
        for (int i = 1; i < rects.Count; i++)
        {
            xMin = Mathf.Min(xMin, rects[i].xMin);
            yMin = Mathf.Min(yMin, rects[i].yMin);
            xMax = Mathf.Max(xMax, rects[i].xMax);
            yMax = Mathf.Max(yMax, rects[i].yMax);
        }

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    void CreateDimPanel(Rect localRect)
    {
        if (localRect.width < 0.5f || localRect.height < 0.5f)
            return;

        var rt = CreateChild("Dim", _dimRoot);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, DimAlpha);
        image.raycastTarget = _dimRaycast;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = localRect.center;
        rt.sizeDelta = localRect.size;
    }

    Rect GetLocalRect(RectTransform target, out float padX, out float padTop, out float padBottom)
    {
        Vector3[] corners = new Vector3[4];
        padX = HolePadding;
        padTop = HolePadding;
        padBottom = HolePadding;

        bool visual = TryGetBarRestWorldCorners(target, corners);

        var slot = target.GetComponent<StreamsUiSlot>();
        if (slot == null)
            slot = target.GetComponentInParent<StreamsUiSlot>();

        if (!visual && slot != null && slot.TryGetVisualWorldCorners(corners, SlotSpritePadBottomExtra))
            visual = true;

        if (!visual && TryGetButtonWorldCorners(target, corners))
        {
            visual = true;
            padX = ButtonHolePadding;
            padTop = ButtonHolePadding;
            padBottom = ButtonHolePadding;
        }

        if (!visual && TryGetTmpWorldCorners(target, corners))
        {
            visual = true;
            padX = TmpHolePadding;
            padTop = TmpHolePadding;
            padBottom = TmpHolePadding;
        }

        if (!visual && TryGetImageWorldCorners(target, corners))
        {
            visual = true;
            padX = ImageHolePadding;
            padTop = ImageHolePadding;
            padBottom = ImageHolePadding;
        }

        if (!visual)
            target.GetWorldCorners(corners);

        Camera eventCam = CameraFor(target, _worldCamera);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            Vector2 screen = WorldToGameScreen(eventCam, corners[i]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_dimRoot, screen, null, out Vector2 local);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        float fit = Mathf.Max(0.35f, _uiFit);
        padX *= fit;
        padTop *= fit;
        padBottom *= fit;
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static bool IsGraphBar(RectTransform target)
    {
        if (target == null)
            return false;
        return target.name == "Bar_Player" || target.name == "Bar_AI";
    }

    float RestHeightForBar(RectTransform target)
    {
        if (target == null)
            return 0f;
        if (target.name == "Bar_Player")
            return _barRestPlayer;
        if (target.name == "Bar_AI")
            return _barRestAi;
        return 0f;
    }

    bool TryGetBarRestWorldCorners(RectTransform target, Vector3[] corners)
    {
        float height = RestHeightForBar(target);
        if (height < 1f || corners == null || corners.Length < 4)
            return false;

        Rect sr = target.rect;
        float xMin = sr.xMin;
        float xMax = sr.xMax;
        corners[0] = target.TransformPoint(new Vector3(xMin, 0f, 0f));
        corners[1] = target.TransformPoint(new Vector3(xMin, height, 0f));
        corners[2] = target.TransformPoint(new Vector3(xMax, height, 0f));
        corners[3] = target.TransformPoint(new Vector3(xMax, 0f, 0f));
        return true;
    }

    static Rect CenterOn(Rect merged, Vector2 center)
    {
        float halfW = Mathf.Max(
            merged.width * 0.5f,
            Mathf.Abs(merged.xMax - center.x),
            Mathf.Abs(center.x - merged.xMin));
        float halfH = Mathf.Max(
            merged.height * 0.5f,
            Mathf.Abs(merged.yMax - center.y),
            Mathf.Abs(center.y - merged.yMin));
        return Rect.MinMaxRect(center.x - halfW, center.y - halfH, center.x + halfW, center.y + halfH);
    }

    static Rect CenterLengthOn(Rect merged, Rect focus)
    {
        float cy = focus.center.y;
        float half = Mathf.Max(
            focus.height * 0.5f,
            Mathf.Abs(merged.yMax - cy),
            Mathf.Abs(cy - merged.yMin));
        return Rect.MinMaxRect(merged.xMin, cy - half, merged.xMax, cy + half);
    }

    static bool TryGetButtonWorldCorners(RectTransform target, Vector3[] corners)
    {
        var button = target.GetComponent<Button>();
        if (button == null)
            button = target.GetComponentInParent<Button>();
        if (button == null)
            return false;

        Image image = button.targetGraphic as Image;
        if (image == null)
            image = button.GetComponent<Image>();
        if (image == null)
            image = button.GetComponentInChildren<Image>();
        return TryGetSpriteDrawnWorldCorners(image, corners);
    }

    static bool TryGetTmpWorldCorners(RectTransform target, Vector3[] corners)
    {
        var tmp = target.GetComponent<TextMeshProUGUI>();
        if (tmp == null)
            return false;

        tmp.ForceMeshUpdate();
        Bounds b = tmp.textBounds;
        if (b.size.x < 1f || b.size.y < 1f)
            return false;

        var rt = tmp.rectTransform;
        corners[0] = rt.TransformPoint(new Vector3(b.min.x, b.min.y, 0f));
        corners[1] = rt.TransformPoint(new Vector3(b.min.x, b.max.y, 0f));
        corners[2] = rt.TransformPoint(new Vector3(b.max.x, b.max.y, 0f));
        corners[3] = rt.TransformPoint(new Vector3(b.max.x, b.min.y, 0f));
        return true;
    }

    static bool TryGetImageWorldCorners(RectTransform target, Vector3[] corners)
    {
        var image = target.GetComponent<Image>();
        if (image == null)
            image = target.GetComponentInChildren<Image>();
        return TryGetSpriteDrawnWorldCorners(image, corners);
    }

    static bool TryGetSpriteDrawnWorldCorners(Image image, Vector3[] corners)
    {
        if (image == null || image.sprite == null || corners == null || corners.Length < 4)
            return false;

        var rt = image.rectTransform;
        Sprite sprite = image.sprite;
        Rect sr = rt.rect;
        Rect tex = sprite.rect;
        if (tex.width < 1f || tex.height < 1f)
            return false;

        float xMin = sr.xMin;
        float xMax = sr.xMax;
        float yMin = sr.yMin;
        float yMax = sr.yMax;

        if (image.type == Image.Type.Simple && image.preserveAspect)
            FitPreserveAspect(ref xMin, ref xMax, ref yMin, ref yMax, tex.width / tex.height);

        Vector4 padding = DataUtility.GetPadding(sprite);
        bool sliced = image.type == Image.Type.Sliced || image.type == Image.Type.Tiled;
        Vector4 border = sliced ? sprite.border : Vector4.zero;
        float toLocal = sliced ? SpritePixelsToLocal(image, sprite) : 0f;

        float leftPad;
        float rightPad;
        float bottomPad;
        float topPad;
        if (sliced && border.x + border.z >= 1f)
        {
            leftPad = padding.x * toLocal;
            rightPad = padding.z * toLocal;
        }
        else
        {
            float sx = (xMax - xMin) / tex.width;
            leftPad = padding.x * sx;
            rightPad = padding.z * sx;
        }

        if (sliced && border.y + border.w >= 1f)
        {
            bottomPad = padding.y * toLocal;
            topPad = padding.w * toLocal;
        }
        else
        {
            float sy = (yMax - yMin) / tex.height;
            bottomPad = padding.y * sy;
            topPad = padding.w * sy;
        }

        xMin += leftPad;
        xMax -= rightPad;
        yMin += bottomPad;
        yMax -= topPad;
        if (xMax <= xMin + 1f || yMax <= yMin + 1f)
            return false;

        corners[0] = rt.TransformPoint(new Vector3(xMin, yMin, 0f));
        corners[1] = rt.TransformPoint(new Vector3(xMin, yMax, 0f));
        corners[2] = rt.TransformPoint(new Vector3(xMax, yMax, 0f));
        corners[3] = rt.TransformPoint(new Vector3(xMax, yMin, 0f));
        return true;
    }

    static void FitPreserveAspect(ref float xMin, ref float xMax, ref float yMin, ref float yMax, float aspect)
    {
        if (aspect <= 0.0001f)
            return;

        float width = xMax - xMin;
        float height = yMax - yMin;
        if (width < 1f || height < 1f)
            return;

        if (width / height > aspect)
        {
            float fitted = height * aspect;
            float cx = (xMin + xMax) * 0.5f;
            xMin = cx - fitted * 0.5f;
            xMax = cx + fitted * 0.5f;
        }
        else
        {
            float fitted = width / aspect;
            float cy = (yMin + yMax) * 0.5f;
            yMin = cy - fitted * 0.5f;
            yMax = cy + fitted * 0.5f;
        }
    }

    static float SpritePixelsToLocal(Image image, Sprite sprite)
    {
        float spritePpu = Mathf.Max(0.01f, sprite.pixelsPerUnit);
        float refPpu = 100f;
        Canvas canvas = image.canvas;
        if (canvas != null)
            refPpu = Mathf.Max(0.01f, canvas.referencePixelsPerUnit);
        float multiplied = (spritePpu / refPpu) * Mathf.Max(0.01f, image.pixelsPerUnitMultiplier);
        return 1f / multiplied;
    }

    static Vector2 WorldToGameScreen(Camera cam, Vector3 world)
    {
        if (cam == null)
            return RectTransformUtility.WorldToScreenPoint(null, world);

        Vector3 viewport = cam.WorldToViewportPoint(world);
        Rect pixel = cam.pixelRect;
        return new Vector2(
            pixel.x + viewport.x * pixel.width,
            pixel.y + viewport.y * pixel.height);
    }

    static Camera CameraFor(RectTransform rt, Camera worldCamera)
    {
        Canvas canvas = rt.GetComponentInParent<Canvas>();
        if (canvas == null)
            return worldCamera;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;
        return canvas.worldCamera != null ? canvas.worldCamera : worldCamera;
    }

    static Rect Pad(Rect rect, float padX, float padTop, float padBottom)
    {
        return Rect.MinMaxRect(rect.xMin - padX, rect.yMin - padBottom, rect.xMax + padX, rect.yMax + padTop);
    }

    static Rect Intersect(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        if (xMax <= xMin || yMax <= yMin)
            return new Rect(xMin, yMin, 0f, 0f);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    static void Subtract(Rect from, Rect hole, List<Rect> output)
    {
        if (!from.Overlaps(hole, true))
        {
            output.Add(from);
            return;
        }

        Rect clipped = Intersect(from, hole);
        if (clipped.width < 0.5f || clipped.height < 0.5f)
        {
            output.Add(from);
            return;
        }

        // top
        if (clipped.yMax < from.yMax)
            output.Add(Rect.MinMaxRect(from.xMin, clipped.yMax, from.xMax, from.yMax));
        // bottom
        if (clipped.yMin > from.yMin)
            output.Add(Rect.MinMaxRect(from.xMin, from.yMin, from.xMax, clipped.yMin));
        // left
        if (clipped.xMin > from.xMin)
            output.Add(Rect.MinMaxRect(from.xMin, clipped.yMin, clipped.xMin, clipped.yMax));
        // right
        if (clipped.xMax < from.xMax)
            output.Add(Rect.MinMaxRect(clipped.xMax, clipped.yMin, from.xMax, clipped.yMax));
    }

    static IEnumerator WaitForPointerUp()
    {
        while (IsPointerHeld())
            yield return null;
    }

    static bool IsPointerHeld()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            return true;
        return false;
    }

    static RectTransform CreateChild(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void ClearChildren(RectTransform parent)
    {
        if (parent == null)
            return;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child != null && (child.name == "CoachText" || child.name == "ClickIcon"))
                continue;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    static TMP_FontAsset ResolveUiFont()
    {
#if UNITY_EDITOR
        var fromAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/GmarketSansTTFBold SDF.asset");
        if (fromAsset != null)
            return fromAsset;
#endif
        TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
        foreach (var tmp in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tmp == null || tmp.font == null || tmp.font == fallback)
                continue;
            return tmp.font;
        }

        return fallback;
    }

    static Sprite ResolveClickIconSprite()
    {
#if UNITY_EDITOR
        var fromAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(ClickIconSpritePath);
        if (fromAsset != null)
            return fromAsset;
#endif
        return Resources.Load<Sprite>("ClickIcon");
    }
}
