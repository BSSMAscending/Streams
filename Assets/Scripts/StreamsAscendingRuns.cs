using System.Collections.Generic;
using UnityEngine;

/// <summary>점수와 같은 규칙으로 오름차순 run 구간을 구합니다.</summary>
public static class StreamsAscendingRuns
{
    /// <summary>오름차순 사각형 기본 색. #00e7ff</summary>
    public static readonly Color DefaultColor = new Color(0f, 231f / 255f, 1f, 1f);

    /// <summary>index = run 길이. 1칸은 0점, 20칸은 300점. num_path·MCTS와 동일합니다.</summary>
    public static readonly int[] ScoreByLength =
    {
        0, 0, 1, 3, 5, 7, 9, 11, 15, 20,
        25, 30, 35, 40, 50, 60, 70, 85, 100, 150, 300
    };

    public static int ScoreForLength(int length)
    {
        if (length <= 0)
            return 0;
        return ScoreByLength[Mathf.Clamp(length, 0, ScoreByLength.Length - 1)];
    }

    /// <summary>
    /// 첫 구간은 기본 색, 이후는 이미 쓴 색과 가장 먼 보색 쪽으로 배치해 겹치지 않게 합니다.
    /// 순서: 0°, 180°, 90°, 270°, 45° …
    /// </summary>
    public static Color RunColor(int runIndex)
    {
        Color.RGBToHSV(DefaultColor, out float h, out float s, out float v);
        Color c = Color.HSVToRGB(Mathf.Repeat(h + DistinctHueOffset(runIndex), 1f), s, v);
        c.a = 1f;
        return c;
    }

    /// <summary>0, 0.5, 0.25, 0.75, 0.125, … (색상환에서 남는 가장 큰 간격의 가운데).</summary>
    static float DistinctHueOffset(int runIndex)
    {
        if (runIndex <= 0)
            return 0f;

        uint n = (uint)runIndex;
        float offset = 0f;
        float step = 0.5f;
        while (n != 0)
        {
            if ((n & 1) != 0)
                offset += step;
            n >>= 1;
            step *= 0.5f;
        }

        return offset;
    }

    public static List<(int start, int end)> FromCards(IList<string> cards)
    {
        var runs = new List<(int start, int end)>();
        if (cards == null || cards.Count == 0)
            return runs;

        var processed = ResolveJoker(cards);
        int runStart = 0;
        int prevValue = -1;

        for (int i = 0; i < processed.Count; i++)
        {
            string clean = processed[i];
            if (string.IsNullOrEmpty(clean))
            {
                AddRun(runs, runStart, i - 1);
                runStart = i + 1;
                prevValue = -1;
                continue;
            }

            if (!int.TryParse(clean, out int value))
                continue;

            if (prevValue != -1 && value < prevValue)
            {
                AddRun(runs, runStart, i - 1);
                runStart = i;
            }

            prevValue = value;
        }

        AddRun(runs, runStart, processed.Count - 1);
        return runs;
    }

    static List<string> ResolveJoker(IList<string> cards)
    {
        var processed = new List<string>(cards.Count);
        int jokerIdx = -1;
        for (int i = 0; i < cards.Count; i++)
        {
            string clean = (cards[i] ?? "").Trim().ToUpperInvariant();
            processed.Add(clean);
            if (jokerIdx < 0 && clean == "J")
                jokerIdx = i;
        }

        if (jokerIdx < 0)
            return processed;

        var front = new List<string>(processed);
        front[jokerIdx] = CopyNeighborValue(processed, jokerIdx - 1);
        var back = new List<string>(processed);
        back[jokerIdx] = CopyNeighborValue(processed, jokerIdx + 1);

        int frontScore = ScoreNoJoker(front);
        int backScore = ScoreNoJoker(back);
        if (frontScore != backScore)
            return frontScore > backScore ? front : back;

        // 2~6칸, 8~12칸처럼 점수 증가분이 같은 길이는 총점이 같다.
        // 그때 >= 로 왼쪽을 고르면 오른쪽이 더 길어도 짧은 쪽에 붙는다.
        int frontLen = RunLengthContaining(front, jokerIdx);
        int backLen = RunLengthContaining(back, jokerIdx);
        return frontLen >= backLen ? front : back;
    }

    /// <summary>조커를 치환한 뒤 그 칸이 들어 있는 오름차순 구간 길이.</summary>
    static int RunLengthContaining(List<string> cards, int index)
    {
        int runStart = 0;
        int prevValue = -1;

        for (int i = 0; i < cards.Count; i++)
        {
            string clean = cards[i];
            if (string.IsNullOrEmpty(clean))
            {
                if (index >= runStart && index < i)
                    return i - runStart;
                runStart = i + 1;
                prevValue = -1;
                continue;
            }

            if (!int.TryParse(clean, out int value))
                continue;

            if (prevValue != -1 && value < prevValue)
            {
                if (index >= runStart && index < i)
                    return i - runStart;
                runStart = i;
            }

            prevValue = value;
        }

        if (index >= runStart && index < cards.Count)
            return cards.Count - runStart;
        return 0;
    }

    /// <summary>조커를 이웃 숫자로 바꿀 때 빈 칸·조커는 0으로 둡니다. 혼자 있는 J도 한 칸 run이 됩니다.</summary>
    public static string CopyNeighborValue(IList<string> cards, int fromIdx)
    {
        if (cards == null || fromIdx < 0 || fromIdx >= cards.Count)
            return "0";

        string n = (cards[fromIdx] ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(n) || n == "J")
            return "0";
        return int.TryParse(n, out _) ? n : "0";
    }

    static int ScoreNoJoker(List<string> cards)
    {
        int total = 0;
        int run = 0;
        int prev = -1;

        for (int i = 0; i < cards.Count; i++)
        {
            string clean = cards[i];
            if (string.IsNullOrEmpty(clean))
            {
                if (run > 0)
                    total += ScoreForLength(run);
                run = 0;
                prev = -1;
                continue;
            }

            if (!int.TryParse(clean, out int value))
                continue;

            if (run > 0 && prev != -1 && value < prev)
            {
                total += ScoreForLength(run);
                run = 1;
            }
            else
            {
                run++;
            }

            prev = value;
        }

        if (run > 0)
            total += ScoreForLength(run);
        return total;
    }

    static void AddRun(List<(int start, int end)> runs, int start, int end)
    {
        if (start <= end)
            runs.Add((start, end));
    }
}
