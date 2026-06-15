/// <summary>SampleScene → EndScene 씬 전환 시 점수 전달용(단일 플레이 세션).</summary>
public static class StreamsGameResults
{
    public static bool HasData { get; private set; }
    public static int PlayerScore { get; private set; }
    public static readonly int[] AiScores = new int[3];

    public static void SaveFromBoards(num_path player, num_path[] aiBoards)
    {
        PlayerScore = player != null ? player.GetBoardScore() : 0;

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
        PlayerScore = 0;
        for (int i = 0; i < AiScores.Length; i++)
            AiScores[i] = 0;
    }
}
