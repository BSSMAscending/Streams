using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 모든 씬에서 ESC를 일정 시간 누르고 있으면 게임을 종료합니다.
/// 씬에 오브젝트를 넣을 필요 없이 플레이 시작 시 자동으로 등록됩니다.
/// </summary>
public class HoldEscToQuit : MonoBehaviour
{
    [SerializeField] private float holdSeconds = 1f;

    private float _heldTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<HoldEscToQuit>() != null)
            return;

        var go = new GameObject(nameof(HoldEscToQuit));
        DontDestroyOnLoad(go);
        go.AddComponent<HoldEscToQuit>();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.escapeKey.isPressed)
        {
            _heldTime += Time.unscaledDeltaTime;
            if (_heldTime >= holdSeconds)
                Quit();
        }
        else
        {
            _heldTime = 0f;
        }
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
