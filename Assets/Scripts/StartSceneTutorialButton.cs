using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StartScene 튜토리얼 버튼. 클릭 시 튜토리얼 세션을 켜고 게임 씬으로 이동합니다.
/// </summary>
[RequireComponent(typeof(Button))]
public class StartSceneTutorialButton : MonoBehaviour
{
    const string DefaultGameSceneName = "SampleScene";

    [SerializeField] string gameSceneName = DefaultGameSceneName;

    void Awake()
    {
        var button = GetComponent<Button>();
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(StartTutorial);
    }

    public void StartTutorial()
    {
        string sceneName = string.IsNullOrWhiteSpace(gameSceneName) ? DefaultGameSceneName : gameSceneName;
        StartTutorialSession(sceneName);
    }

    public static void StartTutorialSession(string sceneName = DefaultGameSceneName)
    {
        StreamsTutorialSelection.Start();
        StreamsOpponentSelection.Clear();
        StreamsGameResults.Clear();

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("StartSceneTutorialButton: sceneName이 비어 있습니다.");
            return;
        }

        StreamsSceneTransition.Load(sceneName);
    }
}
