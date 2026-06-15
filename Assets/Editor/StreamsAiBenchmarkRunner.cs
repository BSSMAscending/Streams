using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AI MCTS 3단계(1 / 100 / 10000 시뮬) 단독 20턴 게임 벤치마크. UI 점수(num_path 규칙) 출력.
/// </summary>
public static class StreamsAiBenchmarkRunner
{
    const int GamesPerTier = 100;
    const int BaseSeed = 42_026_051;
    const int JokerModelValue = 21;
    const int DrawLimit = 20;

    static readonly string OutputPath = Path.Combine(
        Path.GetDirectoryName(Application.dataPath) ?? ".",
        "Tools",
        "benchmark_ai_scores.csv");

    [MenuItem("Streams/Benchmark AI Scores (100 games)")]
    public static void RunFromMenu()
    {
        RunBenchmark(GamesPerTier);
    }

    [MenuItem("Streams/Benchmark AI Scores (quick 10 games)")]
    public static void RunQuickFromMenu()
    {
        RunBenchmark(10);
    }

    public static void RunFromCommandLine()
    {
        RunBenchmark(GamesPerTier);
        EditorApplication.Exit(0);
    }

    static void RunBenchmark(int gamesPerTier)
    {
        var tiers = new (string name, int nsim)[]
        {
            ("weak", 1),
            ("normal", 100),
            ("strong", 10000),
        };

        var rows = new List<string> { "tier,nsim,game,seed,score" };
        var summaries = new List<string>();

        try
        {
            int totalSteps = tiers.Length * gamesPerTier;
            int step = 0;

            foreach (var (tierName, nsim) in tiers)
            {
                var scores = new List<int>(gamesPerTier);

                for (int g = 0; g < gamesPerTier; g++)
                {
                    step++;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Streams AI Benchmark",
                            $"{tierName} ({nsim} sim) — game {g + 1}/{gamesPerTier}",
                            step / (float)totalSteps))
                    {
                        Debug.LogWarning("Benchmark cancelled by user.");
                        return;
                    }

                    int seed = BaseSeed + g;
                    int score = PlayOneGame(nsim, seed);
                    scores.Add(score);
                    rows.Add($"{tierName},{nsim},{g},{seed},{score}");
                }

                double mean = Mean(scores);
                double std = StdDev(scores, mean);
                int min = Min(scores);
                int max = Max(scores);
                summaries.Add(
                    $"summary,{tierName},{nsim},,mean={mean:F2},std={std:F2},min={min},max={max},n={gamesPerTier}");
                Debug.Log($"[Benchmark] {tierName} (nsim={nsim}): mean={mean:F2}, std={std:F2}, min={min}, max={max}, n={gamesPerTier}");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        rows.AddRange(summaries);
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? "Tools");
        File.WriteAllText(OutputPath, string.Join("\n", rows), Encoding.UTF8);
        Debug.Log($"Benchmark CSV written to: {OutputPath}");
        AssetDatabase.Refresh();
    }

    static int PlayOneGame(int nsim, int seed)
    {
        var drawSequence = BuildDrawSequence(seed);
        var mctsRng = new System.Random(seed ^ 0x5EED_BEEF);
        var board = new int[20];
        var slotStrings = new string[20];
        for (int i = 0; i < 20; i++)
            board[i] = -1;

        for (int round = 0; round < drawSequence.Count; round++)
        {
            string cardStr = drawSequence[round].card;
            var remainingDeck = drawSequence[round].remainingDeck;

            int emptyCount = 0;
            for (int i = 0; i < 20; i++)
                if (board[i] == -1) emptyCount++;

            int futureTileCount = Math.Max(0, emptyCount - 1);
            var remainingMcts = new List<int>(remainingDeck.Count);
            foreach (var s in remainingDeck)
                remainingMcts.Add(CardStringToMctsInt(s));

            int newTileMcts = CardStringToMctsInt(cardStr);
            int pos = StreamsMctsCore.MctsPosition(board, newTileMcts, remainingMcts, futureTileCount, mctsRng, nsim);
            if (pos < 0 || pos >= 20 || board[pos] != -1)
                pos = FirstEmpty(board);

            board[pos] = newTileMcts;
            slotStrings[pos] = cardStr.Trim().ToUpper() == "J" ? "J" : cardStr;
        }

        for (int i = 0; i < 20; i++)
        {
            if (string.IsNullOrEmpty(slotStrings[i]))
                slotStrings[i] = BoardIntToScoreString(board[i]);
        }

        return StreamsScoreUi.Calculate(slotStrings);
    }

    struct RoundDraw
    {
        public string card;
        public List<string> remainingDeck;
    }

    static List<RoundDraw> BuildDrawSequence(int seed)
    {
        var gameRng = new System.Random(seed);
        var deck = BuildDeck();
        var rounds = new List<RoundDraw>(DrawLimit);

        for (int round = 0; round < DrawLimit && deck.Count > 0; round++)
        {
            int pick = gameRng.Next(0, deck.Count);
            string cardStr = deck[pick];
            deck.RemoveAt(pick);
            rounds.Add(new RoundDraw
            {
                card = cardStr,
                remainingDeck = new List<string>(deck),
            });
        }

        return rounds;
    }

    static string BoardIntToScoreString(int v)
    {
        if (v == 0 || v == JokerModelValue) return "J";
        if (v == -1) return "";
        return v.ToString();
    }

    static int FirstEmpty(int[] board)
    {
        for (int i = 0; i < board.Length; i++)
            if (board[i] == -1) return i;
        return 0;
    }

    static List<string> BuildDeck()
    {
        return new List<string>
        {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
            "11", "12", "13", "14", "15", "16", "17", "18", "19",
            "11", "12", "13", "14", "15", "16", "17", "18", "19",
            "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "J"
        };
    }

    static int CardStringToMctsInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        s = s.Trim().ToUpper();
        if (s == "J") return 0;
        return int.TryParse(s, out int v) ? v : 0;
    }

    static double Mean(List<int> values)
    {
        if (values.Count == 0) return 0;
        long sum = 0;
        foreach (int v in values) sum += v;
        return sum / (double)values.Count;
    }

    static double StdDev(List<int> values, double mean)
    {
        if (values.Count <= 1) return 0;
        double acc = 0;
        foreach (int v in values)
        {
            double d = v - mean;
            acc += d * d;
        }
        return Math.Sqrt(acc / (values.Count - 1));
    }

    static int Min(List<int> values)
    {
        int m = int.MaxValue;
        foreach (int v in values)
            if (v < m) m = v;
        return m == int.MaxValue ? 0 : m;
    }

    static int Max(List<int> values)
    {
        int m = int.MinValue;
        foreach (int v in values)
            if (v > m) m = v;
        return m == int.MinValue ? 0 : m;
    }
}

/// <summary>num_path.CalculateScore / CalculateScoreNoJoker 와 동일 (UI 표시 점수).</summary>
static class StreamsScoreUi
{
    static readonly int[] ScoreTable =
    {
        0, 1, 3, 5, 7, 9, 11, 15, 20, 25,
        30, 35, 40, 50, 60, 70, 85, 100, 150, 300
    };

    public static int Calculate(string[] cards)
    {
        int jokerIdx = -1;
        for (int i = 0; i < cards.Length; i++)
        {
            if (!string.IsNullOrEmpty(cards[i]) && cards[i].Trim().ToUpper() == "J")
            {
                jokerIdx = i;
                break;
            }
        }

        if (jokerIdx == -1)
            return CalculateScoreNoJoker(cards);

        var frontList = (string[])cards.Clone();
        frontList[jokerIdx] = jokerIdx > 0 ? cards[jokerIdx - 1] : "0";
        int frontScore = CalculateScoreNoJoker(frontList);

        var backList = (string[])cards.Clone();
        backList[jokerIdx] = jokerIdx < cards.Length - 1 ? cards[jokerIdx + 1] : "0";
        int backScore = CalculateScoreNoJoker(backList);

        return Math.Max(frontScore, backScore);
    }

    static int CalculateScoreNoJoker(string[] cards)
    {
        int totalScore = 0;
        int currentRun = 0;
        int prevValue = -1;

        for (int i = 0; i < cards.Length; i++)
        {
            string clean = cards[i] == null ? "" : cards[i].Trim().ToUpper();
            if (string.IsNullOrEmpty(clean))
            {
                if (currentRun > 0) totalScore += GetScore(currentRun);
                currentRun = 0;
                prevValue = -1;
                continue;
            }

            if (int.TryParse(clean, out int value))
            {
                if (currentRun > 0 && prevValue != -1 && value < prevValue)
                {
                    totalScore += GetScore(currentRun);
                    currentRun = 1;
                }
                else
                {
                    currentRun++;
                }

                prevValue = value;
            }
        }

        if (currentRun > 0) totalScore += GetScore(currentRun);
        return totalScore;
    }

    static int GetScore(int length)
    {
        if (length <= 0) return 0;
        int idx = Math.Clamp(length, 0, ScoreTable.Length - 1);
        return ScoreTable[idx];
    }
}
