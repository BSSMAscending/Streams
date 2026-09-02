using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ResultCanvas: Background가 작았다가 원래 크기로 팝인된 뒤, 막대가 올라가며 점수가 따라갑니다.
/// </summary>
public class StreamsResultOverlay : MonoBehaviour
{
    const string BackgroundName = "Background";
    const string PlayerBarName = "Bar_Player";
    const string AiBarName = "Bar_AI";
    const string PlayerScoreName = "PlayerScore";
    const string AiScoreName = "AIScore";

    const float BackgroundStartMul = 0.22f;
    const float BackgroundDuration = 0.4f;
    const float BarDuration = 1.1f;
    const float MinBarHeight = 4f;

    RectTransform _background;
    Vector3 _backgroundRest = Vector3.one;
    bool _backgroundRestCaptured;

    BarAnim _player;
    BarAnim _ai;
    Coroutine _play;

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
        int high = Mathf.Max(playerScore, aiScore);

        _player.score = playerScore;
        _ai.score = aiScore;

        if (high <= 0)
        {
            _player.targetHeight = MinBarHeight;
            _ai.targetHeight = MinBarHeight;
            _player.targetFill = 0f;
            _ai.targetFill = 0f;
        }
        else if (playerScore == aiScore)
        {
            _player.targetHeight = _player.restHeight;
            _ai.targetHeight = _ai.restHeight;
            _player.targetFill = 1f;
            _ai.targetFill = 1f;
        }
        else if (playerScore > aiScore)
        {
            _player.targetHeight = _player.restHeight;
            _ai.targetHeight = _player.restHeight * (aiScore / (float)high);
            _player.targetFill = 1f;
            _ai.targetFill = aiScore / (float)high;
        }
        else
        {
            _ai.targetHeight = _ai.restHeight;
            _player.targetHeight = _ai.restHeight * (playerScore / (float)high);
            _ai.targetFill = 1f;
            _player.targetFill = playerScore / (float)high;
        }

        ApplyBar(_player, 0f, 0);
        ApplyBar(_ai, 0f, 0);
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
        if (_player.rt == null && _ai.rt == null && _player.tmp == null && _ai.tmp == null)
            yield break;

        float t = 0f;
        while (t < BarDuration)
        {
            t += Time.deltaTime;
            float u = EaseOutQuint(Mathf.Clamp01(t / BarDuration));
            ApplyBar(_player, u, Mathf.RoundToInt(_player.score * u));
            ApplyBar(_ai, u, Mathf.RoundToInt(_ai.score * u));
            yield return null;
        }

        ApplyBar(_player, 1f, _player.score);
        ApplyBar(_ai, 1f, _ai.score);
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
}
