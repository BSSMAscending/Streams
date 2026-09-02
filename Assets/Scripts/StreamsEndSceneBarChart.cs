using UnityEngine;
using UnityEngine.UI;

/// <summary>EndScene result: AI 막대 1개(공유) + 플레이어 막대 1개.</summary>
public class StreamsEndSceneBarChart : MonoBehaviour
{
    [Header("1대1 막대 (비우면 Bar_AI / Bar_Player 이름으로 탐색)")]
    public RectTransform aiBar;
    public RectTransform playerBar;

    [Header("점수 라벨 (비우면 막대 위에 자동 생성)")]
    public Text aiScoreLabel;
    public Text playerScoreLabel;

    [Header("예전 4분할 UI (겹치면 자동 숨김)")]
    public GameObject legacyWinScoreRoot;

    [Header("하위 호환 bars 배열 (비우면 위 필드 사용)")]
    public RectTransform[] bars = new RectTransform[4];

    [Header("막대 높이")]
    public float maxBarHeight = 1200f;
    public int referenceMaxScore = 300;
    public float minBarHeight = 8f;

    [Header("점수 텍스트")]
    public int scoreFontSize = 28;
    public Color scoreColor = Color.black;
    public float scoreLabelOffsetAboveBar = 12f;

    void Start()
    {
        if (IsUnderResultCanvas())
            return;

        HideLegacyOverlaps();
        ResolveBars();
        Refresh();
    }

    bool IsUnderResultCanvas()
    {
        for (Transform t = transform; t != null; t = t.parent)
        {
            if (t.name == "ResultCanvas")
                return true;
        }

        return false;
    }

    public void Refresh()
    {
        HideLegacyOverlaps();
        ResolveBars();

        if (aiBar == null || playerBar == null)
        {
            Debug.LogWarning("StreamsEndSceneBarChart: Bar_AI / Bar_Player를 찾지 못했습니다.");
            return;
        }

        HideUnusedBars();

        int aiScore = 0;
        int playerScore = 0;
        if (StreamsGameResults.HasData)
        {
            aiScore = StreamsGameResults.IsDuel
                ? StreamsGameResults.OpponentScore
                : StreamsGameResults.AiScores[0];
            playerScore = StreamsGameResults.PlayerScore;
        }
        else
        {
            Debug.LogWarning("StreamsEndSceneBarChart: 게임 결과 데이터가 없습니다.");
        }

        ApplyBar(aiBar, aiScore, aiScoreLabel);
        ApplyBar(playerBar, playerScore, playerScoreLabel);
        ApplyAxisLabels();
    }

    void ResolveBars()
    {
        if (aiBar == null)
            aiBar = FindBar("Bar_AI") ?? (bars != null && bars.Length > 0 ? bars[0] : null);
        if (playerBar == null)
            playerBar = FindBar("Bar_Player") ?? FindLastBarInArray();

        if (legacyWinScoreRoot == null)
        {
            var legacy = GameObject.Find("winscore");
            if (legacy != null)
                legacyWinScoreRoot = legacy;
        }
    }

    RectTransform FindBar(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        foreach (var rt in GetComponentsInChildren<RectTransform>(true))
        {
            if (rt != null && rt.name == objectName)
                return rt;
        }

        var found = GameObject.Find(objectName);
        return found != null ? found.GetComponent<RectTransform>() : null;
    }

    RectTransform FindLastBarInArray()
    {
        if (bars == null)
            return null;

        for (int i = bars.Length - 1; i >= 0; i--)
        {
            if (bars[i] != null)
                return bars[i];
        }

        return null;
    }

    void HideLegacyOverlaps()
    {
        if (legacyWinScoreRoot != null)
            legacyWinScoreRoot.SetActive(false);
    }

    void HideUnusedBars()
    {
        if (bars == null)
            return;

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] == null)
                continue;

            bool keep = bars[i] == aiBar || bars[i] == playerBar;
            bars[i].gameObject.SetActive(keep);
        }

        if (aiBar != null)
            aiBar.gameObject.SetActive(true);
        if (playerBar != null)
            playerBar.gameObject.SetActive(true);
    }

    void ApplyAxisLabels()
    {
        SetAxisLabel(playerBar, "플레이어");
        SetAxisLabel(aiBar, StreamsOpponentSelection.GetSelectedDisplayName());
    }

    void SetAxisLabel(RectTransform bar, string text)
    {
        if (bar == null)
            return;

        Text label = FindAxisLabel(bar) ?? CreateAxisLabel(bar);
        if (label != null)
            label.text = text;
    }

    static Text FindAxisLabel(RectTransform bar)
    {
        foreach (var text in bar.GetComponentsInChildren<Text>(true))
        {
            if (text != null && text.gameObject.name == "AxisLabel")
                return text;
        }

        return null;
    }

    Text CreateAxisLabel(RectTransform bar)
    {
        var go = new GameObject("AxisLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(bar, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -8f);
        rt.sizeDelta = new Vector2(220f, 40f);

        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 22;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.UpperCenter;
        label.color = scoreColor;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    void ApplyBar(RectTransform bar, int score, Text label)
    {
        if (bar == null)
            return;

        float height = ScoreToBarHeight(score);
        Vector2 size = bar.sizeDelta;
        bar.sizeDelta = new Vector2(size.x, height);

        Text scoreText = label != null ? label : FindOrCreateScoreLabel(bar);
        if (scoreText != null)
        {
            scoreText.text = score.ToString() + "점";
            PositionScoreLabelAboveBar(scoreText, bar);
        }
    }

    void PositionScoreLabelAboveBar(Text scoreText, RectTransform bar)
    {
        var rt = scoreText.rectTransform;
        rt.SetParent(bar, false);
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, scoreLabelOffsetAboveBar);
        rt.sizeDelta = new Vector2(140f, 36f);
        scoreText.transform.SetAsLastSibling();
    }

    Text FindOrCreateScoreLabel(RectTransform bar)
    {
        foreach (var text in bar.GetComponentsInChildren<Text>(true))
        {
            if (text != null && text.gameObject.name != "AxisLabel")
                return text;
        }

        return CreateScoreLabel(bar);
    }

    float ScoreToBarHeight(int score)
    {
        if (score <= 0 || referenceMaxScore <= 0)
            return minBarHeight;

        float height = score / (float)referenceMaxScore * maxBarHeight;
        return Mathf.Clamp(height, minBarHeight, maxBarHeight);
    }

    Text CreateScoreLabel(RectTransform bar)
    {
        var go = new GameObject("ScoreLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(bar, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, scoreLabelOffsetAboveBar);
        rt.sizeDelta = new Vector2(140f, 36f);

        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = scoreFontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = scoreColor;
        label.text = "0";
        return label;
    }
}
