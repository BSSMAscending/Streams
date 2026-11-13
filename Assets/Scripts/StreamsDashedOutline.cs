using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스포트라이트 밝은 영역과 어두운 영역 경계. 작은 점선이 그 가운데를 따라 흐릅니다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StreamsDashedOutline : MaskableGraphic
{
    const float Thickness = 5f;
    const float DashLength = 16f;
    const float GapLength = 9f;
    const float MarchSpeed = 46f;
    const float PulsePeriod = 2.2f;
    const float PulseMinAlpha = 0.4f;
    const float PulseMaxAlpha = 0.95f;

    readonly List<Rect> _rects = new List<Rect>();
    readonly List<Vector2[]> _loops = new List<Vector2[]>();
    float _fit = 1f;
    float _offset;
    float _pulseTime;
    bool _introDone;
    float _appliedAlpha = 1f;

    public override Texture mainTexture => s_WhiteTexture;

    public void Show(IList<Rect> rects, float fit)
    {
        fit = Mathf.Max(0.35f, fit);
        bool changed = !Mathf.Approximately(_fit, fit) || !SameRects(rects);
        _fit = fit;
        if (changed)
        {
            _rects.Clear();
            if (rects != null)
            {
                for (int i = 0; i < rects.Count; i++)
                {
                    Rect rect = rects[i];
                    if (rect.width < 1f || rect.height < 1f || float.IsNaN(rect.xMin) || float.IsNaN(rect.yMin))
                        continue;
                    _rects.Add(rect);
                }
            }

            RebuildLoops();
        }

        if (_loops.Count == 0)
        {
            Hide();
            return;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        else if (changed)
            SetVerticesDirty();
    }

    public void Hide()
    {
        _rects.Clear();
        _loops.Clear();
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
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
        if (_loops.Count == 0)
            return;

        _offset += MarchSpeed * _fit * Time.unscaledDeltaTime;
        _pulseTime += Time.unscaledDeltaTime;
        float period = Mathf.Max(0.05f, PulsePeriod);
        float introDuration = period * 0.5f;
        float alpha;

        if (!_introDone)
        {
            float u = Mathf.Clamp01(_pulseTime / introDuration);
            alpha = Mathf.Lerp(0f, PulseMaxAlpha, Mathf.SmoothStep(0f, 1f, u));
            if (u >= 1f)
            {
                _introDone = true;
                _pulseTime = introDuration;
            }
        }
        else
        {
            float wave = (Mathf.Sin(_pulseTime * Mathf.PI * 2f / period - Mathf.PI * 0.5f) + 1f) * 0.5f;
            alpha = Mathf.Lerp(PulseMinAlpha, PulseMaxAlpha, wave);
        }

        ApplyRendererAlpha(alpha);
        SetVerticesDirty();
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
        if (_loops.Count == 0)
            return;

        float thickness = Thickness * _fit;
        float dash = Mathf.Max(2f, DashLength * _fit);
        float gap = Mathf.Max(1f, GapLength * _fit);
        var color = new Color32(0, 0, 0, 255);
        for (int i = 0; i < _loops.Count; i++)
            DrawDashedLoop(vh, _loops[i], thickness, dash, gap, color);
    }

    bool SameRects(IList<Rect> rects)
    {
        int count = 0;
        if (rects != null)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                Rect rect = rects[i];
                if (rect.width < 1f || rect.height < 1f || float.IsNaN(rect.xMin) || float.IsNaN(rect.yMin))
                    continue;
                if (count >= _rects.Count || _rects[count] != rect)
                    return false;
                count++;
            }
        }

        return count == _rects.Count;
    }

    void RebuildLoops()
    {
        _loops.Clear();
        if (_rects.Count == 0)
            return;

        var xs = new List<float>();
        var ys = new List<float>();
        for (int i = 0; i < _rects.Count; i++)
        {
            AddCoord(xs, _rects[i].xMin);
            AddCoord(xs, _rects[i].xMax);
            AddCoord(ys, _rects[i].yMin);
            AddCoord(ys, _rects[i].yMax);
        }

        xs.Sort();
        ys.Sort();
        if (xs.Count < 2 || ys.Count < 2)
            return;

        int nx = xs.Count - 1;
        int ny = ys.Count - 1;
        var fill = new bool[nx * ny];
        for (int ix = 0; ix < nx; ix++)
        {
            float x0 = xs[ix];
            float x1 = xs[ix + 1];
            if (x1 - x0 < 0.05f)
                continue;
            for (int iy = 0; iy < ny; iy++)
            {
                float y0 = ys[iy];
                float y1 = ys[iy + 1];
                if (y1 - y0 < 0.05f)
                    continue;
                fill[ix * ny + iy] = ContainsAny((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            }
        }

        var edges = new List<Edge>();
        for (int y = 0; y <= ny; y++)
        {
            for (int x = 0; x < nx; x++)
            {
                if (xs[x + 1] - xs[x] < 0.05f)
                    continue;
                bool above = y < ny && fill[x * ny + y];
                bool below = y > 0 && fill[x * ny + (y - 1)];
                if (above == below)
                    continue;
                if (above)
                    edges.Add(new Edge(x, y, x + 1, y));
                else
                    edges.Add(new Edge(x + 1, y, x, y));
            }
        }

        for (int x = 0; x <= nx; x++)
        {
            for (int y = 0; y < ny; y++)
            {
                if (ys[y + 1] - ys[y] < 0.05f)
                    continue;
                bool right = x < nx && fill[x * ny + y];
                bool left = x > 0 && fill[(x - 1) * ny + y];
                if (right == left)
                    continue;
                if (right)
                    edges.Add(new Edge(x, y + 1, x, y));
                else
                    edges.Add(new Edge(x, y, x, y + 1));
            }
        }

        var outgoing = new Dictionary<long, List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            long key = Key(edges[i].ax, edges[i].ay);
            if (!outgoing.TryGetValue(key, out List<int> list))
            {
                list = new List<int>(2);
                outgoing.Add(key, list);
            }

            list.Add(i);
        }

        var used = new bool[edges.Count];
        var loop = new List<Vector2>();
        float inset = 0f;
        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i])
                continue;

            loop.Clear();
            int edgeIndex = i;
            int startX = edges[edgeIndex].ax;
            int startY = edges[edgeIndex].ay;
            bool closed = false;
            int guard = 0;
            while (edgeIndex >= 0 && !used[edgeIndex] && guard++ <= edges.Count)
            {
                used[edgeIndex] = true;
                Edge edge = edges[edgeIndex];
                loop.Add(new Vector2(xs[edge.ax], ys[edge.ay]));
                if (edge.bx == startX && edge.by == startY)
                {
                    closed = true;
                    break;
                }

                edgeIndex = NextEdge(edges, outgoing, used, xs, ys, edge);
            }

            if (!closed || loop.Count < 3)
                continue;

            RemoveCollinear(loop);
            if (loop.Count < 3)
                continue;

            List<Vector2> shaped = TryInset(loop, inset);
            _loops.Add(shaped.ToArray());
        }
    }

    bool ContainsAny(float x, float y)
    {
        for (int i = 0; i < _rects.Count; i++)
        {
            Rect rect = _rects[i];
            if (x >= rect.xMin && x < rect.xMax && y >= rect.yMin && y < rect.yMax)
                return true;
        }

        return false;
    }

    static int NextEdge(
        List<Edge> edges,
        Dictionary<long, List<int>> outgoing,
        bool[] used,
        List<float> xs,
        List<float> ys,
        Edge incoming)
    {
        if (!outgoing.TryGetValue(Key(incoming.bx, incoming.by), out List<int> list))
            return -1;

        float dirX = xs[incoming.bx] - xs[incoming.ax];
        float dirY = ys[incoming.by] - ys[incoming.ay];
        int best = -1;
        float bestAngle = -100f;
        for (int i = 0; i < list.Count; i++)
        {
            int index = list[i];
            if (used[index])
                continue;

            Edge candidate = edges[index];
            if (candidate.bx == incoming.ax && candidate.by == incoming.ay && list.Count > 1)
                continue;

            float outX = xs[candidate.bx] - xs[candidate.ax];
            float outY = ys[candidate.by] - ys[candidate.ay];
            float angle = Mathf.Atan2(dirX * outY - dirY * outX, dirX * outX + dirY * outY);
            if (angle > bestAngle)
            {
                bestAngle = angle;
                best = index;
            }
        }

        return best;
    }

    static void RemoveCollinear(List<Vector2> loop)
    {
        int guard = 0;
        int index = 0;
        while (index < loop.Count && loop.Count >= 3 && guard++ < 10000)
        {
            Vector2 prev = loop[(index + loop.Count - 1) % loop.Count];
            Vector2 curr = loop[index];
            Vector2 next = loop[(index + 1) % loop.Count];
            Vector2 inDir = curr - prev;
            Vector2 outDir = next - curr;
            float cross = inDir.x * outDir.y - inDir.y * outDir.x;
            if (inDir.sqrMagnitude < 0.01f || (Mathf.Abs(cross) < 0.05f && Vector2.Dot(inDir, outDir) > 0f))
            {
                loop.RemoveAt(index);
                continue;
            }

            index++;
        }
    }

    static List<Vector2> TryInset(List<Vector2> loop, float inset)
    {
        if (Mathf.Abs(inset) <= 0.01f)
            return loop;

        var full = Offset(loop, inset);
        if (KeepsDirection(loop, full))
            return full;

        var half = Offset(loop, inset * 0.5f);
        if (KeepsDirection(loop, half))
            return half;

        return loop;
    }

    static List<Vector2> Offset(List<Vector2> loop, float inset)
    {
        int count = loop.Count;
        var result = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
        {
            Vector2 prev = loop[(i + count - 1) % count];
            Vector2 curr = loop[i];
            Vector2 next = loop[(i + 1) % count];
            Vector2 inDir = (curr - prev).normalized;
            Vector2 outDir = (next - curr).normalized;
            Vector2 inNormal = new Vector2(-inDir.y, inDir.x);
            Vector2 outNormal = new Vector2(-outDir.y, outDir.x);
            Vector2 miter = inNormal + outNormal;
            float denom = Vector2.Dot(miter, inNormal);
            Vector2 delta = Mathf.Abs(denom) < 0.001f
                ? inNormal * inset
                : miter * (inset / denom);
            float limit = Mathf.Abs(inset) * 4f;
            if (delta.sqrMagnitude > limit * limit)
                delta = delta.normalized * limit;
            result.Add(curr + delta);
        }

        return result;
    }

    static bool KeepsDirection(List<Vector2> source, List<Vector2> offset)
    {
        if (source == null || offset == null || source.Count != offset.Count || source.Count < 3)
            return false;

        int count = source.Count;
        for (int i = 0; i < count; i++)
        {
            Vector2 before = source[(i + 1) % count] - source[i];
            Vector2 after = offset[(i + 1) % count] - offset[i];
            if (before.sqrMagnitude < 0.01f || after.sqrMagnitude < 0.01f)
                return false;
            if (Vector2.Dot(before, after) <= 0f)
                return false;
        }

        return true;
    }

    void DrawDashedLoop(VertexHelper vh, Vector2[] poly, float thickness, float dash, float gap, Color32 color)
    {
        int count = poly.Length;
        if (count < 2)
            return;

        var lengthAt = new float[count + 1];
        for (int i = 0; i < count; i++)
            lengthAt[i + 1] = lengthAt[i] + Vector2.Distance(poly[i], poly[(i + 1) % count]);

        float perimeter = lengthAt[count];
        if (perimeter < 1f)
            return;

        float period = dash + gap;
        float phase = Mathf.Repeat(_offset, period);
        for (float start = phase - period; start < perimeter; start += period)
        {
            float from = start;
            float to = start + dash;
            if (to <= 0f || from >= perimeter)
                continue;

            DrawSpan(vh, poly, lengthAt, Mathf.Max(0f, from), Mathf.Min(perimeter, to), thickness, color);
        }
    }

    static void DrawSpan(
        VertexHelper vh,
        Vector2[] poly,
        float[] lengthAt,
        float from,
        float to,
        float thickness,
        Color32 color)
    {
        int count = poly.Length;
        for (int i = 0; i < count; i++)
        {
            float start = lengthAt[i];
            float end = lengthAt[i + 1];
            float a = Mathf.Max(from, start);
            float b = Mathf.Min(to, end);
            if (b - a < 0.5f)
                continue;

            float length = end - start;
            if (length < 0.01f)
                continue;

            float u0 = (a - start) / length;
            float u1 = (b - start) / length;
            Vector2 p0 = poly[i];
            Vector2 p1 = poly[(i + 1) % count];
            AddDash(vh, Vector2.Lerp(p0, p1, u0), Vector2.Lerp(p0, p1, u1), thickness, color);
        }
    }

    static void AddDash(VertexHelper vh, Vector2 p0, Vector2 p1, float thickness, Color32 color)
    {
        Vector2 dir = p1 - p0;
        float length = dir.magnitude;
        if (length < 0.01f)
            return;

        dir /= length;
        Vector2 normal = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
        int index = vh.currentVertCount;
        vh.AddVert(p0 - normal, color, Vector2.zero);
        vh.AddVert(p0 + normal, color, Vector2.zero);
        vh.AddVert(p1 + normal, color, Vector2.zero);
        vh.AddVert(p1 - normal, color, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }

    static void AddCoord(List<float> coords, float value)
    {
        for (int i = 0; i < coords.Count; i++)
        {
            if (Mathf.Abs(coords[i] - value) < 0.05f)
                return;
        }

        coords.Add(value);
    }

    static long Key(int x, int y) => ((long)x << 32) | (uint)y;

    readonly struct Edge
    {
        public readonly int ax;
        public readonly int ay;
        public readonly int bx;
        public readonly int by;

        public Edge(int ax, int ay, int bx, int by)
        {
            this.ax = ax;
            this.ay = ay;
            this.bx = bx;
            this.by = by;
        }
    }
}
