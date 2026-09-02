using UnityEngine;

/// <summary>
/// StartScene에서 고른 난이도에 맞는 <see cref="StreamsAIController"/> 하나만 활성화합니다.
/// 씬에 있는 AI_0 / AI_1000 / AI_1000000 등을 배열에 넣어 두세요.
/// </summary>
[DefaultExecutionOrder(-1050)]
public class StreamsDuelOpponentSetup : MonoBehaviour
{
    [Tooltip("씬에 배치된 AI 컨트롤러들. mctsPerformance로 매칭합니다.")]
    public StreamsAIController[] opponentCandidates;

    [Tooltip("선택 정보가 없을 때 사용할 난이도.")]
    public StreamsAiMctsPerformance fallbackOpponent = StreamsAiMctsPerformance.Normal;

    public StreamsAIController ActiveOpponent { get; private set; }

    void Awake() => ActivateSelected();

    public void ActivateSelected()
    {
        var target = StreamsOpponentSelection.HasSelection
            ? StreamsOpponentSelection.SelectedOpponent
            : fallbackOpponent;

        ActiveOpponent = null;

        if (opponentCandidates != null)
        {
            foreach (var candidate in opponentCandidates)
            {
                if (candidate == null)
                    continue;

                bool isActive = candidate.mctsPerformance == target;
                candidate.gameObject.SetActive(isActive);

                if (isActive)
                    ActiveOpponent = candidate;
            }
        }

        if (ActiveOpponent == null)
            Debug.LogWarning($"StreamsDuelOpponentSetup: {target} 난이도 AI를 찾지 못했습니다.");
    }
}
