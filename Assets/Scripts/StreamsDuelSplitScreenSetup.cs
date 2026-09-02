using UnityEngine;

/// <summary>
/// 예전 1대1 전용 컴포넌트. <see cref="StreamsGameFlowController"/>가 동일 역할을 합니다.
/// </summary>
[DefaultExecutionOrder(-999)]
public class StreamsDuelSplitScreenSetup : MonoBehaviour
{
    void Awake()
    {
        if (FindFirstObjectByType<StreamsGameFlowController>() != null)
            enabled = false;
    }
}
