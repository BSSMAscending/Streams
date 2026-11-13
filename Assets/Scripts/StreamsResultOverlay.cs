using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ResultCanvas: Background가 작았다가 원래 크기로 팝인된 뒤, 막대가 올라가며 점수가 따라갑니다.
/// AI 막대에는 학습 횟수 기준 점선이 같은 눈금으로 함께 올라갑니다.
/// </summary>
public class StreamsResultOverlay : MonoBehaviour
{
    const string BackgroundName = "Background";
    const string PlayerBarName = "Bar_Player";
    const string AiBarName = "Bar_AI";
    const string PlayerScoreName = "PlayerScore";
    const string AiScoreName = "AIScore";
    const string AiNameObjectName = "AIName";
    const string DottedLineSpritePath = "Assets/Sprites/DottedLine.png";
    const string DottedLineRefResourceName = "DottedLineRef";

    const float BackgroundStartMul = 0.22f;
    const float BackgroundDuration = 0.4f;
    const float BarDuration = 1.1f;
    const float MinBarHeight = 4f;
    const float MinGraphScore = 43f;
    const int HideReferenceScore = 150;
    const float ReferenceLabelFontSize = 36f;
    const float ReferenceLabelGap = 16f;
    const float ReferenceLabelWidth = 680f;
    const float ReferenceLabelPadding = 8f;
    const float ReferenceCharInterval = 0.02f;

    static readonly ReferenceSpec[] ReferenceSpecs =
    {
        new ReferenceSpec("1번 학습한 AI", 18f),
        new ReferenceSpec("1,000번 학습한 AI", 40f),
        new ReferenceSpec("1,000,000번 학습한 AI", 43f),
    };

    RectTransform _background;
    Vector3 _backgroundRest = Vector3.one;
    bool _backgroundRestCaptured;

    BarAnim _player;
    BarAnim _ai;
    Coroutine _play;
    float _graphScale = MinGraphScore;
    bool _showReferences = true;
    Sprite _dottedSprite;
    bool _dottedSpriteMissing;
    ReferenceMark[] _references;

    public bool IsPlaying => _play != null;
    public float PlayerBarRestHeight => _player.restHeight;
    public float AiBarRestHeight => _ai.restHeight;

    public void Play()
    {
        if (_play != null)
            StopCoroutine(_play);
        _play = StartCoroutine(PlayRoutine());
    }

    IEnumerator PlayRoutine()
    {
        Resolve();
        PrepareBars();
        yield return AnimateBackground();
        yield return AnimateBars();
        _play = null;
    }

    void Resolve()
    {
        if (_background == null)
        {
            Transform found = transform.Find(BackgroundName);
            if (found == null)
                found = FindNamedDeep(transform, BackgroundName);
            _background = found as RectTransform;
        }

        if (_background != null && !_backgroundRestCaptured)
        {
            _backgroundRest = _background.localScale;
            if (_backgroundRest.sqrMagnitude < 0.0001f)
                _backgroundRest = Vector3.one;
            _backgroundRestCaptured = true;
        }

        if (_player.rt == null && _player.tmp == null)
            _player = BindBar(PlayerBarName, PlayerScoreName);
        if (_ai.rt == null && _ai.tmp == null)
            _ai = BindBar(AiBarName, AiScoreName);
    }

    BarAnim BindBar(string barName, string scoreName)
    {
        var anim = new BarAnim();
        Transform found = FindNamedDeep(transform, barName);
        anim.rt = found as RectTransform;
        if (anim.rt != null)
        {
            anim.image = anim.rt.GetComponent<Image>();
            anim.useFill = anim.image != null && anim.image.type == Image.Type.Filled;
            anim.restHeight = Mathf.Abs(anim.rt.sizeDelta.y);
            if (anim.restHeight < 1f)
                anim.restHeight = anim.rt.rect.height;

            anim.tmp = FindScoreTmp(anim.rt);
            anim.text = anim.tmp == null ? FindScoreText(anim.rt) : null;
        }

        if (anim.tmp == null && anim.text == null)
        {
            Transform score = FindNamedDeep(transform, scoreName);
            if (score != null)
            {
                anim.tmp = score.GetComponent<TextMeshProUGUI>();
                if (anim.tmp == null)
                    anim.text = score.GetComponent<Text>();
            }
        }

        return anim;
    }

    void PrepareBars()
    {
        int playerScore = StreamsGameResults.PlayerScore;
        int aiScore = StreamsGameResults.OpponentScore;
        _graphScale = Mathf.Max(Mathf.Max(playerScore, aiScore), MinGraphScore);

        _player.score = playerScore;
        _ai.score = aiScore;
        _player.targetHeight = _player.restHeight * (playerScore / _graphScale);
        _ai.targetHeight = _ai.restHeight * (aiScore / _graphScale);
        _player.targetFill = playerScore / _graphScale;
        _ai.targetFill = aiScore / _graphScale;
        _showReferences = playerScore < HideReferenceScore && aiScore < HideReferenceScore;

        ApplyBar(_player, 0f, 0);
        ApplyBar(_ai, 0f, 0);
        if (_showReferences)
        {
            LayoutReferenceLines();
            ApplyReferenceLines(0f);
            SetReferenceLabelVisibleCharacters(0);
        }
        else
            SetReferenceLinesActive(false);
    }

    IEnumerator AnimateBackground()
    {
        if (_background == null)
            yield break;

        Vector3 from = _backgroundRest * BackgroundStartMul;
        _background.localScale = from;

        float t = 0f;
        while (t < BackgroundDuration)
        {
            t += Time.deltaTime;
            float u = EaseOutBack(Mathf.Clamp01(t / BackgroundDuration));
            _background.localScale = Vector3.LerpUnclamped(from, _backgroundRest, u);
            yield return null;
        }

        _background.localScale = _backgroundRest;
    }

    IEnumerator AnimateBars()
    {
        bool bars = _player.rt != null || _ai.rt != null || _player.tmp != null || _ai.tmp != null;
        int maxLen = ReferenceLabelCharacterCount();
        SetReferenceLabelVisibleCharacters(0);

        float t = 0f;
        int shown = 0;
        while ((bars && t < BarDuration) || shown < maxLen)
        {
            t += Time.deltaTime;
            if (bars)
            {
                float u = EaseOutQuint(Mathf.Clamp01(t / BarDuration));
                ApplyBar(_player, u, Mathf.RoundToInt(_player.score * u));
                ApplyBar(_ai, u, Mathf.RoundToInt(_ai.score * u));
                ApplyReferenceLines(u);
            }

            if (maxLen > 0)
            {
                int next = Mathf.Min(maxLen, Mathf.FloorToInt(t / ReferenceCharInterval));
                if (next != shown)
                {
                    shown = next;
                    SetReferenceLabelVisibleCharacters(shown);
                }
            }

            yield return null;
        }

        if (bars)
        {
            ApplyBar(_player, 1f, _player.score);
            ApplyBar(_ai, 1f, _ai.score);
            ApplyReferenceLines(1f);
        }

        if (maxLen > 0)
            SetReferenceLabelVisibleCharacters(99999);
    }

    void ApplyBar(BarAnim bar, float u, int shownScore)
    {
        if (bar.rt != null)
        {
            if (bar.useFill && bar.image != null)
                bar.image.fillAmount = bar.targetFill * u;
            else
            {
                Vector2 size = bar.rt.sizeDelta;
                size.y = Mathf.Lerp(0f, Mathf.Max(MinBarHeight, bar.targetHeight), u);
                bar.rt.sizeDelta = size;
            }
        }

        string label = shownScore.ToString() + "점";
        if (bar.tmp != null)
            bar.tmp.text = label;
        else if (bar.text != null)
            bar.text.text = label;
    }

    void LayoutReferenceLines()
    {
        if (!_showReferences)
        {
            SetReferenceLinesActive(false);
            return;
        }

        EnsureReferenceLines();
        SetReferenceLinesActive(true);
        if (_references == null || _ai.rt == null)
            return;

        float width = BarWidth(_ai.rt);
        float height = ReferenceLineHeight(width);
        for (int i = 0; i < _references.Length; i++)
        {
            ReferenceMark mark = _references[i];
            mark.rise = _ai.restHeight * (mark.score / _graphScale);
            if (mark.root == null)
                continue;

            mark.root.sizeDelta = new Vector2(width, height);
        }

        SeparateReferenceLabels();
    }

    void EnsureReferenceLines()
    {
        if (_references != null || _ai.rt == null)
            return;

        Sprite sprite = LoadDottedLineSprite();
        if (sprite == null)
        {
            if (!_dottedSpriteMissing)
            {
                _dottedSpriteMissing = true;
                Debug.LogWarning("StreamsResultOverlay: DottedLine 스프라이트를 찾지 못했습니다.");
            }

            return;
        }

        ResolveReferenceFont(out TMP_FontAsset font, out Material fontMaterial, out Color color);
        _references = new ReferenceMark[ReferenceSpecs.Length];
        for (int i = 0; i < ReferenceSpecs.Length; i++)
        {
            ReferenceSpec spec = ReferenceSpecs[i];
            _references[i] = CreateReferenceLine(spec, sprite, font, fontMaterial, color);
        }
    }

    ReferenceMark CreateReferenceLine(
        ReferenceSpec spec,
        Sprite sprite,
        TMP_FontAsset font,
        Material fontMaterial,
        Color color)
    {
        var mark = new ReferenceMark { score = spec.score };
        var rootGo = new GameObject(
            "ReferenceLine_" + spec.score.ToString("0.0", CultureInfo.InvariantCulture),
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        rootGo.transform.SetParent(_ai.rt.parent, false);

        var root = rootGo.GetComponent<RectTransform>();
        root.anchorMin = _ai.rt.anchorMin;
        root.anchorMax = _ai.rt.anchorMax;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.localScale = Vector3.one;
        root.localRotation = Quaternion.identity;

        var image = rootGo.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        image.raycastTarget = false;

        var labelGo = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(root, false);

        var label = labelGo.GetComponent<RectTransform>();
        label.anchorMin = new Vector2(1f, 0.5f);
        label.anchorMax = new Vector2(1f, 0.5f);
        label.pivot = new Vector2(0f, 0.5f);
        label.anchoredPosition = new Vector2(ReferenceLabelGap, 0f);
        label.sizeDelta = new Vector2(ReferenceLabelWidth, ReferenceLabelFontSize * 3f);
        label.localScale = Vector3.one;
        label.localRotation = Quaternion.identity;

        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        mark.fullText = spec.title + "\n평균 " + spec.score.ToString("0.0", CultureInfo.InvariantCulture) + "점";
        tmp.text = mark.fullText;
        tmp.fontSize = ReferenceLabelFontSize;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.lineSpacing = 0f;
        tmp.useMaxVisibleDescender = false;
        tmp.maxVisibleCharacters = 0;
        if (font != null)
        {
            tmp.font = font;
            tmp.fontSharedMaterial = fontMaterial != null ? fontMaterial : font.material;
        }

        tmp.maxVisibleCharacters = 99999;
        tmp.ForceMeshUpdate();
        mark.labelHeight = Mathf.Max(tmp.preferredHeight, ReferenceLabelFontSize * 2f);
        tmp.maxVisibleCharacters = 0;
        mark.tmp = tmp;
        mark.root = root;
        mark.label = label;
        root.SetSiblingIndex(_ai.rt.GetSiblingIndex() + 1);
        return mark;
    }

    void SeparateReferenceLabels()
    {
        if (_references == null || _references.Length < 2)
            return;

        var order = new int[_references.Length];
        var nudge = new float[_references.Length];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;

        System.Array.Sort(order, (a, b) => _references[a].rise.CompareTo(_references[b].rise));

        for (int pass = 0; pass < order.Length; pass++)
        {
            for (int k = 0; k < order.Length - 1; k++)
            {
                int lower = order[k];
                int upper = order[k + 1];
                float gap = (_references[upper].rise + nudge[upper]) - (_references[lower].rise + nudge[lower]);
                float need = (_references[lower].labelHeight + _references[upper].labelHeight) * 0.5f + ReferenceLabelPadding;
                if (gap >= need)
                    continue;

                float push = (need - gap) * 0.5f;
                nudge[lower] -= push;
                nudge[upper] += push;
            }
        }

        for (int i = 0; i < _references.Length; i++)
            _references[i].labelNudge = nudge[i];
    }

    void ApplyReferenceLines(float u)
    {
        if (!_showReferences || _references == null || _ai.rt == null)
            return;

        float bottom = _ai.rt.anchoredPosition.y - _ai.rt.pivot.y * _ai.restHeight;
        float x = _ai.rt.anchoredPosition.x + (0.5f - _ai.rt.pivot.x) * BarWidth(_ai.rt);
        for (int i = 0; i < _references.Length; i++)
        {
            ReferenceMark mark = _references[i];
            if (mark.root == null)
                continue;

            Vector2 pos = mark.root.anchoredPosition;
            pos.x = x;
            pos.y = bottom + mark.rise * u;
            mark.root.anchoredPosition = pos;

            if (mark.label == null)
                continue;

            Vector2 labelPos = mark.label.anchoredPosition;
            labelPos.x = ReferenceLabelGap;
            labelPos.y = mark.labelNudge * u;
            mark.label.anchoredPosition = labelPos;
        }
    }

    void SetReferenceLinesActive(bool active)
    {
        if (_references == null)
            return;

        for (int i = 0; i < _references.Length; i++)
        {
            RectTransform root = _references[i].root;
            if (root != null)
                root.gameObject.SetActive(active);
        }
    }

    int ReferenceLabelCharacterCount()
    {
        if (!_showReferences || _references == null)
            return 0;

        int maxLen = 0;
        for (int i = 0; i < _references.Length; i++)
        {
            string text = _references[i].fullText;
            if (text != null && text.Length > maxLen)
                maxLen = text.Length;
        }

        return maxLen;
    }

    void SetReferenceLabelVisibleCharacters(int count)
    {
        if (_references == null)
            return;

        for (int i = 0; i < _references.Length; i++)
        {
            TextMeshProUGUI tmp = _references[i].tmp;
            if (tmp != null)
                tmp.maxVisibleCharacters = count;
        }
    }

    float ReferenceLineHeight(float width)
    {
        if (_dottedSprite == null || _dottedSprite.rect.width < 1f)
            return width;

        return width * (_dottedSprite.rect.height / _dottedSprite.rect.width);
    }

    static float BarWidth(RectTransform bar)
    {
        float width = Mathf.Abs(bar.sizeDelta.x);
        if (width < 1f)
            width = Mathf.Abs(bar.rect.width);
        return width;
    }

    void ResolveReferenceFont(out TMP_FontAsset font, out Material fontMaterial, out Color color)
    {
        font = null;
        fontMaterial = null;
        color = Color.black;

        Transform nameObject = FindNamedDeep(transform, AiNameObjectName);
        var nameLabel = nameObject != null ? nameObject.GetComponent<TextMeshProUGUI>() : null;
        if (nameLabel == null)
            return;

        font = nameLabel.font;
        fontMaterial = nameLabel.fontSharedMaterial;
        color = nameLabel.color;
    }

    Sprite LoadDottedLineSprite()
    {
        if (_dottedSprite != null)
            return _dottedSprite;

#if UNITY_EDITOR
        _dottedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(DottedLineSpritePath);
        if (_dottedSprite != null)
            return _dottedSprite;
#endif
        var holder = Resources.Load<GameObject>(DottedLineRefResourceName);
        if (holder != null)
        {
            var image = holder.GetComponent<Image>();
            if (image != null && image.sprite != null)
                _dottedSprite = image.sprite;
        }

        if (_dottedSprite == null)
            _dottedSprite = Resources.Load<Sprite>("DottedLine");

        return _dottedSprite;
    }

    static TextMeshProUGUI FindScoreTmp(Transform bar)
    {
        foreach (var tmp in bar.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (tmp != null && tmp.name != "WinnerText")
                return tmp;
        }

        return null;
    }

    static Text FindScoreText(Transform bar)
    {
        foreach (var text in bar.GetComponentsInChildren<Text>(true))
        {
            if (text != null && text.name != "AxisLabel" && text.name != "WinnerText")
                return text;
        }

        return null;
    }

    static Transform FindNamedDeep(Transform root, string objectName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == objectName)
                return child;
            Transform nested = FindNamedDeep(child, objectName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    static float EaseOutBack(float u)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float inv = u - 1f;
        return 1f + c3 * inv * inv * inv + c1 * inv * inv;
    }

    static float EaseOutQuint(float u)
    {
        float inv = 1f - u;
        return 1f - inv * inv * inv * inv * inv;
    }

    struct BarAnim
    {
        public RectTransform rt;
        public Image image;
        public bool useFill;
        public float restHeight;
        public float targetHeight;
        public float targetFill;
        public int score;
        public TextMeshProUGUI tmp;
        public Text text;
    }

    readonly struct ReferenceSpec
    {
        public readonly string title;
        public readonly float score;

        public ReferenceSpec(string title, float score)
        {
            this.title = title;
            this.score = score;
        }
    }

    sealed class ReferenceMark
    {
        public float score;
        public float rise;
        public float labelNudge;
        public float labelHeight;
        public string fullText;
        public TextMeshProUGUI tmp;
        public RectTransform root;
        public RectTransform label;
    }
}
