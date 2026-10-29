using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 창 크기가 바뀌어도 게임 화면은 16:9로 유지합니다. 밖은 검은 여백입니다.
/// </summary>
[DefaultExecutionOrder(-200)]
public class StreamsAspectFit : MonoBehaviour
{
    public const float DesignWidth = 1920f;
    public const float DesignHeight = 1080f;
    public static float DesignAspect => DesignWidth / DesignHeight;

    RectTransform _frame;
    RectTransform _barLeft;
    RectTransform _barRight;
    RectTransform _barBottom;
    RectTransform _barTop;
    bool _clearCenter;
    bool _built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ConfigureWebGl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            pipeline.renderScale = 1f;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid())
            return;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;

            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                TryAttach(canvas);
        }

        ApplyCameras();
    }

    static void TryAttach(Canvas canvas)
    {
        if (canvas == null || !canvas.isRootCanvas)
            return;
        if (canvas.GetComponent<StreamsAspectFit>() != null)
            return;
        if (canvas.name == "StreamsTutorialSpotlight" || canvas.name == "StreamsSceneTransition")
            return;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return;

        canvas.gameObject.AddComponent<StreamsAspectFit>();
    }

    public static Rect Fit(Rect area)
    {
        if (area.width < 1f || area.height < 1f)
            return area;

        float window = area.width / area.height;
        float width;
        float height;
        if (window > DesignAspect)
        {
            height = area.height;
            width = height * DesignAspect;
        }
        else
        {
            width = area.width;
            height = width / DesignAspect;
        }

        return new Rect(
            area.center.x - width * 0.5f,
            area.center.y - height * 0.5f,
            width,
            height);
    }

    void Awake()
    {
        Build();
        Apply();
    }

    void OnEnable() => Apply();

    void OnRectTransformDimensionsChange() => Apply();

    void LateUpdate()
    {
        Apply();
        ApplyCameras();
    }

    void Build()
    {
        if (_built)
            return;
        _built = true;

        var root = transform as RectTransform;
        if (root == null)
            return;

        var children = new List<RectTransform>();
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i) is RectTransform child && child.name != "AspectBars")
                children.Add(child);
        }

        // 결과 화면 딤은 알파가 있습니다. 캔버스 전체를 검은 판으로 덮으면
        // 그 위에 반투명이 섞여 보드가 안 보이고 불투명해집니다.
        _clearCenter = gameObject.name == "ResultCanvas";

        var barsGo = new GameObject("AspectBars", typeof(RectTransform));
        var bars = barsGo.GetComponent<RectTransform>();
        bars.SetParent(root, false);
        Stretch(bars);
        if (_clearCenter)
        {
            _barLeft = CreateBar("BarLeft", bars);
            _barRight = CreateBar("BarRight", bars);
            _barBottom = CreateBar("BarBottom", bars);
            _barTop = CreateBar("BarTop", bars);
        }
        else
        {
            var fill = CreateBar("Fill", bars);
            Stretch(fill);
        }

        var frameGo = new GameObject("AspectFrame", typeof(RectTransform));
        _frame = frameGo.GetComponent<RectTransform>();
        _frame.SetParent(root, false);
        _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f);
        _frame.pivot = new Vector2(0.5f, 0.5f);
        _frame.sizeDelta = new Vector2(DesignWidth, DesignHeight);
        _frame.anchoredPosition = Vector2.zero;
        _frame.localScale = Vector3.one;

        for (int i = 0; i < children.Count; i++)
            ReparentKeepLayout(children[i], _frame);

        bars.SetAsFirstSibling();
        _frame.SetAsLastSibling();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    static void ReparentKeepLayout(RectTransform rt, RectTransform parent)
    {
        Vector2 anchorMin = rt.anchorMin;
        Vector2 anchorMax = rt.anchorMax;
        Vector2 pivot = rt.pivot;
        Vector2 anchored = rt.anchoredPosition;
        Vector2 size = rt.sizeDelta;
        Vector2 offsetMin = rt.offsetMin;
        Vector2 offsetMax = rt.offsetMax;
        Vector3 scale = rt.localScale;
        Quaternion rotation = rt.localRotation;
        bool stretch = (anchorMax - anchorMin).sqrMagnitude > 0.0001f;

        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.localScale = scale;
        rt.localRotation = rotation;
        if (stretch)
        {
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
        else
        {
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
        }
    }

    void Apply()
    {
        if (_frame == null)
            return;

        var root = transform as RectTransform;
        if (root == null)
            return;

        Rect fitted = Fit(root.rect);
        if (fitted.width < 2f || fitted.height < 2f)
            return;

        float scale = fitted.width / DesignWidth;
        var next = new Vector3(scale, scale, 1f);
        if (_frame.localScale != next)
            _frame.localScale = next;

        if (_clearCenter)
            LayoutLetterbox(root.rect, fitted);
    }

    static RectTransform CreateBar(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = true;
        return rt;
    }

    void LayoutLetterbox(Rect full, Rect fitted)
    {
        float width = Mathf.Max(1f, full.width);
        float height = Mathf.Max(1f, full.height);
        float x0 = Mathf.Clamp01((fitted.xMin - full.xMin) / width);
        float x1 = Mathf.Clamp01((fitted.xMax - full.xMin) / width);
        float y0 = Mathf.Clamp01((fitted.yMin - full.yMin) / height);
        float y1 = Mathf.Clamp01((fitted.yMax - full.yMin) / height);

        PlaceBar(_barLeft, new Vector2(0f, 0f), new Vector2(x0, 1f));
        PlaceBar(_barRight, new Vector2(x1, 0f), new Vector2(1f, 1f));
        PlaceBar(_barBottom, new Vector2(x0, 0f), new Vector2(x1, y0));
        PlaceBar(_barTop, new Vector2(x0, y1), new Vector2(x1, 1f));
    }

    static void PlaceBar(RectTransform bar, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (bar == null)
            return;

        bar.anchorMin = anchorMin;
        bar.anchorMax = anchorMax;
        bar.offsetMin = Vector2.zero;
        bar.offsetMax = Vector2.zero;
        bar.pivot = new Vector2(0.5f, 0.5f);
        bool visible = anchorMax.x - anchorMin.x > 0.002f && anchorMax.y - anchorMin.y > 0.002f;
        if (bar.gameObject.activeSelf != visible)
            bar.gameObject.SetActive(visible);
    }

    static void ApplyCameras()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL은 카메라 viewport를 16:9로 줄이면 화면 전체가 검게 나옵니다.
        // 입력은 그대로라 버튼은 눌리고, 나중에 그려지는 전환 효과만 보입니다.
        var full = new Rect(0f, 0f, 1f, 1f);
        foreach (var camera in Camera.allCameras)
        {
            if (camera == null || camera.targetTexture != null)
                continue;
            if (camera.rect != full)
                camera.rect = full;

            Color background = camera.backgroundColor;
            if (background.a < 1f)
            {
                background.a = 1f;
                camera.backgroundColor = background;
            }
        }
        return;
#endif

        if (Screen.width < 2 || Screen.height < 2)
            return;

        Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
        Rect fitted = Fit(screen);
        var viewport = new Rect(
            fitted.xMin / screen.width,
            fitted.yMin / screen.height,
            fitted.width / screen.width,
            fitted.height / screen.height);

        foreach (var camera in Camera.allCameras)
        {
            if (camera == null || camera.targetTexture != null)
                continue;
            if (camera.rect != viewport)
                camera.rect = viewport;
        }
    }
}
