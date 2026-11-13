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
                return "1번 연습한 AI";
            case StreamsAiMctsPerformance.Strong:
                return "1,000,000번 연습한 AI";
            default:
                return "1,000번 연습한 AI";
        }
    }

    public static string GetSelectedDisplayName()
    {
        return GetDisplayName(HasSelection ? SelectedOpponent : StreamsAiMctsPerformance.Normal);
    }

    /// <summary>결과창 한 줄. 1번 초보 / 1000번 중수 / 100만번 고수.</summary>
    public static string GetResultHeadline(StreamsAiMctsPerformance opponent, int playerScore, int aiScore)
    {
        string ai = PracticeTitle(opponent) + " AI";
        if (playerScore > aiScore)
            return ai + "를 이겼어요!";
        if (aiScore > playerScore)
            return ai + "가 이겼어요!";
        return ai + "와 비겼어요!";
    }

    static string PracticeTitle(StreamsAiMctsPerformance opponent)
    {
        switch (opponent)
        {
            case StreamsAiMctsPerformance.Weak:
                return "1번 연습한 초보";
            case StreamsAiMctsPerformance.Strong:
                return "100만번 연습한 고수";
            default:
                return "1000번 연습한 중수";
        }
    }
}
