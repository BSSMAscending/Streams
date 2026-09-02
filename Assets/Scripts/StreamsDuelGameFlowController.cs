using UnityEngine;

/// <summary>
/// 예전 1대1 전용 컴포넌트. <see cref="StreamsGameFlowController"/>가 동일 역할을 합니다.
/// 씬에 둘 다 있으면 이 컴포넌트는 자동 비활성화됩니다.
/// </summary>
[DefaultExecutionOrder(-999)]
public class StreamsDuelGameFlowController : MonoBehaviour
{
    void Awake()
    {
        if (FindFirstObjectByType<StreamsGameFlowController>() != null)
            enabled = false;
    }
}
