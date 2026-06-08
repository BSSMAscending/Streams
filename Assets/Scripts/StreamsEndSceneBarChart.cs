using UnityEngine;
using UnityEngine.UI;

/// <summary>EndScene result 영역: 회색 막대만 점수 비율로 높이 변경, 막대 위에 점수만 표시.</summary>
public class StreamsEndSceneBarChart : MonoBehaviour
{
    [Header("움직이는 막대 (result 안, 왼→오: AI1, AI2, AI3, 플레이어)")]
    public RectTransform[] bars = new RectTransform[4];

    [Header("점수 라벨 (비우면 막대 위에 자동 생성)")]
    public Text[] scoreLabels;

    [Header("막대 높이")]
    public float maxBarHeight = 320f;
    public float minBarHeight = 8f;

    [Header("점수 텍스트")]
    public int scoreFontSize = 28;
    public Color scoreColor = Color.black;
    public float scoreLabelOffsetAboveBar = 12f;

    void Start()
    {
        EnsureScoreLabels();

        var scores = new int[4];
        if (StreamsGameResults.HasData)
        {
            scores[0] = StreamsGameResults.AiScores[0];
            scores[1] = StreamsGameResults.AiScores[1];
            scores[2] = StreamsGameResults.AiScores[2];
            scores[3] = StreamsGameResults.PlayerScore;
        }
        else
            Debug.LogWarning("StreamsEndSceneBarChart: 게임 결과 데이터가 없습니다.");

        int maxScore = 0;
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i] > maxScore)
                maxScore = scores[i];
        }

        for (int i = 0; i < 4; i++)
        {
            float height = maxScore > 0
                ? Mathf.Lerp(minBarHeight, maxBarHeight, scores[i] / (float)maxScore)
                : minBarHeight;

            if (bars != null && i < bars.Length && bars[i] != null)
            {
                Vector2 size = bars[i].sizeDelta;
                bars[i].sizeDelta = new Vector2(size.x, height);
            }

            if (scoreLabels != null && i < scoreLabels.Length && scoreLabels[i] != null)
                scoreLabels[i].text = scores[i].ToString();
        }
    }

    void EnsureScoreLabels()
    {
        if (bars == null || bars.Length < 4)
            return;

        bool needScores = scoreLabels == null || scoreLabels.Length < 4;
        if (!needScores)
        {
            for (int i = 0; i < 4; i++)
            {
                if (scoreLabels[i] == null)
                {
                    needScores = true;
                    break;
                }
            }
        }

        if (!needScores)
            return;

        scoreLabels = new Text[4];
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        for (int i = 0; i < 4; i++)
        {
            if (bars[i] == null)
                continue;

            scoreLabels[i] = CreateScoreLabel(bars[i], font);
        }
    }

    Text CreateScoreLabel(RectTransform bar, Font font)
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
        label.font = font;
        label.fontSize = scoreFontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = scoreColor;
        label.text = "0";
        return label;
    }
}
