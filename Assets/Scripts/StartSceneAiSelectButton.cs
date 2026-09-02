using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// StartScene AI 선택 버튼. 클릭 시 상대를 저장하고 게임 씬으로 이동합니다.
/// 씬의 OnClick 연결이 깨져 있어도 <see cref="StartSceneAiButtonBootstrap"/>이 런타임에 다시 연결합니다.
/// </summary>
[RequireComponent(typeof(Button))]
public class StartSceneAiSelectButton : MonoBehaviour
{
    internal const string DefaultGameSceneName = "SampleScene";

    [Tooltip("이 버튼이 고르는 AI 난이도 (약함/보통/강함).")]
    [SerializeField] StreamsAiMctsPerformance opponent = StreamsAiMctsPerformance.Normal;

    [SerializeField] string gameSceneName = DefaultGameSceneName;

    internal StreamsAiMctsPerformance Opponent => opponent;
    internal string GameSceneName => gameSceneName;

    void Awake() => StartSceneAiButtonBootstrap.WireButton(GetComponent<Button>(), opponent, gameSceneName);

    public void SelectAndStartGame() => StartWithOpponent(opponent, gameSceneName);

    public static void StartWithOpponent(StreamsAiMctsPerformance opponent, string sceneName = DefaultGameSceneName)
    {
        StreamsOpponentSelection.Select(opponent);
        StreamsTutorialSelection.Clear();
        StreamsGameResults.Clear();

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("StartSceneAiSelectButton: sceneName이 비어 있습니다.");
            return;
        }

        StreamsSceneTransition.Load(sceneName);
    }
}

/// <summary>StartScene 진입 시 AI 선택 버튼을 이름으로 찾아 OnClick을 코드로 연결합니다.</summary>
static class StartSceneAiButtonBootstrap
{
    struct ButtonBinding
    {
        public string objectName;
        public StreamsAiMctsPerformance opponent;
    }

    static readonly ButtonBinding[] k_Bindings =
    {
        new ButtonBinding { objectName = "Button_1", opponent = StreamsAiMctsPerformance.Weak },
        new ButtonBinding { objectName = "Button_1000", opponent = StreamsAiMctsPerformance.Normal },
        new ButtonBinding { objectName = "Button_1000000", opponent = StreamsAiMctsPerformance.Strong },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RegisterSceneLoadHandler()
    {
        SceneManager.sceneLoaded -= BindWhenStartSceneLoads;
        SceneManager.sceneLoaded += BindWhenStartSceneLoads;
        BindWhenStartSceneLoads(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void BindWhenStartSceneLoads(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || scene.name != "StartScene")
            return;

        foreach (var binding in k_Bindings)
        {
            var go = GameObject.Find(binding.objectName);
            if (go == null)
            {
                Debug.LogWarning($"StartSceneAiButtonBootstrap: '{binding.objectName}' 오브젝트를 찾을 수 없습니다.");
                continue;
            }

            var button = go.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning($"StartSceneAiButtonBootstrap: '{binding.objectName}'에 Button이 없습니다.");
                continue;
            }

            var selectButton = go.GetComponent<StartSceneAiSelectButton>();
            var opponent = selectButton != null ? selectButton.Opponent : binding.opponent;
            var sceneName = selectButton != null && !string.IsNullOrWhiteSpace(selectButton.GameSceneName)
                ? selectButton.GameSceneName
                : StartSceneAiSelectButton.DefaultGameSceneName;

            WireButton(button, opponent, sceneName);
        }
    }

    internal static void WireButton(Button button, StreamsAiMctsPerformance opponent, string sceneName)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        var selectedOpponent = opponent;
        var selectedScene = sceneName;
        button.onClick.AddListener(() => StartSceneAiSelectButton.StartWithOpponent(selectedOpponent, selectedScene));
    }
}
