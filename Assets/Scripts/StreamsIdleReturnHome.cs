using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임 화면에서 일정 시간 동안 입력이 없으면 홈(StartScene)으로 돌아갑니다.
/// 1분 남으면 TimeText를 천천히 드러내 카운트다운하고, 버튼 클릭이 있으면 빠르게 숨깁니다.
/// 마우스 이동은 활동으로 보지 않고, UI 버튼 클릭만 타이머를 리셋합니다.
/// 씬에 오브젝트를 넣을 필요 없이 플레이 시작 시 자동으로 등록됩니다.
/// </summary>
public class StreamsIdleReturnHome : MonoBehaviour
{
    const string HomeSceneName = "StartScene";
    const string TimeTextObjectName = "TimeText";
    const float IdleSeconds = 180f;
    const float WarningSeconds = 60f;
    const float FadeInSeconds = 1.4f;
    const float FadeOutSeconds = 0.22f;

    static readonly List<RaycastResult> RaycastHits = new List<RaycastResult>();

    float _idleTime;
    float _alpha;
    TextMeshProUGUI _timeText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<StreamsIdleReturnHome>() != null)
            return;

        var go = new GameObject(nameof(StreamsIdleReturnHome));
        DontDestroyOnLoad(go);
        go.AddComponent<StreamsIdleReturnHome>();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _timeText = null;
        _alpha = 0f;
        _idleTime = 0f;
    }

    void Update()
    {
        var scene = SceneManager.GetActiveScene();
        bool onHome = !scene.IsValid() || scene.name == HomeSceneName;
        bool activity = !onHome && HasActivity();

        if (onHome || activity)
            _idleTime = 0f;
        else
            _idleTime += Time.unscaledDeltaTime;

        float remaining = IdleSeconds - _idleTime;
        bool warning = !onHome && remaining <= WarningSeconds;
        UpdateTimeText(warning, remaining);

        if (onHome || _idleTime < IdleSeconds)
            return;

        _idleTime = 0f;
        StreamsTutorialSelection.Clear();
        StreamsSceneTransition.Load(HomeSceneName);
    }

    void UpdateTimeText(bool warning, float remaining)
    {
        ResolveTimeText();
        if (_timeText == null)
            return;

        if (warning)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
            _timeText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }

        float dt = Time.unscaledDeltaTime;
        if (warning)
            _alpha = Mathf.MoveTowards(_alpha, 1f, dt / FadeInSeconds);
        else
            _alpha = Mathf.MoveTowards(_alpha, 0f, dt / FadeOutSeconds);

        _timeText.alpha = _alpha;
        _timeText.raycastTarget = false;

        bool show = warning || _alpha > 0.001f;
        if (_timeText.gameObject.activeSelf != show)
            _timeText.gameObject.SetActive(show);
    }

    void ResolveTimeText()
    {
        if (_timeText != null)
            return;

        foreach (var tmp in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tmp != null && tmp.name.Equals(TimeTextObjectName, System.StringComparison.OrdinalIgnoreCase))
            {
                _timeText = tmp;
                _timeText.alpha = 0f;
                _timeText.raycastTarget = false;
                return;
            }
        }
    }

    static bool HasActivity()
    {
        if (!WasPointerPressedThisFrame())
            return false;

        var es = EventSystem.current;
        if (es == null || !TryPointerPosition(out Vector2 position))
            return false;

        var eventData = new PointerEventData(es) { position = position };
        RaycastHits.Clear();
        es.RaycastAll(eventData, RaycastHits);

        for (int i = 0; i < RaycastHits.Count; i++)
        {
            var hit = RaycastHits[i].gameObject;
            if (hit == null)
                continue;

            var button = hit.GetComponentInParent<Button>();
            if (button != null && button.IsActive() && button.IsInteractable())
                return true;
        }

        return false;
    }

    static bool WasPointerPressedThisFrame()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
        return Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
    }

    static bool TryPointerPosition(out Vector2 position)
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            position = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        if (Mouse.current != null)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }

        position = default;
        return false;
    }
}
