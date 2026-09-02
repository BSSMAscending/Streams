/// <summary>StartScene에서 고른 AI를 SampleScene으로 넘기는 세션 데이터.</summary>
public static class StreamsOpponentSelection
{
    public static bool HasSelection { get; private set; }
    public static StreamsAiMctsPerformance SelectedOpponent { get; private set; }

    public static void Select(StreamsAiMctsPerformance opponent)
    {
        SelectedOpponent = opponent;
        HasSelection = true;
    }

    public static void Clear()
    {
        HasSelection = false;
        SelectedOpponent = StreamsAiMctsPerformance.Normal;
    }

    public static string GetDisplayName(StreamsAiMctsPerformance opponent)
    {
        switch (opponent)
        {
            case StreamsAiMctsPerformance.Weak:
                return "AI (1회 학습)";
            case StreamsAiMctsPerformance.Strong:
                return "AI (1000000회 학습)";
            default:
                return "AI (1000회 학습)";
        }
    }

    public static string GetSelectedDisplayName()
    {
        return GetDisplayName(HasSelection ? SelectedOpponent : StreamsAiMctsPerformance.Normal);
    }
}
