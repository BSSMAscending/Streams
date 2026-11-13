using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// SampleScene 물음표 버튼. 복제한 플레이어 보드와 주머니 카드로 도움말 세 장을 넘깁니다.
/// 게임은 계속 진행되고, 어두운 막이 클릭만 막습니다.
/// </summary>
[DisallowMultipleComponent]
public class StreamsHelpPopup : MonoBehaviour
{
    const int SortingOrder = 120;
    const float DimAlpha = 0.97f;
    const float BoardDimAlpha = 0.42f;
    const float CardDimmed = 1f - BoardDimAlpha;
    const float CardLit = 1f;
    const float CardSpriteSize = 512f;
    const float CardVisibleWidth = 176f;
    const float CardVisibleHeight = 272f;
    const float BoardScaleFactor = 0.9f;
    const float BoardTopMargin = 36f;
    const float ButtonInset = 160f;
    const float ButtonHeight = 192f;
    const float SlotBrighten = 0.1f;
    const float SlotHold = 0.28f;
    const float SlotDarken = 0.22f;
    const float RunBrighten = 0.32f;
    const float RunHold = 0.22f;
    const float RunDarken = 0.55f;
    const float ArrowAlpha = 0.55f;
    const float ArrowBobPx = 9f;
    const float ArrowBobSpeed = 6.4f;
    const string Page1Text = "출발 칸에서 도착 칸으로 갈수록 숫자가 커지게 배치하세요.";
    const string Page3Text = "더 길게 배치할수록 더 높은 점수를 얻을 수 있습니다.";
    const string PouchText = "카드는 1~30 카드와 조커카드가 있어요.\n\n11~19 카드는 2개씩 들어있어요.";
    const string FontPath = "Assets/Fonts/GmarketSansTTFBold SDF.asset";

    // 1~20 연속 대신, 겹치는 칸 10-11·15-16에 11~19 중복 카드를 둔 오름차순.
    static readonly string[] Page1Cards =
    {
        "1", "2", "4", "6", "8", "9", "10", "12", "13", "14",
        "15", "15", "16", "17", "18", "19", "19", "23", "26", "30"
    };

    static readonly string[] DemoCards =
    {
        "1", "2", "4", "7", "10",
        "3", "6", "9", "12",
        "5", "8", "15", "18",
        "11", "14", "16", "20", "22", "25", "30"
    };

    static readonly string[] DeckLow =
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10"
    };

    static readonly string[] DeckMid =
    {
        "11", "12", "13", "14", "15", "16", "17", "18", "19"
    };

    static readonly string[] DeckHigh =
    {
        "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30"
    };

    GameObject _board;
    GameObject _cardGrid;
    Image _dim;
    RectTransform _copyRoot;
    TextMeshProUGUI _copy;
    Button _arrowLeft;
    Button _arrowRight;
    Button _confirm;
    RectTransform _arrowLeftRt;
    RectTransform _arrowRightRt;
    float _arrowLeftBaseX;
    float _arrowRightBaseX;
    List<StreamsUiSlot> _slots = new List<StreamsUiSlot>();
    (int start, int end)[] _runs = System.Array.Empty<(int, int)>();
    int _page;
    bool _built;
    Coroutine _runRoutine;
    Coroutine _fadeRoutine;
    readonly List<Coroutine> _slotCycles = new List<Coroutine>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "SampleScene")
            return;

        var existing = FindObjectsByType<StreamsHelpPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (existing != null && existing.Length > 0)
            return;

        var go = new GameObject(nameof(StreamsHelpPopup));
        go.AddComponent<StreamsHelpPopup>();
    }

    void Start()
    {
        Build();
        WireQuestionButton();
        gameObject.SetActive(false);
    }

    void OnDisable()
    {
        StopRuns();
    }

    public void Open()
    {
        if (StreamsTutorialSelection.IsActive)
            return;

        if (!_built)
            Build();

        gameObject.SetActive(true);
        ShowPage(0);
    }

    public void Close()
    {
        StopRuns();
        gameObject.SetActive(false);
    }

    void WireQuestionButton()
    {
        Button question = null;
        var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == "QuestionButton")
            {
                question = buttons[i];
                break;
            }
        }

        if (question == null)
        {
            Debug.LogWarning("StreamsHelpPopup: QuestionButton을 찾지 못했습니다.");
            return;
        }

        question.onClick.RemoveListener(Open);
        question.onClick.AddListener(Open);
    }

    void Build()
    {
        if (_built)
            return;
        _built = true;

        var canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;

        ApplyGameScaler();
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        _dim = CreateImage("Dim", transform, new Color(0f, 0f, 0f, DimAlpha));
        Stretch(_dim.rectTransform);
        _dim.raycastTarget = false;

        BuildBoard();
        BuildCardGrid();
        BuildCopy();
        BuildEdgeButtons();

        Sprite arrow = LoadSprite("Arrow1");
        _arrowLeft = CreateArrow("ArrowLeft", 0f, 160f, flip: true, arrow);
        _arrowRight = CreateArrow("ArrowRight", 1f, -160f, flip: false, arrow);
        _arrowLeftRt = _arrowLeft.transform as RectTransform;
        _arrowRightRt = _arrowRight.transform as RectTransform;
        _arrowLeftBaseX = _arrowLeftRt.anchoredPosition.x;
        _arrowRightBaseX = _arrowRightRt.anchoredPosition.x;
        _arrowLeft.onClick.AddListener(ShowPrevious);
        _arrowRight.onClick.AddListener(ShowNext);

        _confirm = CreateConfirm();
        _confirm.onClick.AddListener(Close);

        BuildRuns();
        ShowPage(0);
        AttachAspectFit();
    }

    void BuildBoard()
    {
        var source = GameObject.Find("PlayerBoard");
        if (source == null)
        {
            Debug.LogError("StreamsHelpPopup: PlayerBoard가 없습니다.");
            return;
        }

        _board = Instantiate(source, transform);
        _board.name = "HelpBoard";
        KeepArrowsSlotsAndPlate(_board);

        foreach (var path in _board.GetComponentsInChildren<num_path>(true))
        {
            path.isPlayerControlledBoard = false;
            path.enabled = false;
        }

        var rt = _board.transform as RectTransform;
        var sourceRt = source.transform as RectTransform;
        if (rt != null && sourceRt != null)
        {
            float scale = sourceRt.localScale.x * BoardScaleFactor;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = new Vector3(scale, scale, scale);
            rt.sizeDelta = sourceRt.sizeDelta;
            rt.anchoredPosition = new Vector2(0f, HelpBoardCenterY(sourceRt.sizeDelta.y * scale));
        }

        var slots = CollectSlots(_board.transform);
        _slots = slots;

        Transform slotsRoot = _board.transform.Find("Slots");
        if (slotsRoot != null)
            slotsRoot.SetAsLastSibling();

        SilenceBoard(_board);
        RaiseAboveDim(_board, SortingOrder + 1, false);

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] == null)
                continue;

            RaiseAboveDim(_slots[i].gameObject, SortingOrder + 2, false);
            SetSlotBrightness(_slots[i], CardDimmed);
        }
    }

    static void KeepArrowsSlotsAndPlate(GameObject board)
    {
        var remove = new List<GameObject>();
        for (int i = 0; i < board.transform.childCount; i++)
        {
            Transform child = board.transform.GetChild(i);
            if (child.name == "Slots" || child.name.StartsWith("Arrow"))
                continue;
            remove.Add(child.gameObject);
        }

        for (int i = 0; i < remove.Count; i++)
            Destroy(remove[i]);
    }

    static List<StreamsUiSlot> CollectSlots(Transform board)
    {
        var list = new List<StreamsUiSlot>();
        Transform root = board.Find("Slots");
        if (root == null)
            return list;

        for (int i = 0; i < root.childCount; i++)
        {
            var slot = root.GetChild(i).GetComponent<StreamsUiSlot>();
            if (slot == null || slot.IsHoldingPreview)
                continue;
            list.Add(slot);
        }

        return list;
    }

    static void SilenceBoard(GameObject board)
    {
        foreach (var slot in board.GetComponentsInChildren<StreamsUiSlot>(true))
            slot.enabled = false;

        // 버튼을 그냥 끄면 카드 이미지가 비활성 색으로 죽어 원본보다 어둡게 남습니다.
        foreach (var button in board.GetComponentsInChildren<Button>(true))
        {
            var graphic = button.targetGraphic;
            Color original = graphic != null ? graphic.color : Color.white;
            button.transition = Selectable.Transition.None;
            button.enabled = false;
            if (graphic != null)
                graphic.color = original;
        }

        foreach (var graphic in board.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    void BuildCardGrid()
    {
        _cardGrid = new GameObject("CardGrid", typeof(RectTransform));
        _cardGrid.transform.SetParent(transform, false);

        const float gap = 12f;
        const float groupGap = 40f;
        float slotSize = BoardSlotCanvasSize();
        float scale = slotSize / CardSpriteSize;
        float visW = CardVisibleWidth * scale;
        float visH = CardVisibleHeight * scale;

        float RowWidth(int count)
        {
            return count * visW + Mathf.Max(0, count - 1) * gap;
        }

        float leftWidth = Mathf.Max(RowWidth(DeckLow.Length), RowWidth(DeckMid.Length));
        float rightWidth = RowWidth(DeckHigh.Length);
        float leftHeight = visH * 3f + groupGap + gap;
        float rightHeight = visH * 2f + groupGap;
        float width = leftWidth + groupGap + rightWidth;
        float height = Mathf.Max(leftHeight, rightHeight);

        var grid = _cardGrid.GetComponent<RectTransform>();
        grid.anchorMin = grid.anchorMax = new Vector2(0.5f, 0.5f);
        grid.pivot = new Vector2(0.5f, 0.5f);
        grid.sizeDelta = new Vector2(width, height);
        PlaceCardGrid();

        GameObject prefab = ResolveCardPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("StreamsHelpPopup: Card 프리팹이 없어 주머니 장을 비웁니다.");
            return;
        }

        float top = height * 0.5f;
        float leftX = -width * 0.5f + leftWidth * 0.5f;
        float rightX = width * 0.5f - rightWidth * 0.5f;
        PlaceCardRow(prefab, grid, DeckLow, scale, visW, gap, leftX, top - visH * 0.5f);
        PlaceCardRow(prefab, grid, DeckMid, scale, visW, gap, leftX, top - visH - groupGap - visH * 0.5f);
        PlaceCardRow(prefab, grid, DeckMid, scale, visW, gap, leftX, top - visH * 2f - groupGap - gap - visH * 0.5f);
        PlaceCardRow(prefab, grid, DeckHigh, scale, visW, gap, rightX, top - visH * 0.5f);
        PlaceCardRow(prefab, grid, new[] { "J" }, scale, visW, gap, rightX, top - visH - groupGap - visH * 0.5f);
    }

    static void PlaceCardRow(
        GameObject prefab,
        Transform parent,
        string[] cards,
        float scale,
        float visW,
        float gap,
        float centerX,
        float centerY)
    {
        float rowWidth = cards.Length * visW + Mathf.Max(0, cards.Length - 1) * gap;
        float x = centerX - rowWidth * 0.5f + visW * 0.5f;
        for (int i = 0; i < cards.Length; i++)
        {
            PlaceHelpCard(prefab, parent, cards[i], scale, new Vector2(x, centerY));
            x += visW + gap;
        }
    }

    void PlaceCardGrid()
    {
        var grid = _cardGrid != null ? _cardGrid.transform as RectTransform : null;
        if (grid == null)
            return;

        float textHeight = _copyRoot != null && _copyRoot.sizeDelta.y > 1f ? _copyRoot.sizeDelta.y : 180f;
        float textBottom = -StreamsAspectFit.DesignHeight * 0.5f + ButtonInset + ButtonHeight + 8f;
        float limitBottom = textBottom + textHeight + 36f;
        float limitTop = StreamsAspectFit.DesignHeight * 0.5f - BoardTopMargin;
        float half = grid.sizeDelta.y * 0.5f;
        float center = (limitBottom + limitTop) * 0.5f;
        if (center + half > limitTop)
            center = limitTop - half;
        if (center - half < limitBottom)
            center = limitBottom + half;
        grid.anchoredPosition = new Vector2(0f, center);
    }

    static void PlaceHelpCard(GameObject prefab, Transform parent, string value, float scale, Vector2 position)
    {
        var card = Instantiate(prefab, parent);
        card.name = "HelpCard_" + value;
        var rt = card.transform as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = new Vector3(scale, scale, scale);
            rt.anchoredPosition = position;
        }

        var label = card.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = value;
            label.rectTransform.localRotation = Quaternion.identity;
        }

        foreach (var graphic in card.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    void ApplyBoardCards(string[] cards)
    {
        if (_slots == null || cards == null)
            return;

        int count = Mathf.Min(_slots.Count, cards.Length);
        for (int i = 0; i < count; i++)
        {
            if (_slots[i] != null)
                _slots[i].SetFilled(cards[i]);
        }
    }

    void BuildRuns()
    {
        var found = StreamsAscendingRuns.FromCards(DemoCards);
        var kept = new List<(int start, int end)>();
        for (int i = 0; i < found.Count; i++)
        {
            if (found[i].end - found[i].start + 1 >= 2)
                kept.Add(found[i]);
        }

        _runs = kept.ToArray();
    }

    void ShowPage(int page)
    {
        _page = Mathf.Clamp(page, 0, 2);
        if (_board != null)
            _board.SetActive(_page != 1);
        if (_page == 0)
            ApplyBoardCards(Page1Cards);
        else if (_page == 2)
            ApplyBoardCards(DemoCards);
        if (_cardGrid != null)
            _cardGrid.SetActive(_page == 1);
        if (_copy != null)
            _copy.text = KeepWordsTogether(_page == 1 ? PouchText : _page == 2 ? Page3Text : Page1Text);
        LayoutCopy();
        if (_arrowLeft != null)
            _arrowLeft.gameObject.SetActive(true);
        if (_arrowRight != null)
            _arrowRight.gameObject.SetActive(true);

        PlaceChrome();
        bool showOutline = _page == 2;
        SetOutlineVisible(showOutline);
        if (showOutline)
            RefreshOutlines();
        StopRuns();
        if (!isActiveAndEnabled)
            return;
        if (_page == 0)
            _runRoutine = StartCoroutine(PlaySlotLap());
        else if (_page == 2)
            _runRoutine = StartCoroutine(PlayRuns());
    }

    void Update()
    {
        BobArrow(_arrowLeftRt, _arrowLeftBaseX, -1f);
        BobArrow(_arrowRightRt, _arrowRightBaseX, 1f);
    }

    static void BobArrow(RectTransform rt, float baseX, float direction)
    {
        if (rt == null || !rt.gameObject.activeSelf)
            return;

        float bob = Mathf.Sin(Time.unscaledTime * ArrowBobSpeed) * ArrowBobPx;
        Vector2 pos = rt.anchoredPosition;
        pos.x = baseX + direction * bob;
        rt.anchoredPosition = pos;
    }

    void ShowPrevious()
    {
        ShowPage(_page <= 0 ? 2 : _page - 1);
    }

    void ShowNext()
    {
        ShowPage(_page >= 2 ? 0 : _page + 1);
    }

    void StopRuns()
    {
        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }

        for (int i = 0; i < _slotCycles.Count; i++)
        {
            if (_slotCycles[i] != null)
                StopCoroutine(_slotCycles[i]);
        }
        _slotCycles.Clear();

        if (_runRoutine == null)
            return;

        StopCoroutine(_runRoutine);
        _runRoutine = null;
    }

    IEnumerator PlaySlotLap()
    {
        SetAllBrightness(CardDimmed);
        while (_page == 0 && isActiveAndEnabled)
        {
            int count = _slots.Count;
            if (count == 0)
            {
                yield return null;
                continue;
            }

            for (int i = 0; i < count; i++)
            {
                if (_page != 0 || !isActiveAndEnabled)
                    yield break;

                _slotCycles.Add(StartCoroutine(SlotCycle(i)));
                yield return WaitUnscaled(0, SlotBrighten);
                if (_page != 0 || !isActiveAndEnabled)
                    yield break;
            }

            // 마지막 칸이 밝아졌다가 다시 어두워진 뒤에 다음 바퀴를 시작합니다.
            yield return WaitUnscaled(0, SlotBrighten + SlotHold + SlotDarken);
            if (_page != 0 || !isActiveAndEnabled)
                yield break;
            _slotCycles.Clear();
        }
    }

    IEnumerator SlotCycle(int index)
    {
        yield return FadeCover(0, index, CardDimmed, CardLit, SlotBrighten);
        yield return FadeCover(0, index, CardLit, CardLit, SlotHold);
        yield return FadeCover(0, index, CardLit, CardDimmed, SlotDarken);
    }

    IEnumerator FadeCover(int page, int index, float from, float to, float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (elapsed < duration)
        {
            if (_page != page || !isActiveAndEnabled)
                yield break;

            elapsed += Time.unscaledDeltaTime;
            if (index >= 0 && index < _slots.Count)
                SetBrightness(index, Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }

        if (_page == page && isActiveAndEnabled && index >= 0 && index < _slots.Count)
            SetBrightness(index, to);
    }

    IEnumerator WaitUnscaled(int page, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (_page != page || !isActiveAndEnabled)
                yield break;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator PlayRuns()
    {
        SetAllBrightness(CardDimmed);
        while (_page == 2 && isActiveAndEnabled)
        {
            if (_runs.Length == 0)
            {
                yield return null;
                continue;
            }

            for (int i = 0; i < _runs.Length; i++)
            {
                var run = _runs[i];
                if (!BeginFade(2, run.start, run.end, CardDimmed, CardLit, RunBrighten))
                    yield break;
                yield return _fadeRoutine;
                _fadeRoutine = null;

                if (!BeginFade(2, run.start, run.end, CardLit, CardLit, RunHold))
                    yield break;
                yield return _fadeRoutine;
                _fadeRoutine = null;

                if (!BeginFade(2, run.start, run.end, CardLit, CardDimmed, RunDarken))
                    yield break;
                yield return _fadeRoutine;
                _fadeRoutine = null;
            }
        }
    }

    bool BeginFade(int page, int start, int end, float from, float to, float duration)
    {
        if (_page != page || !isActiveAndEnabled)
            return false;

        _fadeRoutine = StartCoroutine(FadeRun(page, start, end, from, to, duration));
        return true;
    }

    IEnumerator FadeRun(int page, int start, int end, float from, float to, float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (elapsed < duration)
        {
            if (_page != page || !isActiveAndEnabled)
                yield break;

            elapsed += Time.unscaledDeltaTime;
            PaintRun(start, end, Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }

        if (_page == page && isActiveAndEnabled)
            PaintRun(start, end, to);
    }

    void RefreshOutlines()
    {
        if (_slots == null || _slots.Count == 0 || _runs.Length == 0)
            return;

        Canvas.ForceUpdateCanvases();
        StreamsRunOutlineOverlay.Refresh(_slots, _runs);
        RaiseOutline();
    }

    void RaiseOutline()
    {
        if (_board == null)
            return;

        Transform outline = _board.transform.Find("RunOutlines");
        if (outline == null)
            return;

        RaiseAboveDim(outline.gameObject, SortingOrder + 3, false);
        var graphic = outline.GetComponent<Graphic>();
        if (graphic != null)
            graphic.SetVerticesDirty();
    }

    void SetOutlineVisible(bool on)
    {
        if (_board == null)
            return;

        Transform outline = _board.transform.Find("RunOutlines");
        if (outline == null)
            return;

        outline.gameObject.SetActive(on);
    }

    void PaintRun(int start, int end, float brightness)
    {
        int last = Mathf.Min(end, _slots.Count - 1);
        for (int i = Mathf.Max(0, start); i <= last; i++)
            SetBrightness(i, brightness);
    }

    void SetAllBrightness(float brightness)
    {
        for (int i = 0; i < _slots.Count; i++)
            SetBrightness(i, brightness);
    }

    void SetBrightness(int index, float brightness)
    {
        if (index < 0 || index >= _slots.Count)
            return;

        SetSlotBrightness(_slots[index], brightness);
    }

    static void SetSlotBrightness(StreamsUiSlot slot, float brightness)
    {
        if (slot == null || slot.targetImage == null)
            return;

        float level = Mathf.Clamp01(brightness);
        slot.targetImage.color = new Color(level, level, level, 1f);
    }

    float BoardSlotCanvasSize()
    {
        const float fallback = 217.6f;
        if (_slots == null || _slots.Count == 0 || _slots[0] == null)
            return fallback;

        var slot = _slots[0].transform as RectTransform;
        var space = transform as RectTransform;
        if (slot == null)
            return fallback;

        float side = slot.rect.width > 1f ? slot.rect.width : slot.sizeDelta.x;
        float slotScale = Mathf.Abs(slot.lossyScale.x);
        float spaceScale = space != null ? Mathf.Abs(space.lossyScale.x) : 1f;
        if (side < 1f || slotScale < 0.0001f || spaceScale < 0.0001f)
            return fallback;

        return side * slotScale / spaceScale;
    }

    Button CreateArrow(string name, float anchorX, float x, bool flip, Sprite sprite)
    {
        var image = CreateImage(name, transform, new Color(1f, 1f, 1f, ArrowAlpha));
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(180f, 180f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.localScale = new Vector3(flip ? -1f : 1f, 1f, 1f);
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = true;

        var button = image.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        image.gameObject.AddComponent<UiButtonPressScale>();
        RaiseAboveDim(image.gameObject, SortingOrder + 3, true);
        return button;
    }

    static void RaiseAboveDim(GameObject go, int order, bool raycast)
    {
        if (go == null)
            return;

        var canvas = go.GetComponent<Canvas>();
        if (canvas == null)
            canvas = go.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = order;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;

        var raycaster = go.GetComponent<GraphicRaycaster>();
        if (raycast)
        {
            if (raycaster == null)
                go.AddComponent<GraphicRaycaster>();
        }
        else if (raycaster != null)
        {
            raycaster.enabled = false;
        }
    }

    Button CreateConfirm()
    {
        var image = CreateImage("ConfirmButton", transform, new Color(0f, 0.5686275f, 0.8156863f, 1f));
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        image.sprite = LoadSprite("Button2");
        image.type = Image.Type.Sliced;
        image.preserveAspect = false;
        image.pixelsPerUnitMultiplier = 1f;
        image.raycastTarget = true;

        var button = image.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        var label = CreateText("Label", image.transform, "알겠어요!", 85f);
        Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.color = Color.white;

        rt.sizeDelta = new Vector2(1024f, 512f);
        rt.localScale = new Vector3(0.375f, 0.375f, 1f);
        rt.anchoredPosition = new Vector2(0f, ButtonInset + ButtonHeight * 0.5f);

        image.gameObject.AddComponent<UiButtonPressScale>();
        RaiseAboveDim(image.gameObject, SortingOrder + 4, true);
        return button;
    }

    void PlaceChrome()
    {
        if (_board != null)
            _board.transform.SetAsFirstSibling();

        var order = new List<Transform>();
        if (_dim != null)
            order.Add(_dim.transform);
        if (_board != null)
            order.Add(_board.transform);
        if (_cardGrid != null)
            order.Add(_cardGrid.transform);
        if (_copyRoot != null)
            order.Add(_copyRoot);
        if (_confirm != null)
            order.Add(_confirm.transform);

        for (int i = 0; i < order.Count; i++)
            order[i].SetSiblingIndex(i);
    }

    void BuildEdgeButtons()
    {
        CreateEdgeButton("EdgeLeft", 0f, 0.5f, 0f, 1f, ShowPrevious);
        CreateEdgeButton("EdgeRight", 0.5f, 1f, 0f, 1f, ShowNext);
    }

    Button CreateEdgeButton(
        string name,
        float anchorMinX,
        float anchorMaxX,
        float anchorMinY,
        float anchorMaxY,
        UnityEngine.Events.UnityAction onClick)
    {
        var image = CreateImage(name, transform, new Color(0f, 0f, 0f, 0f));
        var rt = image.rectTransform;
        rt.anchorMin = new Vector2(anchorMinX, anchorMinY);
        rt.anchorMax = new Vector2(anchorMaxX, anchorMaxY);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        image.raycastTarget = true;

        var button = image.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);
        RaiseAboveDim(image.gameObject, SortingOrder + 3, true);
        return button;
    }

    void BuildCopy()
    {
        var rootGo = new GameObject("Copy", typeof(RectTransform));
        _copyRoot = rootGo.GetComponent<RectTransform>();
        _copyRoot.SetParent(transform, false);

        var bg = CreateImage("Bg", _copyRoot, new Color(0.06f, 0.06f, 0.06f, 0.88f));
        Stretch(bg.rectTransform);

        _copy = CreateText("Label", _copyRoot, Page1Text, 36f);
        var labelRt = _copy.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(14f, 10f);
        labelRt.offsetMax = new Vector2(-14f, -10f);
        _copy.alignment = TextAlignmentOptions.Center;

        RaiseAboveDim(rootGo, SortingOrder + 4, false);
    }

    void LayoutCopy()
    {
        if (_copyRoot == null || _copy == null)
            return;

        float wrap = _page == 1 ? 760f : HelpBoardWidth() * 0.92f;
        _copy.textWrappingMode = TextWrappingModes.Normal;
        Vector2 pref = _copy.GetPreferredValues(_copy.text, wrap, 0f);
        float width = Mathf.Clamp(pref.x + 28f, 80f, wrap + 28f);
        float height = Mathf.Max(pref.y + 20f, 48f);
        _copyRoot.sizeDelta = new Vector2(width, height);

        if (_page == 1)
        {
            PlaceCopyBottomCenter();
            PlaceCardGrid();
        }
        else
        {
            PlaceCopyInsideBoard();
        }
    }

    void PlaceCopyBottomCenter()
    {
        _copyRoot.anchorMin = _copyRoot.anchorMax = new Vector2(0.5f, 0f);
        _copyRoot.pivot = new Vector2(0.5f, 0f);
        _copyRoot.localScale = Vector3.one;
        _copyRoot.anchoredPosition = new Vector2(0f, ButtonInset + ButtonHeight + 8f);
    }

    void PlaceCopyInsideBoard()
    {
        var board = _board != null ? _board.transform as RectTransform : null;
        _copyRoot.anchorMin = _copyRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _copyRoot.pivot = new Vector2(0.5f, 0f);
        _copyRoot.localScale = Vector3.one;
        if (board == null)
        {
            _copyRoot.anchoredPosition = new Vector2(0f, 0f);
            return;
        }

        float buttonTop = -StreamsAspectFit.DesignHeight * 0.5f + ButtonInset + ButtonHeight;
        float y = buttonTop + 8f;
        _copyRoot.anchoredPosition = new Vector2(0f, y);
    }

    float HelpBoardWidth()
    {
        var board = _board != null ? _board.transform as RectTransform : null;
        if (board == null)
            return 640f;
        return board.sizeDelta.x * board.localScale.x;
    }

    static float HelpBoardCenterY(float boardHeight)
    {
        float half = boardHeight * 0.5f;
        float topLimit = StreamsAspectFit.DesignHeight * 0.5f - 24f;
        float bottomLimit = -StreamsAspectFit.DesignHeight * 0.5f + 24f;
        float center = -20f;
        if (center + half > topLimit)
            center = topLimit - half;
        if (center - half < bottomLimit)
            center = bottomLimit + half;
        return center;
    }

    static string KeepWordsTogether(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var builder = new StringBuilder(text.Length + 16);
        int index = 0;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '\n' || c == ' ')
            {
                builder.Append(c);
                index++;
                continue;
            }

            int start = index;
            while (index < text.Length && text[index] != '\n' && text[index] != ' ')
                index++;

            builder.Append("<nobr>");
            builder.Append(text, start, index - start);
            builder.Append("</nobr>");
        }

        return builder.ToString();
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.richText = true;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        tmp.overflowMode = TextOverflowModes.Overflow;

        TMP_FontAsset font = ResolveFont();
        if (font != null)
        {
            tmp.font = font;
            tmp.fontSharedMaterial = font.material;
        }

        return tmp;
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    void ApplyGameScaler()
    {
        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();

        CanvasScaler source = null;
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null || canvases[i].name != "BoardCanvas")
                continue;
            source = canvases[i].GetComponent<CanvasScaler>();
            break;
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = source != null ? source.referenceResolution : new Vector2(1920f, 1080f);
        scaler.screenMatchMode = source != null
            ? source.screenMatchMode
            : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = source != null ? source.matchWidthOrHeight : 0f;
        scaler.referencePixelsPerUnit = source != null ? source.referencePixelsPerUnit : 100f;
    }

    void AttachAspectFit()
    {
        if (GetComponent<StreamsAspectFit>() == null)
            gameObject.AddComponent<StreamsAspectFit>();

        // 기본 맞춤은 화면 전체를 불투명 검정으로 덮습니다. 알파 0.8 막이 게임을 비치게 두려고 그 판만 투명하게 합니다.
        Transform fill = transform.Find("AspectBars/Fill");
        if (fill == null)
            return;

        var image = fill.GetComponent<Image>();
        if (image == null)
            return;

        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
    }

    static GameObject ResolveCardPrefab()
    {
        var found = FindObjectsByType<StreamsCardDrawCinematic>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null && found[i].cardPrefab != null)
                return found[i].cardPrefab;
        }

#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Card.prefab");
#else
        return null;
#endif
    }

    static Sprite LoadSprite(string fileName)
    {
#if UNITY_EDITOR
        var editorSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Sprites/{fileName}.png");
        if (editorSprite != null)
            return editorSprite;
#endif
        // AssetDatabase 경로는 플레이어 빌드에 포함되지 않습니다.
        // 단일 스프라이트는 Resources.Load<Sprite>가 비는 경우가 있어, 직접 참조한 프리팹을 봅니다.
        var holder = Resources.Load<GameObject>(fileName + "Ref");
        if (holder != null)
        {
            var held = holder.GetComponent<Image>();
            if (held != null && held.sprite != null)
                return held.sprite;
        }

        var images = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image != null && image.sprite != null && image.sprite.name == fileName)
                return image.sprite;
        }

        Sprite fromResources = Resources.Load<Sprite>(fileName);
        if (fromResources != null)
            return fromResources;

        Debug.LogWarning($"StreamsHelpPopup: {fileName} 스프라이트를 찾지 못했습니다.");
        return null;
    }

    static TMP_FontAsset ResolveFont()
    {
#if UNITY_EDITOR
        var fromAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (fromAsset != null)
            return fromAsset;
#endif
        TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
        var labels = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] == null || labels[i].font == null || labels[i].font == fallback)
                continue;
            return labels[i].font;
        }

        return fallback;
    }
}
