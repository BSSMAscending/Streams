using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// UI Button을 누르는 동안 살짝 줄어들었다가, 손을 떼면 튕기듯 원래 크기로 돌아옵니다.
/// 씬에 붙이지 않아도 플레이 시작 시 모든 Button에 자동으로 붙습니다.
/// </summary>
[DisallowMultipleComponent]
public class UiButtonPressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Range(0.7f, 1f)]
    [SerializeField] float pressedScale = 0.92f;
    [SerializeField] float pressDuration = 0.06f;
    [SerializeField] float releaseDuration = 0.16f;

    Button _button;
    Vector3 _restScale = Vector3.one;

    public Vector3 RestLocalScale => _restScale;
    Coroutine _anim;
    bool _pressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= AttachOnSceneLoaded;
        SceneManager.sceneLoaded += AttachOnSceneLoaded;
        AttachToButtonsInScene();
    }

    static void AttachOnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToButtonsInScene();

    static void AttachToButtonsInScene()
    {
        var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null || button.GetComponent<UiButtonPressScale>() != null)
                continue;
            button.gameObject.AddComponent<UiButtonPressScale>();
        }
    }

    void Awake()
    {
        _button = GetComponent<Button>();
        _restScale = transform.localScale;
    }

    void OnEnable()
    {
        _restScale = transform.localScale;
        _pressed = false;
        transform.localScale = _restScale;
    }

    void OnDisable()
    {
        StopAnim();
        _pressed = false;
        transform.localScale = _restScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanPress())
            return;

        _pressed = true;
        Play(_restScale * pressedScale, pressDuration, EaseOutCubic);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_pressed)
            return;

        _pressed = false;
        Play(_restScale, releaseDuration, EaseOutBack);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_pressed)
            return;

        _pressed = false;
        Play(_restScale, releaseDuration, EaseOutCubic);
    }

    bool CanPress()
    {
        return isActiveAndEnabled
            && !StreamsCardDrawCinematic.IsBlockingPlacement
            && (_button == null || _button.interactable);
    }

    void Play(Vector3 target, float duration, System.Func<float, float> ease)
    {
        StopAnim();
        if (!isActiveAndEnabled)
        {
            transform.localScale = target;
            return;
        }

        _anim = StartCoroutine(Animate(target, duration, ease));
    }

    System.Collections.IEnumerator Animate(Vector3 target, float duration, System.Func<float, float> ease)
    {
        Vector3 from = transform.localScale;
        duration = Mathf.Max(0.01f, duration);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            float k = ease(Mathf.Clamp01(t));
            transform.localScale = Vector3.LerpUnclamped(from, target, k);
            yield return null;
        }

        transform.localScale = target;
        _anim = null;
    }

    void StopAnim()
    {
        if (_anim == null)
            return;
        StopCoroutine(_anim);
        _anim = null;
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

    static float EaseOutBack(float t)
    {
        const float overshoot = 1.70158f;
        float inv = t - 1f;
        return 1f + (overshoot + 1f) * inv * inv * inv + overshoot * inv * inv;
    }
}
