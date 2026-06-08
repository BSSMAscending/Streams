using System.Text;

const int gamesPerTier = 100;
const int baseSeed = 42_026_051;
var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var outputPath = Path.Combine(projectRoot, "Tools", "benchmark_ai_scores.csv");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var tiers = new (string name, int nsim)[]
{
    ("weak", 1),
    ("normal", 100),
    ("strong", 10000),
};

var rows = new List<string> { "tier,nsim,game,seed,score" };
var summaries = new List<string>();

foreach (var (tierName, nsim) in tiers)
{
    Console.WriteLine($"{tierName} (nsim={nsim})...");
    var scores = new List<int>(gamesPerTier);

    for (int g = 0; g < gamesPerTier; g++)
    {
        if (g % 10 == 0)
            Console.WriteLine($"  game {g + 1}/{gamesPerTier}");
        int seed = baseSeed + g;
        int score = BenchmarkGame.PlayOneGame(nsim, seed);
        scores.Add(score);
        rows.Add($"{tierName},{nsim},{g},{seed},{score}");
    }

    double mean = Stats.Mean(scores);
    double std = Stats.StdDev(scores, mean);
    int min = Stats.Min(scores);
    int max = Stats.Max(scores);
    summaries.Add($"summary,{tierName},{nsim},,mean={mean:F2},std={std:F2},min={min},max={max},n={gamesPerTier}");
    Console.WriteLine($"  mean={mean:F2}, std={std:F2}, min={min}, max={max}");
}

rows.AddRange(summaries);
File.WriteAllText(outputPath, string.Join("\n", rows), Encoding.UTF8);
Console.WriteLine($"Wrote {outputPath}");

static class BenchmarkGame
{
    const int DrawLimit = 20;

    public static int PlayOneGame(int nsim, int seed)
    {
        var drawSequence = BuildDrawSequence(seed);
        var mctsRng = new Random(seed ^ unchecked((int)0x5EED_BEEF));
        var board = new int[20];
        var slotStrings = new string[20];
        Array.Fill(board, -1);

        foreach (var round in drawSequence)
        {
            int emptyCount = 0;
            for (int i = 0; i < 20; i++)
                if (board[i] == -1) emptyCount++;

            int futureTileCount = Math.Max(0, emptyCount - 1);
            var remainingMcts = new List<int>(round.remainingDeck.Count);
            foreach (var s in round.remainingDeck)
                remainingMcts.Add(CardStringToMctsInt(s));

            int newTileMcts = CardStringToMctsInt(round.card);
            int pos = StreamsMctsCore.MctsPosition(board, newTileMcts, remainingMcts, futureTileCount, mctsRng, nsim);
            if (pos < 0 || pos >= 20 || board[pos] != -1)
                pos = FirstEmpty(board);

            board[pos] = newTileMcts;
            slotStrings[pos] = round.card.Trim().ToUpperInvariant() == "J" ? "J" : round.card;
        }

        for (int i = 0; i < 20; i++)
        {
            if (string.IsNullOrEmpty(slotStrings[i]))
                slotStrings[i] = BoardIntToScoreString(board[i]);
        }

        return StreamsScoreUi.Calculate(slotStrings);
    }

    static string BoardIntToScoreString(int v)
    {
        if (v == 0) return "J";
        if (v == -1) return "";
        return v.ToString();
    }

    static int FirstEmpty(int[] board)
    {
        for (int i = 0; i < board.Length; i++)
            if (board[i] == -1) return i;
        return 0;
    }

    static List<RoundDraw> BuildDrawSequence(int seed)
    {
        var gameRng = new Random(seed);
        var deck = BuildDeck();
        var rounds = new List<RoundDraw>(DrawLimit);

        for (int round = 0; round < DrawLimit && deck.Count > 0; round++)
        {
            int pick = gameRng.Next(0, deck.Count);
            string cardStr = deck[pick];
            deck.RemoveAt(pick);
            rounds.Add(new RoundDraw(cardStr, new List<string>(deck)));
        }

        return rounds;
    }

    static List<string> BuildDeck() => new()
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
        "11", "12", "13", "14", "15", "16", "17", "18", "19",
        "11", "12", "13", "14", "15", "16", "17", "18", "19",
        "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "J"
    };

    static int CardStringToMctsInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        s = s.Trim().ToUpperInvariant();
        if (s == "J") return 0;
        return int.TryParse(s, out int v) ? v : 0;
    }

    readonly record struct RoundDraw(string card, List<string> remainingDeck);
}

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
            if (!string.IsNullOrEmpty(cards[i]) && cards[i].Trim().ToUpperInvariant() == "J")
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

        foreach (string card in cards)
        {
            string clean = card == null ? "" : card.Trim().ToUpperInvariant();
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
        return ScoreTable[Math.Clamp(length, 0, ScoreTable.Length - 1)];
    }
}

static class Stats
{
    public static double Mean(List<int> values)
    {
        long sum = 0;
        foreach (int v in values) sum += v;
        return sum / (double)values.Count;
    }

    public static double StdDev(List<int> values, double mean)
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

    public static int Min(List<int> values) => values.Min();
    public static int Max(List<int> values) => values.Max();
}
