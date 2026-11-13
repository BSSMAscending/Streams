using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 끝내기 확인. 결과창과 같은 흰 판에 '정말 그만할까요?'와 두 버튼을 띄웁니다.
/// </summary>
[DisallowMultipleComponent]
public class StreamsQuitConfirmPopup : MonoBehaviour
{
    const int SortingOrder = 200;
    const float DimAlpha = 0.78431374f;
    const float PopDuration = 0.4f;
    const float PopStartMul = 0.22f;
    const string FontPath = "Assets/Fonts/GmarketSansTTFBold SDF.asset";

    // 결과 보기(NextButton)와 같은 파란색, 끝내기(EndButton)와 같은 회색.
    static readonly Color ContinueColor = new Color(0f, 0.5686275f, 0.8156863f, 1f);
    static readonly Color QuitColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    RectTransform _panel;
    Vector3 _panelRest = new Vector3(0.42f, 0.42f, 0.42f);
    Action _onQuit;
    bool _built;
    Coroutine _pop;

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

        if (FindExisting() != null)
            return;

        var go = new GameObject(nameof(StreamsQuitConfirmPopup));
        go.SetActive(false);
        go.AddComponent<StreamsQuitConfirmPopup>();
    }

    public static void Show(Action onQuit)
    {
        var popup = FindExisting();
        if (popup == null)
        {
            var go = new GameObject(nameof(StreamsQuitConfirmPopup));
            go.SetActive(false);
            popup = go.AddComponent<StreamsQuitConfirmPopup>();
        }

        popup.Open(onQuit);
    }

    static StreamsQuitConfirmPopup FindExisting()
    {
        var found = FindObjectsByType<StreamsQuitConfirmPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                return found[i];
        }

        return null;
    }

    void Awake()
    {
        if (gameObject.activeInHierarchy)
            Build();
    }

    void OnEnable()
    {
        if (!_built)
            Build();
        ClearAspectFill();
    }

    public void Open(Action onQuit)
    {
        _onQuit = onQuit;
        gameObject.SetActive(true);
        if (_pop != null)
            StopCoroutine(_pop);
        _pop = StartCoroutine(Pop());
    }

    public void Close()
    {
        _onQuit = null;
        if (_pop != null)
        {
            StopCoroutine(_pop);
            _pop = null;
        }

        if (_panel != null)
            _panel.localScale = _panelRest;

        gameObject.SetActive(false);
    }

    void OnContinue() => Close();

    void OnQuitPressed()
    {
        Action quit = _onQuit;
        Close();
        quit?.Invoke();
    }

    IEnumerator Pop()
    {
        if (_panel == null)
            yield break;

        Vector3 from = _panelRest * PopStartMul;
        _panel.localScale = from;

        float t = 0f;
        while (t < PopDuration)
        {
            t += Time.deltaTime;
            float u = EaseOutBack(Mathf.Clamp01(t / PopDuration));
            _panel.localScale = Vector3.LerpUnclamped(from, _panelRest, u);
            yield return null;
        }

        _panel.localScale = _panelRest;
        _pop = null;
    }

    void Build()
    {
        if (_built)
            return;
        _built = true;

        var canvas = GetComponent<Canvas>();
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

        var dim = CreateImage("Dim", transform, new Color(0f, 0f, 0f, DimAlpha));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        ApplyResultDim(dim);

        var panelImage = CreateImage("Background", transform, Color.white);
        panelImage.sprite = FindSprite("Panel2");
        panelImage.type = Image.Type.Sliced;
        panelImage.pixelsPerUnitMultiplier = 1f;
        panelImage.raycastTarget = true;

        _panel = panelImage.rectTransform;
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(2400f, 1400f);
        _panel.anchoredPosition = Vector2.zero;
        _panelRest = new Vector3(0.42f, 0.42f, 0.42f);
        _panel.localScale = _panelRest;

        var message = CreateText("Message", _panel, "정말 그만할까요?", 150f, Color.black);
        var messageRt = message.rectTransform;
        messageRt.anchorMin = messageRt.anchorMax = new Vector2(0.5f, 0.5f);
        messageRt.pivot = new Vector2(0.5f, 0.5f);
        messageRt.sizeDelta = new Vector2(1800f, 280f);
        messageRt.anchoredPosition = new Vector2(0f, 160f);
        message.alignment = TextAlignmentOptions.Center;
        message.textWrappingMode = TextWrappingModes.NoWrap;
        message.overflowMode = TextOverflowModes.Overflow;

        CreateChoice("QuitButton", "그만하기", QuitColor, new Vector2(-440f, -380f), OnQuitPressed);
        CreateChoice("ContinueButton", "계속하기", ContinueColor, new Vector2(440f, -380f), OnContinue);

        if (GetComponent<StreamsAspectFit>() == null)
            gameObject.AddComponent<StreamsAspectFit>();
        ClearAspectFill();
    }

    void CreateChoice(string name, string label, Color color, Vector2 position, UnityEngine.Events.UnityAction onClick)
    {
        var image = CreateImage(name, _panel, color);
        image.sprite = FindSprite("Button2");
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
        image.raycastTarget = true;

        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1024f, 512f);
        rt.anchoredPosition = position;
        rt.localScale = new Vector3(0.75f, 0.75f, 0.75f);

        var button = image.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);

        var text = CreateText("Text (TMP)", image.transform, label, 85f, Color.white);
        Stretch(text.rectTransform);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        image.gameObject.AddComponent<UiButtonPressScale>();
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
        scaler.referenceResolution = source != null
            ? source.referenceResolution
            : new Vector2(StreamsAspectFit.DesignWidth, StreamsAspectFit.DesignHeight);
        scaler.screenMatchMode = source != null
            ? source.screenMatchMode
            : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = source != null ? source.matchWidthOrHeight : 0f;
        scaler.referencePixelsPerUnit = source != null ? source.referencePixelsPerUnit : 100f;
    }

    void ClearAspectFill()
    {
        Transform fill = transform.Find("AspectBars/Fill");
        if (fill == null)
            return;

        var image = fill.GetComponent<Image>();
        if (image == null)
            return;

        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
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

    static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.richText = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
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

    static void ApplyResultDim(Image dim)
    {
        Image source = FindResultDim();
        if (source != null && source.sprite != null)
        {
            dim.sprite = source.sprite;
            dim.type = source.type;
            dim.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            return;
        }

        dim.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        dim.type = Image.Type.Sliced;
        dim.pixelsPerUnitMultiplier = 1f;
    }

    static Image FindResultDim()
    {
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.name != "ResultCanvas")
                continue;

            var images = canvas.GetComponentsInChildren<Image>(true);
            for (int j = 0; j < images.Length; j++)
            {
                if (images[j] != null && images[j].name == "Panel")
                    return images[j];
            }
        }

        return null;
    }

    static Sprite FindSprite(string spriteName)
    {
        var images = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || image.sprite == null)
                continue;
            if (image.sprite.name == spriteName)
                return image.sprite;
        }

#if UNITY_EDITOR
        var fromAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Sprites/{spriteName}.png");
        if (fromAsset != null)
            return fromAsset;
#endif
        return Resources.Load<Sprite>(spriteName);
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

    static float EaseOutBack(float u)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float inv = u - 1f;
        return 1f + c3 * inv * inv * inv + c1 * inv * inv;
    }
}
