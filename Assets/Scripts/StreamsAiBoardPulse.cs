using System.Collections;
using UnityEngine;

/// <summary>
/// AI가 카드를 놓을 때 AI 보드를 버튼처럼 줄였다 되돌립니다.
/// 보드 로직은 비활성 오브젝트에 있어서, 보이는 AIBoard에서 직접 재생합니다.
/// </summary>
[DisallowMultipleComponent]
public class StreamsAiBoardPulse : MonoBehaviour
{
    const float PressedScale = 0.88f;
    const float PressDuration = 0.12f;
    const float ReleaseDuration = 0.28f;

    RectTransform _rect;
    Vector3 _restScale = Vector3.one;
    Vector2 _restAnchored;
    Vector2 _size;
    bool _captured;
    Coroutine _routine;

    public static void PlayForSlot(Transform slot)
    {
        var board = FindAiBoard(slot);
        if (board == null || !board.gameObject.activeInHierarchy)
            return;

        var pulse = board.GetComponent<StreamsAiBoardPulse>();
        if (pulse == null)
            pulse = board.gameObject.AddComponent<StreamsAiBoardPulse>();
        pulse.Play();
    }

    static RectTransform FindAiBoard(Transform start)
    {
        for (Transform t = start; t != null; t = t.parent)
        {
            if (t.name == "AIBoard")
                return t as RectTransform;
        }

        return null;
    }

    void Awake()
    {
        _rect = transform as RectTransform;
    }

    void OnDisable()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        if (_captured && _rect != null)
            Apply(_restScale);
    }

    void Play()
    {
        if (_rect == null)
            _rect = transform as RectTransform;
        if (_rect == null || !isActiveAndEnabled)
            return;

        if (!_captured)
        {
            _restScale = _rect.localScale;
            _restAnchored = _rect.anchoredPosition;
            _size = _rect.rect.size;
            if (_size.x < 1f || _size.y < 1f)
                _size = _rect.sizeDelta;
            _captured = true;
        }

        if (_routine != null)
            StopCoroutine(_routine);
        _routine = StartCoroutine(Pulse());
    }

    IEnumerator Pulse()
    {
        Vector2 center = Center(_restAnchored, _restScale);
        Vector3 pressed = _restScale * PressedScale;
        Vector3 from = _rect.localScale;

        float duration = Mathf.Max(0.01f, PressDuration);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            Apply(Vector3.LerpUnclamped(from, pressed, EaseOutCubic(Mathf.Clamp01(t))), center);
            yield return null;
        }

        from = _rect.localScale;
        duration = Mathf.Max(0.01f, ReleaseDuration);
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            Apply(Vector3.LerpUnclamped(from, _restScale, EaseOutBack(Mathf.Clamp01(t))), center);
            yield return null;
        }

        Apply(_restScale);
        _routine = null;
    }

    Vector2 Center(Vector2 anchored, Vector3 scale)
    {
        Vector2 localCenter = LocalCenter();
        return anchored + Vector2.Scale(localCenter, new Vector2(scale.x, scale.y));
    }

    void Apply(Vector3 scale, Vector2 center)
    {
        Vector2 localCenter = LocalCenter();
        _rect.localScale = scale;
        _rect.anchoredPosition = center - Vector2.Scale(localCenter, new Vector2(scale.x, scale.y));
    }

    void Apply(Vector3 scale)
    {
        _rect.localScale = scale;
        _rect.anchoredPosition = _restAnchored;
    }

    Vector2 LocalCenter()
    {
        return new Vector2((0.5f - _rect.pivot.x) * _size.x, (0.5f - _rect.pivot.y) * _size.y);
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

    static float EaseOutBack(float t)
    {
        const float overshoot = 1.70158f;
        float inv = t - 1f;
        return 1f + (overshoot + 1f) * inv * inv * inv + overshoot * inv * inv;
    }
}
