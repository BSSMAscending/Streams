using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 격자 원이 왼쪽 위부터 커지며 화면을 덮은 뒤 씬을 넘깁니다.
/// </summary>
public class StreamsSceneTransition : MonoBehaviour
{
    const int Columns = 12;
    const float GrowDuration = 0.45f;
    const float ShrinkDuration = 0.35f;
    const float Stagger = 0.05f;
    const float CoverScale = 1.55f;
    const int CircleTextureSize = 128;
    static readonly Color CircleColor = new Color(0.49f, 0.80f, 0.94f, 1f);

    static StreamsSceneTransition _instance;
    static Sprite _circleSprite;

    RectTransform[] _circles = System.Array.Empty<RectTransform>();
    CanvasGroup _group;
    bool _busy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        bool first = _instance == null;
        EnsureInstance();
        if (first)
            _instance.PlayOpeningReveal();
    }

    static void EnsureInstance()
    {
        if (_instance != null)
            return;

        var go = new GameObject(nameof(StreamsSceneTransition));
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<StreamsSceneTransition>();
        _instance.BuildOverlay();
    }

    public static void Load(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("StreamsSceneTransition: sceneName이 비어 있습니다.");
            return;
        }

        EnsureInstance();
        _instance.StartCoroutine(_instance.Transition(sceneName));
    }

    void PlayOpeningReveal()
    {
        RebuildGrid();
        var covered = new Vector3(CoverScale, CoverScale, 1f);
        for (int i = 0; i < _circles.Length; i++)
        {
            if (_circles[i] != null)
                _circles[i].localScale = covered;
        }

        StartCoroutine(RevealRoutine());
    }

    IEnumerator RevealRoutine()
    {
        if (_busy)
            yield break;

        _busy = true;
        yield return null;
        yield return AnimateCircles(CoverScale, 0f, ShrinkDuration, EaseInCubic);
        _busy = false;
    }

    void BuildOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 1f;
        _group.blocksRaycasts = false;
        _group.interactable = false;
    }

    IEnumerator Transition(string sceneName)
    {
        if (_busy)
            yield break;

        _busy = true;
        _group.blocksRaycasts = true;

        RebuildGrid();
        yield return AnimateCircles(0f, CoverScale, GrowDuration, EaseOutCubic);

        var op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            Debug.LogError($"StreamsSceneTransition: '{sceneName}' 씬을 로드할 수 없습니다.");
            yield return AnimateCircles(CoverScale, 0f, ShrinkDuration, EaseInCubic);
            _group.blocksRaycasts = false;
            _busy = false;
            yield break;
        }

        while (!op.isDone)
            yield return null;

        yield return null;
        yield return AnimateCircles(CoverScale, 0f, ShrinkDuration, EaseInCubic);

        _group.blocksRaycasts = false;
        _busy = false;
    }

    void RebuildGrid()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        float width = Screen.width;
        float height = Screen.height;
        float cell = width / Columns;
        int rows = Mathf.Max(1, Mathf.CeilToInt(height / cell));
        int count = Columns * rows;
        _circles = new RectTransform[count];

        Sprite sprite = CircleSprite();

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                var go = new GameObject($"Circle_{col}_{row}");
                var rect = go.AddComponent<RectTransform>();
                rect.SetParent(transform, false);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(cell, cell);
                rect.anchoredPosition = new Vector2((col + 0.5f) * cell, -(row + 0.5f) * cell);
                rect.localScale = Vector3.zero;

                var image = go.AddComponent<Image>();
                image.sprite = sprite;
                image.color = CircleColor;
                image.raycastTarget = false;
                image.preserveAspect = true;

                _circles[row * Columns + col] = rect;
            }
        }
    }

    IEnumerator AnimateCircles(float from, float to, float duration, System.Func<float, float> ease)
    {
        int cols = Columns;
        int rows = Mathf.Max(1, _circles.Length / cols);
        float maxDelay = (cols - 1 + rows - 1) * Stagger;
        float total = maxDelay + duration;
        float elapsed = 0f;

        while (elapsed < total)
        {
            elapsed += Time.unscaledDeltaTime;
            for (int i = 0; i < _circles.Length; i++)
            {
                var rect = _circles[i];
                if (rect == null)
                    continue;

                int col = i % cols;
                int row = i / cols;
                float local = (elapsed - (col + row) * Stagger) / duration;
                float k = ease(Mathf.Clamp01(local));
                float scale = Mathf.LerpUnclamped(from, to, k);
                rect.localScale = new Vector3(scale, scale, 1f);
            }

            yield return null;
        }

        var end = new Vector3(to, to, 1f);
        for (int i = 0; i < _circles.Length; i++)
        {
            if (_circles[i] != null)
                _circles[i].localScale = end;
        }
    }

    static Sprite CircleSprite()
    {
        if (_circleSprite != null)
            return _circleSprite;

        int size = CircleTextureSize;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.name = "StreamsTransitionCircle";

        float center = (size - 1) * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(center - dist + 0.5f);
                byte a = (byte)(alpha * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        _circleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        _circleSprite.name = "StreamsTransitionCircle";
        return _circleSprite;
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

    static float EaseInCubic(float t) => t * t * t;
}
