/// <summary>StartScene에서 튜토리얼을 고르면 SampleScene이 대전 대신 안내 흐름을 탑니다.</summary>
public static class StreamsTutorialSelection
{
    public static bool IsActive { get; private set; }

    public static void Start()
    {
        IsActive = true;
    }

    public static void Clear()
    {
        IsActive = false;
    }
}
