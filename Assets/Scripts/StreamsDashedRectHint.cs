using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보드당 점선 안내 하나. 중첩 Canvas라 흐르는 점선이 보드 UI 전체를 다시 배치하지 않습니다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StreamsDashedRectHint : MaskableGraphic
{
    const string OverlayName = "DashHints";
    const float SpriteSize = 512f;
    const float PadLeft = 120f;
    const float PadRight = 120f;
    const float PadTop = 104f;
    const float PadBottom = 232f;

    [Tooltip("선 두께.")]
    public float thickness = 8f;
    [Tooltip("점선 한 토막 길이.")]
    public float dashLength = 28f;
    [Tooltip("점선 사이 간격.")]
    public float gapLength = 16f;
    [Tooltip("점선이 흐르는 속도.")]
    public float marchSpeed = 70f;
    [Tooltip("투명도 깜빡임 주기(초).")]
    public float pulsePeriod = 2.2f;
    [Tooltip("깜빡일 때 가장 낮은 투명도.")]
    public float pulseMinAlpha = 0.4f;
    [Tooltip("깜빡일 때 가장 높은 투명도.")]
    public float pulseMaxAlpha = 0.95f;

    struct Hint
    {
        public StreamsUiSlot slot;
        public Color color;
    }

    readonly List<Hint> _hints = new List<Hint>();
    float _offset;
    float _pulseTime;
    bool _introDone;
    float _appliedAlpha = 1f;

    public override Texture mainTexture => s_WhiteTexture;

    public static void SetSlotHint(StreamsUiSlot slot, bool on, Color color)
    {
        if (slot == null)
            return;

        if (!on)
        {
            Transform board = FindBoard(slot);
            if (board == null)
                return;
            Transform existing = board.Find(OverlayName);
            if (existing == null)
                return;
            var overlay = existing.GetComponent<StreamsDashedRectHint>();
            overlay?.Apply(slot, false, color);
            return;
        }

        var created = Ensure(slot);
        if (created == null)
            return;

        created.Apply(slot, true, color);
    }

    static StreamsDashedRectHint Ensure(StreamsUiSlot slot)
    {
        Transform board = FindBoard(slot);
        if (board == null)
            return null;

        Transform existing = board.Find(OverlayName);
        StreamsDashedRectHint overlay = existing != null
            ? existing.GetComponent<StreamsDashedRectHint>()
            : null;

        if (overlay == null)
        {
            var go = existing != null
                ? existing.gameObject
                : new GameObject(OverlayName, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(board, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            overlay = go.GetComponent<StreamsDashedRectHint>();
            if (overlay == null)
                overlay = go.AddComponent<StreamsDashedRectHint>();
            overlay.raycastTarget = false;
            overlay.color = Color.white;
            IsolateRebuilds(go);
            go.transform.SetAsLastSibling();
        }
        else
        {
            IsolateRebuilds(overlay.gameObject);
        }

        return overlay;
    }

    static void IsolateRebuilds(GameObject go)
    {
        if (go == null)
            return;

        var canvas = go.GetComponent<Canvas>();
        if (canvas == null)
            canvas = go.AddComponent<Canvas>();
        canvas.overrideSorting = false;

        var raycaster = go.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            raycaster.enabled = false;
    }

    static Transform FindBoard(StreamsUiSlot slot)
    {
        if (slot == null)
            return null;

        for (Transform t = slot.transform; t != null; t = t.parent)
        {
            if (t.name == "PlayerBoard" || t.name == "AIBoard")
                return t;
        }

        return slot.transform.parent;
    }

    void Apply(StreamsUiSlot slot, bool on, Color color)
    {
        int idx = IndexOf(slot);
        if (on)
        {
            if (idx >= 0)
            {
                Hint hint = _hints[idx];
                hint.color = color;
                _hints[idx] = hint;
            }
            else
            {
                _hints.Add(new Hint { slot = slot, color = color });
            }

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }
        else if (idx >= 0)
        {
            _hints.RemoveAt(idx);
            if (_hints.Count == 0)
            {
                gameObject.SetActive(false);
                return;
            }
        }

        SetVerticesDirty();
    }

    int IndexOf(StreamsUiSlot slot)
    {
        for (int i = 0; i < _hints.Count; i++)
        {
            if (_hints[i].slot == slot)
                return i;
        }

        return -1;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
        _offset = 0f;
        _pulseTime = 0f;
        _introDone = false;
        _appliedAlpha = 0f;
        color = Color.white;
        ApplyRendererAlpha(0f);
    }

    void Update()
    {
        if (_hints.Count == 0)
            return;

        _offset += marchSpeed * Time.deltaTime;
        _pulseTime += Time.unscaledDeltaTime;
        float period = Mathf.Max(0.05f, pulsePeriod);
        float introDuration = period * 0.5f;
        float alpha;

        if (!_introDone)
        {
            float u = Mathf.Clamp01(_pulseTime / introDuration);
            alpha = Mathf.Lerp(0f, pulseMaxAlpha, Mathf.SmoothStep(0f, 1f, u));
            if (u >= 1f)
            {
                _introDone = true;
                _pulseTime = introDuration;
            }
        }
        else
        {
            float wave = (Mathf.Sin(_pulseTime * Mathf.PI * 2f / period - Mathf.PI * 0.5f) + 1f) * 0.5f;
            alpha = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, wave);
        }

        ApplyRendererAlpha(alpha);
        PruneMissingSlots();
        SetVerticesDirty();
    }

    void PruneMissingSlots()
    {
        for (int i = _hints.Count - 1; i >= 0; i--)
        {
            if (_hints[i].slot == null)
                _hints.RemoveAt(i);
        }
    }

    void ApplyRendererAlpha(float alpha)
    {
        _appliedAlpha = alpha;
        canvasRenderer.SetAlpha(alpha);
    }

    protected override void UpdateGeometry()
    {
        base.UpdateGeometry();
        canvasRenderer.SetAlpha(_appliedAlpha);
    }

    protected override void UpdateMaterial()
    {
        base.UpdateMaterial();
        canvasRenderer.SetAlpha(_appliedAlpha);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        for (int i = 0; i < _hints.Count; i++)
        {
            Hint hint = _hints[i];
            if (hint.slot == null)
                continue;

            if (!TryGetHintRect(hint.slot, out Rect r))
                continue;

            Color32 c = hint.color;
            c.a = 255;
            DrawDashedRect(vh, r, c);
        }
    }

    bool TryGetHintRect(StreamsUiSlot slot, out Rect rect)
    {
        rect = default;
        var slotRt = slot.transform as RectTransform;
        if (slotRt == null)
            return false;

        Rect sr = slotRt.rect;
        float sx = sr.width / SpriteSize;
        float sy = sr.height / SpriteSize;
        Vector3 bl = slotRt.TransformPoint(new Vector3(sr.xMin + PadLeft * sx, sr.yMin + PadBottom * sy, 0f));
        Vector3 br = slotRt.TransformPoint(new Vector3(sr.xMax - PadRight * sx, sr.yMin + PadBottom * sy, 0f));
        Vector3 tr = slotRt.TransformPoint(new Vector3(sr.xMax - PadRight * sx, sr.yMax - PadTop * sy, 0f));
        Vector3 tl = slotRt.TransformPoint(new Vector3(sr.xMin + PadLeft * sx, sr.yMax - PadTop * sy, 0f));

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        Encapsulate(ref min, ref max, transform.InverseTransformPoint(bl));
        Encapsulate(ref min, ref max, transform.InverseTransformPoint(br));
        Encapsulate(ref min, ref max, transform.InverseTransformPoint(tr));
        Encapsulate(ref min, ref max, transform.InverseTransformPoint(tl));

        float inset = thickness * 0.5f;
        rect = Rect.MinMaxRect(min.x + inset, min.y + inset, max.x - inset, max.y - inset);
        return rect.width >= 2f && rect.height >= 2f;
    }

    static void Encapsulate(ref Vector2 min, ref Vector2 max, Vector3 p)
    {
        min = Vector2.Min(min, p);
        max = Vector2.Max(max, p);
    }

    void DrawDashedRect(VertexHelper vh, Rect r, Color32 c)
    {
        float dash = Mathf.Max(2f, dashLength);
        float gap = Mathf.Max(1f, gapLength);
        float period = dash + gap;
        float perim = 2f * (r.width + r.height);
        float phase = Mathf.Repeat(_offset, period);

        for (float start = phase - period; start < perim; start += period)
        {
            float a = start;
            float b = start + dash;
            if (b <= 0f || a >= perim)
                continue;

            AddRectSpan(vh, r, Mathf.Max(0f, a), Mathf.Min(perim, b), thickness, c);
        }
    }

    static void AddRectSpan(VertexHelper vh, Rect r, float from, float to, float thickness, Color32 color)
    {
        float w = r.width;
        float h = r.height;
        float[] cuts = { 0f, w, w + h, 2f * w + h, 2f * (w + h) };
        for (int i = 0; i < 4; i++)
        {
            float s = Mathf.Max(from, cuts[i]);
            float e = Mathf.Min(to, cuts[i + 1]);
            if (e - s < 0.5f)
                continue;

            AddDash(vh, PointOnRect(r, s), PointOnRect(r, e), thickness, color);
        }
    }

    static Vector2 PointOnRect(Rect r, float d)
    {
        float w = r.width;
        float h = r.height;
        if (d <= w)
            return new Vector2(r.xMin + d, r.yMax);
        d -= w;
        if (d <= h)
            return new Vector2(r.xMax, r.yMax - d);
        d -= h;
        if (d <= w)
            return new Vector2(r.xMax - d, r.yMin);
        d -= w;
        return new Vector2(r.xMin, r.yMin + d);
    }

    static void AddDash(VertexHelper vh, Vector2 p0, Vector2 p1, float thickness, Color32 color)
    {
        Vector2 dir = p1 - p0;
        float len = dir.magnitude;
        if (len < 0.01f)
            return;

        dir /= len;
        Vector2 n = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
        int i = vh.currentVertCount;
        vh.AddVert(p0 - n, color, Vector2.zero);
        vh.AddVert(p0 + n, color, Vector2.zero);
        vh.AddVert(p1 + n, color, Vector2.zero);
        vh.AddVert(p1 - n, color, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
