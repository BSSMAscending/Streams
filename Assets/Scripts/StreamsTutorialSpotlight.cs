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
    const float DimAlpha = 0.62f;
    const float HolePadding = 16f;
    const float SlotSpritePadBottomExtra = 32f;
    const float TmpHolePadding = 8f;
    const float ImageHolePadding = 10f;
    const float CoachFontSize = 42f;
    const float CoachGap = 12f;
    const float CoachMinSide = 80f;
    const float CoachMaxLineWidth = 420f;

    static StreamsTutorialSpotlight _instance;

    Canvas _canvas;
    RectTransform _root;
    RectTransform _dimRoot;
    RectTransform _catcher;
    RectTransform _coachRoot;
    TextMeshProUGUI _coach;
    Camera _worldCamera;
    bool _tapped;
    bool _dimRaycast;
    Coroutine _rebuild;

    readonly List<RectTransform> _holes = new List<RectTransform>();
    string _coachSource;

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
        if (gameObject.GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
        }

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
        _canvas.enabled = false;
    }

    public void Show(IList<RectTransform> holes, string message, bool blockClicks, Camera worldCamera)
    {
        if (_canvas == null)
            Build();

        _worldCamera = worldCamera;
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
        _tapped = false;
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

        if (_catcher != null)
            _catcher.gameObject.SetActive(false);

        if (_canvas != null)
            _canvas.enabled = false;
    }

    public IEnumerator WaitForTap()
    {
        yield return WaitForPointerUp();
        _tapped = false;
        while (!_tapped)
            yield return null;
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
        ClearChildren(_dimRoot);
        if (_dimRoot == null)
            return;

        Canvas.ForceUpdateCanvases();

        Rect canvasLocal = _dimRoot.rect;
        var dark = new List<Rect> { canvasLocal };
        var highlightHoles = new List<Rect>();
        SubtractTargets(_holes, canvasLocal, dark, highlightHoles);

        for (int i = 0; i < dark.Count; i++)
            CreateDimPanel(dark[i]);

        LayoutCoach(canvasLocal, highlightHoles);
    }

    void SubtractTargets(List<RectTransform> targets, Rect canvasLocal, List<Rect> dark, List<Rect> collected)
    {
        if (targets == null)
            return;

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null)
                continue;

            Rect hole = GetLocalRect(targets[i], out float padX, out float padTop, out float padBottom);
            hole = Pad(hole, padX, padTop, padBottom);
            hole = Intersect(canvasLocal, hole);
            if (hole.width < 1f || hole.height < 1f)
                continue;

            collected?.Add(hole);
            var next = new List<Rect>();
            for (int d = 0; d < dark.Count; d++)
                Subtract(dark[d], hole, next);
            dark.Clear();
            dark.AddRange(next);
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
        Vector2 focus = WeightedCenter(holes, highlight.center);
        bool highlightOnRight = focus.x >= canvasLocal.center.x;
        bool onRight = !highlightOnRight;

        float leftLimit = canvasLocal.xMin + CoachGap;
        float rightLimit = canvasLocal.xMax - CoachGap;
        float needed = Mathf.Min(CoachMaxLineWidth, canvasLocal.width * 0.5f - CoachGap);
        float textLeft;
        float textRight;
        if (onRight)
        {
            textLeft = Mathf.Clamp(highlight.xMax + CoachGap, leftLimit, rightLimit - needed);
            textRight = rightLimit;
        }
        else
        {
            textRight = Mathf.Clamp(highlight.xMin - CoachGap, leftLimit + needed, rightLimit);
            textLeft = leftLimit;
        }

        float maxWidth = Mathf.Max(CoachMinSide, textRight - textLeft);
        float wrapWidth = Mathf.Min(maxWidth, CoachMaxLineWidth);
        string wrapped = WrapToWidth(_coach, _coachSource, wrapWidth);
        _coach.text = wrapped;
        _coach.alignment = onRight ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;
        _coach.enableWordWrapping = true;

        Vector2 pref = _coach.GetPreferredValues(wrapped, wrapWidth, 0f);
        float boxW = Mathf.Clamp(pref.x + 28f, 56f, maxWidth);
        float boxH = Mathf.Max(pref.y + 20f, CoachFontSize * 1.2f);

        var rt = _coachRoot != null ? _coachRoot : _coach.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = onRight ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(boxW, boxH);
        rt.anchoredPosition = onRight
            ? new Vector2(textLeft, highlight.yMax)
            : new Vector2(textRight, highlight.yMax);

        _coach.gameObject.SetActive(true);
        if (_coachRoot != null)
        {
            _coachRoot.gameObject.SetActive(true);
            _coachRoot.SetAsLastSibling();
        }
        else
            _coach.transform.SetAsLastSibling();
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

    static Vector2 WeightedCenter(List<Rect> rects, Vector2 fallback)
    {
        if (rects == null || rects.Count == 0)
            return fallback;

        float ax = 0f;
        float ay = 0f;
        float area = 0f;
        for (int i = 0; i < rects.Count; i++)
        {
            float a = Mathf.Max(1f, rects[i].width * rects[i].height);
            ax += rects[i].center.x * a;
            ay += rects[i].center.y * a;
            area += a;
        }

        return area > 0f ? new Vector2(ax / area, ay / area) : fallback;
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

        var slot = target.GetComponent<StreamsUiSlot>();
        if (slot == null)
            slot = target.GetComponentInParent<StreamsUiSlot>();

        bool visual = slot != null && slot.TryGetVisualWorldCorners(corners, SlotSpritePadBottomExtra);

        if (!visual && TryGetButtonWorldCorners(target, corners))
        {
            visual = true;
            padX = ImageHolePadding;
            padTop = 0f;
            padBottom = 0f;
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

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static bool TryGetButtonWorldCorners(RectTransform target, Vector3[] corners)
    {
        var button = target.GetComponent<Button>();
        if (button == null)
            button = target.GetComponentInParent<Button>();
        if (button == null)
            return false;

        var rt = button.transform as RectTransform;
        if (rt == null)
            return false;

        var image = button.GetComponent<Image>();
        if (image != null)
            rt = image.rectTransform;

        Rect sr = rt.rect;
        float left = sr.xMin;
        float right = sr.xMax;
        float bottom = sr.yMin;
        float top = sr.yMax;

        if (image != null && image.sprite != null)
        {
            Rect tex = image.sprite.rect;
            if (tex.height > 1f)
            {
                Vector4 padding = DataUtility.GetPadding(image.sprite);
                float sy = sr.height / tex.height;
                bottom += padding.y * sy;
                top -= padding.w * sy;
                if (top <= bottom)
                {
                    bottom = sr.yMin;
                    top = sr.yMax;
                }
            }
        }

        corners[0] = rt.TransformPoint(new Vector3(left, bottom, 0f));
        corners[1] = rt.TransformPoint(new Vector3(left, top, 0f));
        corners[2] = rt.TransformPoint(new Vector3(right, top, 0f));
        corners[3] = rt.TransformPoint(new Vector3(right, bottom, 0f));
        return true;
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
        if (image == null || image.sprite == null)
            return false;

        var rt = image.rectTransform;
        Sprite sprite = image.sprite;
        Rect sr = rt.rect;
        Rect tex = sprite.rect;
        if (tex.width < 1f || tex.height < 1f)
            return false;

        Vector4 padding = DataUtility.GetPadding(sprite);
        float sx = sr.width / tex.width;
        float sy = sr.height / tex.height;
        float left = padding.x * sx;
        float bottom = padding.y * sy;
        float right = padding.z * sx;
        float top = padding.w * sy;
        if (left + right >= sr.width - 1f || bottom + top >= sr.height - 1f)
            return false;

        corners[0] = rt.TransformPoint(new Vector3(sr.xMin + left, sr.yMin + bottom, 0f));
        corners[1] = rt.TransformPoint(new Vector3(sr.xMin + left, sr.yMax - top, 0f));
        corners[2] = rt.TransformPoint(new Vector3(sr.xMax - right, sr.yMax - top, 0f));
        corners[3] = rt.TransformPoint(new Vector3(sr.xMax - right, sr.yMin + bottom, 0f));
        return true;
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
            if (child != null && child.name == "CoachText")
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
}
