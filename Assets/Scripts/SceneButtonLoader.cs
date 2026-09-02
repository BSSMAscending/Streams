using UnityEngine;

/// <summary>
/// UI Button OnClick 이벤트에 연결해서 씬 전환에 사용.
/// </summary>
public class SceneButtonLoader : MonoBehaviour
{
    [SerializeField] private string sceneName;

    /// <summary>
    /// Inspector에 적은 sceneName으로 씬 이동.
    /// </summary>
    public void LoadConfiguredScene()
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("SceneButtonLoader: sceneName이 비어 있습니다.");
            return;
        }

        StreamsSceneTransition.Load(sceneName);
    }

    /// <summary>
    /// 코드나 이벤트에서 직접 씬 이름을 넘겨서 씬 이동.
    /// </summary>
    public void LoadSceneByName(string targetSceneName)
    {
        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogError("SceneButtonLoader: targetSceneName이 비어 있습니다.");
            return;
        }

        StreamsSceneTransition.Load(targetSceneName);
    }
}
