using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 카드 뽑기 연출. 주머니는 아래쪽에서 멈추고, 카드가 중앙으로 가며 회전한 뒤 앞면을 보여 줍니다.
/// </summary>
public class StreamsCardDrawCinematic : MonoBehaviour
{
    [Header("주머니")]
    [Tooltip("주머니 스프라이트. Assets/Sprites/Pouch.png")]
    public Sprite pouchSprite;

    [Header("카드")]
    [Tooltip("UI 카드 프리팹. 루트에 Image, 하위에 TMP가 있으면 숫자를 넣습니다.")]
    public GameObject cardPrefab;

    [Header("연결 (비우면 자동 생성)")]
    [Tooltip("화면을 어둡히는 전체 Image. 비우면 런타임에 만듭니다.")]
    public Image dimmer;
    [Tooltip("주머니 RectTransform. 비우면 런타임에 만듭니다.")]
    public RectTransform pocket;
    [Tooltip("주머니 Image. 비우면 런타임에 만듭니다.")]
    public Image pocketImage;
    [Tooltip("왼쪽 아래 스킵 안내. 비우면 런타임에 만듭니다.")]
    public TextMeshProUGUI skipHint;

    [Header("타이밍")]
    [Tooltip("화면이 얼마나 어두워질지 (0=그대로, 1=완전 검정).")]
    public float dimAlpha = 0.45f;
    [Tooltip("화면이 어두워지는 시간(초).")]
    public float dimInDuration = 0.5f;
    [Tooltip("주머니가 아래에서 멈춤 위치까지 올라오는 시간(초).")]
    public float pocketRiseDuration = 0.8f;
    [Tooltip("카드가 중앙으로 이동하며 회전하는 시간(초).")]
    public float travelDuration = 1.5f;
    [Tooltip("주머니가 다시 아래로 내려가는 시간(초).")]
    public float pocketDownDuration = 1.1f;
    [Tooltip("앞면에서 멈춘 뒤 커졌다 원래 크기로 돌아오는 시간(초).")]
    public float punchDuration = 0.44f;
    [Tooltip("앞면을 보여 준 뒤 페이드 전까지 유지하는 시간(초).")]
    public float holdFrontDuration = 0.4f;
    [Tooltip("카드가 서서히 사라지는 시간(초).")]
    public float fadeOutDuration = 1.1f;
    [Tooltip("화면이 다시 밝아지는 시간(초).")]
    public float dimOutDuration = 1.1f;

    [Header("회전")]
    [Tooltip("뒷면에서 시작해 이 바퀴 수만큼 돈 뒤 앞면에서 멈춥니다.")]
    public int spinTurns = 3;

    [Header("연출")]
    [Tooltip("앞면에서 멈출 때 커지는 배율. 1이면 크기 변화 없음.")]
    public float punchScale = 1.14f;
    [Tooltip("카드 주변으로 퍼지는 반짝임 개수.")]
    public int sparkleCount = 12;
    [Tooltip("반짝임이 퍼지며 사라지는 시간(초).")]
    public float sparkleDuration = 1.4f;
    [Tooltip("반짝임 스프라이트. 비우면 노란 마름모를 씁니다.")]
    public Sprite sparkleSprite;

    [Header("위치")]
    [Tooltip("화면 높이 비율. 값이 클수록 주머니가 더 아래에서 멈춥니다.")]
    public float pocketStopScreenY = 0.3f;
    [Tooltip("화면 밖으로 더 밀어내는 여유(픽셀). 커질수록 생성·퇴장이 더 아래에서 이뤄집니다.")]
    public float pocketOffscreenPadding = 80f;
    [Tooltip("주머니 UI 크기(픽셀). 스프라이트가 있으면 비율에 맞춰 조정됩니다.")]
    public Vector2 pouchSize = new Vector2(420, 360);

    const string SkipHintText = "터치하여 연출 스킵";
    const float SkipHintAlpha = 0.5f;
    const string SkipHintFontPath = "Assets/Fonts/GmarketSansTTFBold SDF.asset";

    static StreamsCardDrawCinematic _instance;

    RectTransform _card;
    CanvasGroup _cardGroup;
    TextMeshProUGUI _cardNumber;
    GameObject _cardInstance;
    Vector3 _cardBaseScale = Vector3.one;
    readonly List<GameObject> _sparkles = new List<GameObject>();
    readonly List<Image> _sparklePool = new List<Image>();
    RectTransform _sparkleRoot;
    bool _playing;
    bool _skipIntro;
    bool _skipIntroAllowed;
    static int _blockPlacementUntilFrame = -1;
    static bool _waitForPointerRelease;

    public static bool IsBlockingPlacement =>
        (_instance != null && _instance._playing)
        || _waitForPointerRelease
        || Time.frameCount <= _blockPlacementUntilFrame;

    public static IEnumerator PlayNow(string cardValue)
    {
        var inst = EnsureInstance();
        if (inst == null)
            yield break;

        yield return inst.Play(cardValue);
    }

    static StreamsCardDrawCinematic EnsureInstance()
    {
        if (_instance != null)
            return _instance;

        _instance = FindFirstObjectByType<StreamsCardDrawCinematic>(FindObjectsInactive.Include);
        if (_instance != null)
            return _instance;

        var canvas = FindBoardCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("StreamsCardDrawCinematic: BoardCanvas가 없어 연출을 건너뜁니다.");
            return null;
        }

        var go = new GameObject("CardDrawCinematic", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsLastSibling();
        StretchFull(go.GetComponent<RectTransform>());

        _instance = go.AddComponent<StreamsCardDrawCinematic>();
        _instance.BuildUi();
        return _instance;
    }

    static Canvas FindBoardCanvas()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas != null && canvas.name == "BoardCanvas")
                return canvas;
        }

        return FindFirstObjectByType<Canvas>();
    }

    void Awake()
    {
        _instance = this;
        ResolvePouchSprite();
        if (dimmer == null || pocket == null)
            BuildUi();
        HideImmediate();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
        _playing = false;
        _waitForPointerRelease = false;
        ClearSparkles();
        DestroyCardInstance();
    }

    IEnumerator Play(string cardValue)
    {
        ResolvePouchSprite();
        if (dimmer == null || pocket == null)
            BuildUi();

        if (!EnsureCardInstance())
        {
            Debug.LogWarning("StreamsCardDrawCinematic: cardPrefab이 없어 연출을 건너뜁니다.");
            yield break;
        }

        if (skipHint == null)
            EnsureSkipHint();
        if (skipHint != null && _cardNumber != null && _cardNumber.font != null)
        {
            skipHint.font = _cardNumber.font;
            skipHint.fontSharedMaterial = _cardNumber.fontSharedMaterial;
        }

        StopAllCoroutines();
        ClearSparkles();

        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        string display = string.IsNullOrEmpty(cardValue) ? "" : cardValue.Trim().ToUpperInvariant();

        _playing = true;
        _skipIntro = false;
        _skipIntroAllowed = true;
        SetSkipHintVisible(true);

        Coroutine pocketDown = null;
        if (_cardNumber != null)
        {
            _cardNumber.text = display == "J" ? "J" : display;
            _cardNumber.alpha = 0f;
        }

        if (pocketImage != null && pouchSprite != null)
            pocketImage.sprite = pouchSprite;

        SetDimAlpha(0f);
        SetClickBlocker(true);
        _cardInstance.SetActive(true);
        if (_cardGroup != null)
            _cardGroup.alpha = 1f;

        Vector2 center = Vector2.zero;
        Vector2 pocketOff = PocketOffscreenPosition();
        Vector2 pocketStop = new Vector2(0f, -OverlayHeight() * Mathf.Max(0.08f, pocketStopScreenY));
        Vector2 tucked = CardTuckedOffset();
        float revealedAngle = SpinEndAngle();

        SetActive(pocket, false);
        pocket.anchoredPosition = pocketOff;
        pocket.localScale = Vector3.one;
        SetActive(pocket, true);

        PlaceCardBehindPocket();
        _card.localScale = _cardBaseScale;
        _card.localEulerAngles = Vector3.zero;
        ApplyFlipAngle(180f);
        _card.anchoredPosition = pocketOff + tucked;
        SetCardVisible(true);

        yield return FadeDim(dimAlpha, dimInDuration);
        yield return MovePocketWithCardBehind(pocketOff, pocketStop, tucked, pocketRiseDuration);

        PlaceCardBehindPocket();
        yield return TravelAndSpin(_card.anchoredPosition, center, travelDuration);

        if (_skipIntro)
            SnapToRevealed(center, pocketStop, revealedAngle);

        _skipIntroAllowed = false;
        _skipIntro = false;
        SetSkipHintVisible(false);

        pocketDown = StartCoroutine(MoveAnchored(pocket, pocketStop, pocketOff, pocketDownDuration));
        ReleaseToBoard(display);
        StartCoroutine(PlayOutro(pocketDown));
    }

    IEnumerator PlayOutro(Coroutine pocketDown)
    {
        Coroutine punch = StartCoroutine(PunchScale());
        Coroutine sparkles = StartCoroutine(BurstSparkles());
        Coroutine fadeCard = StartCoroutine(FadeCard(1f, 0f, fadeOutDuration));
        yield return FadeDim(0f, dimOutDuration);

        if (punch != null)
            yield return punch;
        if (fadeCard != null)
            yield return fadeCard;
        if (sparkles != null)
            yield return sparkles;
        if (pocketDown != null)
            yield return pocketDown;

        SetActive(pocket, false);
        HideImmediate();
    }

    void ReleaseToBoard(string display)
    {
        SetClickBlocker(false);
        FindPlayerHoldingSlot()?.SetFilled(display);
        ShowEmptySlotHints();
        _playing = false;
        _skipIntroAllowed = false;
        _skipIntro = false;
        _waitForPointerRelease = IsPointerHeld();
        if (_waitForPointerRelease)
            StartCoroutine(WaitPointerRelease());
    }

    IEnumerator WaitPointerRelease()
    {
        while (IsPointerHeld())
            yield return null;
        yield return null;
        _blockPlacementUntilFrame = Time.frameCount + 1;
        _waitForPointerRelease = false;
    }

    void Update()
    {
        if (!_playing || !_skipIntroAllowed || _skipIntro)
            return;

        if (WasSkipInput())
        {
            _skipIntro = true;
            SetSkipHintVisible(false);
        }
    }

    float SpinEndAngle()
    {
        return 360f * (Mathf.Max(1, spinTurns) + 1);
    }

    void SnapToRevealed(Vector2 cardCenter, Vector2 pocketStop, float revealedAngle)
    {
        SetDimAlpha(dimAlpha);
        SetActive(pocket, true);
        pocket.anchoredPosition = pocketStop;
        _card.anchoredPosition = cardCenter;
        _card.localScale = _cardBaseScale;
        ApplyFlipAngle(revealedAngle);
        PlaceCardBehindPocket();
    }

    static bool WasSkipInput()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            return true;
        if (Keyboard.current != null &&
            (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame))
            return true;
        return false;
    }

    static bool IsPointerHeld()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            return true;
        return false;
    }

    bool EnsureCardInstance()
    {
        if (cardPrefab == null)
            return false;

        if (_cardInstance != null && _card != null)
            return true;

        DestroyCardInstance();

        _cardInstance = Instantiate(cardPrefab, transform);
        _cardInstance.name = cardPrefab.name;
        _card = _cardInstance.transform as RectTransform;
        if (_card == null)
        {
            Debug.LogWarning("StreamsCardDrawCinematic: cardPrefab에 RectTransform이 없습니다. UI 프리팹을 넣으세요.");
            DestroyCardInstance();
            return false;
        }

        _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
        _card.pivot = new Vector2(0.5f, 0.5f);
        _card.anchoredPosition = Vector2.zero;
        _cardBaseScale = _card.localScale;

        _cardGroup = _cardInstance.GetComponent<CanvasGroup>();
        if (_cardGroup == null)
            _cardGroup = _cardInstance.AddComponent<CanvasGroup>();
        _cardGroup.blocksRaycasts = false;
        _cardGroup.interactable = false;

        _cardNumber = _cardInstance.GetComponentInChildren<TextMeshProUGUI>(true);

        foreach (var graphic in _cardInstance.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        PlaceCardBehindPocket();
        return true;
    }

    void PlaceCardBehindPocket()
    {
        if (_card == null || pocket == null)
            return;

        _card.SetParent(transform, true);
        if (dimmer != null)
            dimmer.transform.SetAsFirstSibling();
        pocket.SetAsLastSibling();

        SetOverrideSort(dimmer != null ? dimmer.gameObject : null, 100);
        SetOverrideSort(_cardInstance, 101);
        SetOverrideSort(pocket.gameObject, 102);
    }

    static void SetOverrideSort(GameObject target, int order)
    {
        if (target == null)
            return;

        var canvas = target.GetComponent<Canvas>();
        if (canvas == null)
            canvas = target.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = order;

        var raycaster = target.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            raycaster.enabled = false;
    }

    void DestroyCardInstance()
    {
        if (_cardInstance != null)
        {
            if (Application.isPlaying)
                Destroy(_cardInstance);
            else
                DestroyImmediate(_cardInstance);
        }

        _cardInstance = null;
        _card = null;
        _cardGroup = null;
        _cardNumber = null;
    }

    Vector2 CardTuckedOffset()
    {
        if (_card == null)
            return new Vector2(0f, -80f);

        return new Vector2(0f, -Mathf.Abs(_card.sizeDelta.y * _cardBaseScale.y) * 0.2f);
    }

    float OverlayHeight()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.pixelRect.height > 1f)
            return canvas.pixelRect.height / Mathf.Max(canvas.scaleFactor, 0.0001f);
        return Screen.height;
    }

    float PocketHalfHeight()
    {
        if (pocket == null)
            return pouchSize.y * 0.5f;

        float height = pocket.rect.height;
        if (height < 1f)
            height = pocket.sizeDelta.y;
        return Mathf.Abs(height * pocket.localScale.y) * 0.5f;
    }

    Vector2 PocketOffscreenPosition()
    {
        float y = -(OverlayHeight() * 0.5f + PocketHalfHeight() + Mathf.Max(0f, pocketOffscreenPadding));
        return new Vector2(0f, y);
    }

    IEnumerator MovePocketWithCardBehind(Vector2 pocketFrom, Vector2 pocketTo, Vector2 cardOffset, float duration)
    {
        if (duration <= 0.001f)
        {
            pocket.anchoredPosition = pocketTo;
            _card.anchoredPosition = pocketTo + cardOffset;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            if (_skipIntro)
                yield break;
            t += Time.deltaTime;
            float u = EaseOut(Mathf.Clamp01(t / duration));
            Vector2 p = Vector2.LerpUnclamped(pocketFrom, pocketTo, u);
            pocket.anchoredPosition = p;
            _card.anchoredPosition = p + cardOffset;
            yield return null;
        }

        pocket.anchoredPosition = pocketTo;
        _card.anchoredPosition = pocketTo + cardOffset;
    }

    IEnumerator TravelAndSpin(Vector2 from, Vector2 to, float duration)
    {
        float startAngle = 180f;
        float endAngle = SpinEndAngle();

        if (duration <= 0.001f)
        {
            _card.anchoredPosition = to;
            ApplyFlipAngle(endAngle);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            if (_skipIntro)
                yield break;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float moveU = EaseOut(u);
            float spinU = EaseOutSpin(u);
            _card.anchoredPosition = Vector2.LerpUnclamped(from, to, moveU);
            ApplyFlipAngle(Mathf.LerpUnclamped(startAngle, endAngle, spinU));
            yield return null;
        }

        _card.anchoredPosition = to;
        ApplyFlipAngle(endAngle);
    }

    void ApplyFlipAngle(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sx = _cardBaseScale.x * Mathf.Max(0.001f, Mathf.Abs(cos));
        _card.localScale = new Vector3(sx, _cardBaseScale.y, _cardBaseScale.z);

        if (_cardNumber != null)
            _cardNumber.alpha = cos >= 0f ? 1f : 0f;
    }

    IEnumerator PunchScale()
    {
        Vector3 from = _cardBaseScale;
        Vector3 peak = _cardBaseScale * punchScale;
        float duration = Mathf.Max(0.08f, punchDuration);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = EaseOut(Mathf.Clamp01(t / duration));
            float punch = u <= 0.5f ? u * 2f : 2f - u * 2f;
            _card.localScale = Vector3.LerpUnclamped(from, peak, punch);
            yield return null;
        }

        _card.localScale = from;
    }

    IEnumerator BurstSparkles()
    {
        ClearSparkles();
        EnsureSparkleRoot();
        Vector2 origin = _card != null ? _card.anchoredPosition : Vector2.zero;
        int count = Mathf.Max(4, sparkleCount);
        var items = new Sparkle[count];

        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i + Random.Range(-18f, 18f);
            float rad = angle * Mathf.Deg2Rad;
            float speed = Random.Range(220f, 420f);
            float size = Random.Range(10f, 22f);
            var image = RentSparkle();
            image.color = new Color(1f, 0.95f, 0.72f, 1f);
            if (sparkleSprite != null)
                image.sprite = sparkleSprite;
            image.raycastTarget = false;
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = origin;
            rt.localEulerAngles = new Vector3(0f, 0f, 45f);
            rt.localScale = Vector3.one;
            image.gameObject.SetActive(true);
            _sparkles.Add(image.gameObject);
            items[i] = new Sparkle
            {
                rt = rt,
                image = image,
                velocity = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * speed,
                life = sparkleDuration * Random.Range(0.7f, 1f),
                size = size
            };
        }

        float elapsed = 0f;
        float maxLife = sparkleDuration;
        while (elapsed < maxLife)
        {
            elapsed += Time.deltaTime;
            for (int i = 0; i < items.Length; i++)
            {
                var s = items[i];
                if (s.rt == null)
                    continue;

                float u = Mathf.Clamp01(elapsed / s.life);
                float moveU = EaseOut(u);
                s.rt.anchoredPosition = origin + s.velocity * (moveU * s.life);
                float fade = 1f - u;
                float pop = 1f + 0.35f * (1f - u);
                s.rt.localScale = Vector3.one * pop;
                if (s.image != null)
                {
                    Color c = s.image.color;
                    c.a = fade;
                    s.image.color = c;
                }
            }

            yield return null;
        }

        ClearSparkles();
    }

    void EnsureSparkleRoot()
    {
        if (_sparkleRoot != null)
            return;

        var go = new GameObject("Sparkles", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        _sparkleRoot = go.GetComponent<RectTransform>();
        StretchFull(_sparkleRoot);
        SetOverrideSort(go, 103);
    }

    Image RentSparkle()
    {
        EnsureSparkleRoot();
        for (int i = 0; i < _sparklePool.Count; i++)
        {
            var pooled = _sparklePool[i];
            if (pooled != null && !pooled.gameObject.activeSelf)
                return pooled;
        }

        var image = CreateImage("Sparkle", _sparkleRoot, new Color(1f, 0.95f, 0.72f, 1f));
        image.raycastTarget = false;
        _sparklePool.Add(image);
        return image;
    }

    void ClearSparkles()
    {
        for (int i = 0; i < _sparklePool.Count; i++)
        {
            if (_sparklePool[i] != null)
                _sparklePool[i].gameObject.SetActive(false);
        }

        _sparkles.Clear();
    }

    static float EaseOut(float u)
    {
        u = Mathf.Clamp01(u);
        float inv = 1f - u;
        return 1f - inv * inv * inv * inv * inv;
    }

    static float EaseOutSpin(float u)
    {
        u = Mathf.Clamp01(u);
        float inv = 1f - u;
        return 1f - inv * inv;
    }

    IEnumerator MoveAnchored(RectTransform target, Vector2 from, Vector2 to, float duration)
    {
        if (duration <= 0.001f)
        {
            target.anchoredPosition = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = EaseOut(Mathf.Clamp01(t / duration));
            target.anchoredPosition = Vector2.LerpUnclamped(from, to, u);
            yield return null;
        }

        target.anchoredPosition = to;
    }

    IEnumerator FadeDim(float to, float duration)
    {
        float from = dimmer != null ? dimmer.color.a : 0f;
        if (duration <= 0.001f)
        {
            SetDimAlpha(to);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            if (_skipIntro)
                yield break;
            t += Time.deltaTime;
            SetDimAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
            yield return null;
        }

        SetDimAlpha(to);
    }

    IEnumerator FadeCard(float from, float to, float duration)
    {
        if (_cardGroup == null)
            yield break;

        if (duration <= 0.001f)
        {
            _cardGroup.alpha = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            _cardGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }

        _cardGroup.alpha = to;
    }

    void SetDimAlpha(float a)
    {
        if (dimmer == null)
            return;
        Color c = dimmer.color;
        c.a = a;
        dimmer.color = c;
    }

    void SetCardVisible(bool on)
    {
        if (_cardInstance != null)
            _cardInstance.SetActive(on);
    }

    static void SetActive(Component c, bool on)
    {
        if (c != null)
            c.gameObject.SetActive(on);
    }

    void HideImmediate()
    {
        ClearSparkles();
        SetDimAlpha(0f);
        SetActive(pocket, false);
        SetSkipHintVisible(false);
        if (_cardInstance != null)
            _cardInstance.SetActive(false);
        if (_cardGroup != null)
            _cardGroup.alpha = 1f;
        if (_cardNumber != null)
            _cardNumber.alpha = 0f;
        if (_card != null)
            _card.localScale = _cardBaseScale;
    }

    void SetClickBlocker(bool on)
    {
        Image blocker = dimmer;
        if (blocker == null)
            return;
        blocker.canvasRenderer.cullTransparentMesh = false;
        blocker.raycastTarget = on;
        if (on)
        {
            Color c = blocker.color;
            if (c.a < 1f / 255f)
            {
                c.a = 1f / 255f;
                blocker.color = c;
            }
        }
    }

    static StreamsUiSlot FindPlayerHoldingSlot()
    {
        foreach (var slot in FindObjectsByType<StreamsUiSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (slot != null && slot.IsHoldingPreview)
                return slot;
        }

        return null;
    }

    static void ShowEmptySlotHints()
    {
        foreach (var slot in FindObjectsByType<StreamsUiSlot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (slot != null && !slot.isFilled)
                slot.SetHint();
        }
    }

    void ResolvePouchSprite()
    {
        if (pouchSprite == null)
            pouchSprite = LoadSprite("Pouch");
    }

    static Sprite LoadSprite(string fileName)
    {
#if UNITY_EDITOR
        var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Sprites/{fileName}.png");
        if (sprite != null)
            return sprite;
#endif
        return Resources.Load<Sprite>(fileName);
    }

    void BuildUi()
    {
        var root = GetComponent<RectTransform>();
        if (root == null)
            root = gameObject.AddComponent<RectTransform>();
        StretchFull(root);

        if (dimmer == null)
        {
            dimmer = CreateImage("Dimmer", transform, new Color(0f, 0f, 0f, 0f));
            StretchFull(dimmer.rectTransform);
            dimmer.raycastTarget = false;
        }

        if (pocket == null)
        {
            pocketImage = CreateImage("Pocket", transform, Color.white);
            pocket = pocketImage.rectTransform;
            pocket.anchorMin = pocket.anchorMax = new Vector2(0.5f, 0.5f);
            pocket.pivot = new Vector2(0.5f, 0.5f);
            pocket.sizeDelta = pouchSize;
            pocketImage.preserveAspect = true;
            pocketImage.raycastTarget = false;
            if (pouchSprite != null)
            {
                pocketImage.sprite = pouchSprite;
                ApplyNativeSize(pocket, pouchSprite, pouchSize);
            }
        }

        EnsureSkipHint();
        HideImmediate();
    }

    void EnsureSkipHint()
    {
        if (skipHint != null)
            return;

        var go = new GameObject("SkipHint", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(1120f, 112f);
        rt.anchoredPosition = new Vector2(40f, 32f);
        rt.localScale = Vector3.one;

        skipHint = go.GetComponent<TextMeshProUGUI>();
        skipHint.text = SkipHintText;
        skipHint.fontSize = 64;
        skipHint.color = new Color(1f, 1f, 1f, SkipHintAlpha);
        skipHint.alignment = TextAlignmentOptions.BottomLeft;
        skipHint.raycastTarget = false;
        skipHint.enableWordWrapping = false;
        skipHint.overflowMode = TextOverflowModes.Overflow;

        TMP_FontAsset font = ResolveUiFont();
        if (font != null)
        {
            skipHint.font = font;
            skipHint.fontSharedMaterial = font.material;
        }

        SetOverrideSort(go, 110);
    }

    void SetSkipHintVisible(bool on)
    {
        if (skipHint == null && on)
            EnsureSkipHint();
        if (skipHint == null)
            return;

        skipHint.gameObject.SetActive(on);
        if (on)
        {
            skipHint.text = SkipHintText;
            Color c = skipHint.color;
            c.a = SkipHintAlpha;
            skipHint.color = c;
            skipHint.transform.SetAsLastSibling();
        }
    }

    static TMP_FontAsset ResolveUiFont()
    {
#if UNITY_EDITOR
        var fromAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SkipHintFontPath);
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

    static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.anchoredPosition = Vector2.zero;
    }

    static void ApplyNativeSize(RectTransform rt, Sprite sprite, Vector2 fallback)
    {
        if (sprite == null)
        {
            rt.sizeDelta = fallback;
            return;
        }

        float w = sprite.rect.width;
        float h = sprite.rect.height;
        if (w < 1f || h < 1f)
        {
            rt.sizeDelta = fallback;
            return;
        }

        float scale = Mathf.Min(fallback.x / w, fallback.y / h);
        rt.sizeDelta = new Vector2(w * scale, h * scale);
    }

    struct Sparkle
    {
        public RectTransform rt;
        public Image image;
        public Vector2 velocity;
        public float life;
        public float size;
    }
}
