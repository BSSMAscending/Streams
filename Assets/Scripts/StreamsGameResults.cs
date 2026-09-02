/// <summary>SampleScene → EndScene 씬 전환 시 점수 전달용(단일 플레이 세션).</summary>
public static class StreamsGameResults
{
    public static bool HasData { get; private set; }
    public static bool IsDuel { get; private set; }
    public static int PlayerScore { get; private set; }
    public static int OpponentScore { get; private set; }
    public static readonly int[] AiScores = new int[3];

    /// <summary>플레이어 판이 모두 채워졌을 때 확정된 점수(게임 화면 표시와 동일).</summary>
    public static bool PlayerScoreFinalized { get; private set; }

    /// <summary>플레이어가 마지막 카드를 놓아 판이 가득 찼을 때 호출.</summary>
    public static void SetPlayerFinalScore(int score)
    {
        PlayerScore = score;
        PlayerScoreFinalized = true;
        HasData = true;
    }

    public static void SaveFromDuel(num_path player, num_path opponent)
    {
        IsDuel = true;

        if (!PlayerScoreFinalized)
            PlayerScore = player != null ? player.GetBoardScore() : 0;
        else if (player != null)
        {
            int boardScore = player.GetBoardScore();
            if (boardScore != PlayerScore)
            {
                UnityEngine.Debug.LogWarning(
                    $"StreamsGameResults: 확정 플레이어 점수({PlayerScore})와 보드 재계산({boardScore})이 다릅니다. 확정 점수를 유지합니다.");
            }
        }

        OpponentScore = opponent != null ? opponent.GetBoardScore() : 0;
        AiScores[0] = OpponentScore;
        AiScores[1] = 0;
        AiScores[2] = 0;
        HasData = true;
    }

    public static void SaveFromBoards(num_path player, num_path[] aiBoards)
    {
        IsDuel = false;
        OpponentScore = 0;

        if (!PlayerScoreFinalized)
            PlayerScore = player != null ? player.GetBoardScore() : 0;
        else if (player != null)
        {
            int boardScore = player.GetBoardScore();
            if (boardScore != PlayerScore)
            {
                UnityEngine.Debug.LogWarning(
                    $"StreamsGameResults: 확정 플레이어 점수({PlayerScore})와 보드 재계산({boardScore})이 다릅니다. 확정 점수를 유지합니다.");
            }
        }

        for (int i = 0; i < AiScores.Length; i++)
        {
            num_path board = aiBoards != null && i < aiBoards.Length ? aiBoards[i] : null;
            AiScores[i] = board != null ? board.GetBoardScore() : 0;
        }

        HasData = true;
    }

    public static void Clear()
    {
        HasData = false;
        IsDuel = false;
        PlayerScoreFinalized = false;
        PlayerScore = 0;
        OpponentScore = 0;
        for (int i = 0; i < AiScores.Length; i++)
            AiScores[i] = 0;
    }
}
