using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 오름차순 run을 둥근 꼭짓점 외곽선으로 잇고, 기본 색(#00e7ff)의 보색으로 칠합니다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class StreamsRunOutlineOverlay : MaskableGraphic
{
    const float SpriteSize = 512f;
    const string OverlayName = "RunOutlines";
    const float SpritePadLeft = 104f;
    const float SpritePadRight = 104f;
    const float SpritePadTop = 104f;
    const float SpritePadBottom = 192f;

    [Tooltip("스프라이트 픽셀 기준 외곽선 두께.")]
    public float thickness = 16f;
    [Tooltip("기차 실측 사각형에서 한 변당 더 줄이는 스프라이트 픽셀.")]
    public float sizeShrink = 0f;
    [Tooltip("기차 사각형 모서리를 둥글게 깎는 스프라이트 픽셀.")]
    public float cornerRadius = 32f;
    [Tooltip("3-4, 10-11, 15-16이 오름차순이 아닐 때, 맞닿는 꼭짓점만 더 크게 깎습니다.")]
    public float overlapCornerRadius = 64f;

    readonly List<RunMesh> _runs = new List<RunMesh>();

    struct SlotBox
    {
        public Rect aabb;
        public Vector2 spriteBottomLeft;
        public Vector2 spriteBottomRight;
    }

    struct PathSample
    {
        public Vector2 point;
        public Color color;
    }

    struct RunMesh
    {
        public List<Rect> rects;
        public List<PathSample> path;
        public List<Vector2> overlapCorners;
        public float stroke;
        public float radius;
        public float joinRadius;
    }

    public override Texture mainTexture => s_WhiteTexture;

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
    }

    public static void Refresh(IList<StreamsUiSlot> slots, IList<(int start, int end)> runs)
    {
        var overlay = Ensure(slots);
        if (overlay == null)
            return;
        overlay.Apply(slots, runs);
    }

    static StreamsRunOutlineOverlay Ensure(IList<StreamsUiSlot> slots)
    {
        Transform board = FindBoard(slots);
        if (board == null)
            return null;

        Transform existing = board.Find(OverlayName);
        StreamsRunOutlineOverlay overlay = existing != null
            ? existing.GetComponent<StreamsRunOutlineOverlay>()
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
            overlay = go.GetComponent<StreamsRunOutlineOverlay>();
            if (overlay == null)
                overlay = go.AddComponent<StreamsRunOutlineOverlay>();
            IsolateRebuilds(go);
            go.transform.SetAsLastSibling();
        }
        else
        {
            IsolateRebuilds(overlay.gameObject);
        }

        overlay.raycastTarget = false;
        overlay.color = Color.white;
        overlay.cornerRadius = 32f;
        overlay.overlapCornerRadius = 64f;
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

    static Transform FindBoard(IList<StreamsUiSlot> slots)
    {
        if (slots == null)
            return null;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null)
                continue;
            for (Transform t = slots[i].transform; t != null; t = t.parent)
            {
                if (t.name == "PlayerBoard" || t.name == "AIBoard")
                    return t;
            }
        }

        return null;
    }

    void Apply(IList<StreamsUiSlot> slots, IList<(int start, int end)> runs)
    {
        _runs.Clear();
        if (slots == null || runs == null)
        {
            SetVerticesDirty();
            return;
        }

        var allBoxes = new SlotBox[slots.Count];
        var hasBox = new bool[slots.Count];
        var pxLocal = new float[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null || !slots[i].isFilled)
                continue;
            if (!TryGetTrainBox(slots[i], out SlotBox box, out float pxToLocal))
                continue;
            allBoxes[i] = box;
            hasBox[i] = true;
            pxLocal[i] = pxToLocal;
        }

        for (int r = 0; r < runs.Count; r++)
        {
            int start = runs[r].start;
            int end = runs[r].end;
            if (start > end)
                continue;

            var rects = new List<Rect>();
            var squares = new List<(int index, Rect square)>();
            float strokeSum = 0f;
            int strokeCount = 0;
            for (int i = start; i <= end && i < slots.Count; i++)
            {
                if (!hasBox[i])
                    continue;
                rects.Add(allBoxes[i].aabb);
                squares.Add((i, allBoxes[i].aabb));
                strokeSum += thickness * pxLocal[i];
                strokeCount++;
            }

            AddRunJoins(rects, squares);

            if (rects.Count == 0)
                continue;

            float localStroke = strokeCount > 0 ? strokeSum / strokeCount : thickness;
            float px = localStroke / Mathf.Max(0.0001f, thickness);
            _runs.Add(new RunMesh
            {
                rects = rects,
                path = BuildColorPath(squares, StreamsAscendingRuns.RunColor(r)),
                overlapCorners = OverlapCorners(allBoxes, hasBox, squares),
                stroke = localStroke,
                radius = Mathf.Max(0f, overlapCornerRadius) * px,
                joinRadius = Mathf.Max(0f, cornerRadius) * px
            });
        }

        SetVerticesDirty();
    }

    bool TryGetTrainBox(StreamsUiSlot slot, out SlotBox box, out float spritePxToOverlay)
    {
        box = default;
        spritePxToOverlay = 1f;
        var slotRt = slot != null ? slot.transform as RectTransform : null;
        if (slotRt == null)
            return false;

        Rect slotRect = slotRt.rect;
        float sx = slotRect.width / SpriteSize;
        float sy = slotRect.height / SpriteSize;
        float left = SpritePadLeft * sx;
        float right = SpritePadRight * sx;
        float top = SpritePadTop * sy;
        float bottom = SpritePadBottom * sy;
        Vector3[] corners =
        {
            SlotLocalToWorld(slotRt, new Vector3(slotRect.xMin + left, slotRect.yMin + bottom, 0f)),
            SlotLocalToWorld(slotRt, new Vector3(slotRect.xMax - right, slotRect.yMin + bottom, 0f)),
            SlotLocalToWorld(slotRt, new Vector3(slotRect.xMax - right, slotRect.yMax - top, 0f)),
            SlotLocalToWorld(slotRt, new Vector3(slotRect.xMin + left, slotRect.yMax - top, 0f))
        };

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        var overlay = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = transform.InverseTransformPoint(corners[i]);
            overlay[i] = local;
            min = Vector2.Min(min, overlay[i]);
            max = Vector2.Max(max, overlay[i]);
        }

        if (max.x - min.x < 2f || max.y - min.y < 2f)
            return false;

        Vector3 overlayDelta = transform.InverseTransformVector(
            SlotLocalToWorld(slotRt, Vector3.right) - SlotLocalToWorld(slotRt, Vector3.zero));
        spritePxToOverlay = overlayDelta.magnitude * (slotRect.width / SpriteSize);
        box.aabb = InsetContent(Rect.MinMaxRect(min.x, min.y, max.x, max.y), spritePxToOverlay);
        box.spriteBottomLeft = overlay[0];
        box.spriteBottomRight = overlay[1];
        return true;
    }

    static Vector3 SlotLocalToWorld(RectTransform slot, Vector3 localPoint)
    {
        Vector3 rest = RestScaleOf(slot);
        Vector3 live = slot.localScale;
        float sx = live.x == 0f ? 1f : rest.x / live.x;
        float sy = live.y == 0f ? 1f : rest.y / live.y;
        float sz = live.z == 0f ? 1f : rest.z / live.z;
        return slot.TransformPoint(new Vector3(localPoint.x * sx, localPoint.y * sy, localPoint.z * sz));
    }

    static Vector3 RestScaleOf(Transform slot)
    {
        var press = slot.GetComponent<UiButtonPressScale>();
        return press != null ? press.RestLocalScale : slot.localScale;
    }

    Rect InsetContent(Rect train, float spritePxToOverlay)
    {
        float inset = Mathf.Max(0f, sizeShrink) * 0.5f * Mathf.Max(0.0001f, spritePxToOverlay);
        float xMin = train.xMin + inset;
        float xMax = train.xMax - inset;
        float yMin = train.yMin + inset;
        float yMax = train.yMax - inset;
        if (xMax - xMin < 2f || yMax - yMin < 2f)
            return train;
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    static bool IsVerticalSlot(int idx) => (idx >= 0 && idx <= 4) || (idx >= 12 && idx <= 16);

    static void AddRunJoins(List<Rect> rects, List<(int index, Rect square)> squares)
    {
        for (int i = 0; i < squares.Count - 1; i++)
        {
            Rect a = squares[i].square;
            Rect b = squares[i + 1].square;
            float dx = b.center.x - a.center.x;
            float dy = b.center.y - a.center.y;
            float ax = Mathf.Abs(dx);
            float ay = Mathf.Abs(dy);

            if (ay < ax * 0.35f || ax < ay * 0.35f)
            {
                rects.Add(Rect.MinMaxRect(
                    Mathf.Min(a.xMin, b.xMin),
                    Mathf.Min(a.yMin, b.yMin),
                    Mathf.Max(a.xMax, b.xMax),
                    Mathf.Max(a.yMax, b.yMax)));
                continue;
            }

            if (IsVerticalSlot(squares[i].index))
            {
                rects.Add(Rect.MinMaxRect(a.xMin, Mathf.Min(a.yMin, b.yMin), a.xMax, Mathf.Max(a.yMax, b.yMax)));
                rects.Add(Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), b.yMin, Mathf.Max(a.xMax, b.xMax), b.yMax));
            }
            else
            {
                rects.Add(Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), a.yMin, Mathf.Max(a.xMax, b.xMax), a.yMax));
                rects.Add(Rect.MinMaxRect(b.xMin, Mathf.Min(a.yMin, b.yMin), b.xMax, Mathf.Max(a.yMax, b.yMax)));
            }
        }
    }

    static List<PathSample> BuildColorPath(List<(int index, Rect square)> squares, Color color)
    {
        var path = new List<PathSample>(squares.Count * 2);
        if (squares.Count == 0)
            return path;

        path.Add(Sample(squares[0].square.center, color));
        for (int i = 0; i < squares.Count - 1; i++)
        {
            Rect a = squares[i].square;
            Rect b = squares[i + 1].square;
            float dx = b.center.x - a.center.x;
            float dy = b.center.y - a.center.y;
            float ax = Mathf.Abs(dx);
            float ay = Mathf.Abs(dy);

            if (ay >= ax * 0.35f && ax >= ay * 0.35f)
            {
                Vector2 elbow = IsVerticalSlot(squares[i].index)
                    ? new Vector2(a.center.x, b.center.y)
                    : new Vector2(b.center.x, a.center.y);
                path.Add(Sample(elbow, color));
            }

            path.Add(Sample(b.center, color));
        }

        return path;
    }

    static readonly int[] ElbowPairs = { 3, 10, 15 };

    static List<Vector2> OverlapCorners(SlotBox[] allBoxes, bool[] hasBox, List<(int index, Rect square)> squares)
    {
        var corners = new List<Vector2>(4);
        if (allBoxes == null || hasBox == null || squares == null || squares.Count == 0)
            return corners;

        var inRun = new bool[allBoxes.Length];
        for (int i = 0; i < squares.Count; i++)
        {
            int idx = squares[i].index;
            if (idx >= 0 && idx < inRun.Length)
                inRun[idx] = true;
        }

        for (int p = 0; p < ElbowPairs.Length; p++)
        {
            int a = ElbowPairs[p];
            int b = a + 1;
            if (a < 0 || b >= allBoxes.Length)
                continue;
            if (!hasBox[a] || !hasBox[b])
                continue;
            if (inRun[a] && inRun[b])
                continue;
            if (!inRun[a] && !inRun[b])
                continue;

            if (inRun[a])
                corners.Add(FacingBottomCorner(allBoxes[a], allBoxes[b].aabb.center));
            if (inRun[b])
                corners.Add(FacingBottomCorner(allBoxes[b], allBoxes[a].aabb.center));
        }

        return corners;
    }

    static Vector2 FacingBottomCorner(SlotBox box, Vector2 toward)
    {
        float dLeft = (box.spriteBottomLeft - toward).sqrMagnitude;
        float dRight = (box.spriteBottomRight - toward).sqrMagnitude;
        return dLeft <= dRight ? box.spriteBottomLeft : box.spriteBottomRight;
    }

    static PathSample Sample(Vector2 point, Color color) => new PathSample { point = point, color = color };

    static Color ColorAtPoint(List<PathSample> path, Vector2 point)
    {
        if (path == null || path.Count == 0)
            return Color.red;
        if (path.Count == 1)
            return path[0].color;

        float best = float.MaxValue;
        Color bestColor = path[0].color;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 a = path[i].point;
            Vector2 b = path[i + 1].point;
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, ab) / len2);
            Vector2 proj = a + ab * t;
            float d = (point - proj).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestColor = Color.Lerp(path[i].color, path[i + 1].color, t);
            }
        }

        return bestColor;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        for (int i = 0; i < _runs.Count; i++)
            DrawRun(vh, _runs[i]);
    }

    void DrawRun(VertexHelper vh, RunMesh run)
    {
        if (run.rects == null || run.rects.Count == 0)
            return;

        float stroke = Mathf.Max(2f, run.stroke);
        CollectCells(run.rects, out float[] xs, out float[] ys, out bool[,] inside);
        List<List<Vector2>> loops = ExtractLoops(xs, ys, inside);

        for (int i = 0; i < loops.Count; i++)
        {
            List<Vector2> loop = FilletLoop(loops[i], run.joinRadius, run.radius, run.overlapCorners);
            if (loop.Count < 2)
                continue;

            for (int p = 0; p < loop.Count; p++)
            {
                Vector2 a = loop[p];
                Vector2 b = loop[(p + 1) % loop.Count];
                Color ca = ColorAtPoint(run.path, a);
                Color cb = ColorAtPoint(run.path, b);
                AddStroke(vh, a, b, stroke, ca, cb);
                AddDisc(vh, a, stroke * 0.5f, ca);
            }
        }
    }

    static void CollectCells(List<Rect> rects, out float[] xs, out float[] ys, out bool[,] inside)
    {
        var xSet = new SortedSet<float>();
        var ySet = new SortedSet<float>();
        for (int i = 0; i < rects.Count; i++)
        {
            Rect r = rects[i];
            xSet.Add(r.xMin);
            xSet.Add(r.xMax);
            ySet.Add(r.yMin);
            ySet.Add(r.yMax);
        }

        xs = MergeCoords(xSet);
        ys = MergeCoords(ySet);

        int nx = xs.Length - 1;
        int ny = ys.Length - 1;
        inside = new bool[Mathf.Max(0, nx), Mathf.Max(0, ny)];
        for (int x = 0; x < nx; x++)
        {
            for (int y = 0; y < ny; y++)
            {
                var cell = Rect.MinMaxRect(xs[x], ys[y], xs[x + 1], ys[y + 1]);
                for (int i = 0; i < rects.Count; i++)
                {
                    if (OverlapsInclusive(rects[i], cell))
                    {
                        inside[x, y] = true;
                        break;
                    }
                }
            }
        }

        for (int x = 0; x < nx; x++)
        {
            for (int y = 0; y < ny; y++)
            {
                if (inside[x, y])
                    continue;
                bool horzGap = x > 0 && x < nx - 1 && inside[x - 1, y] && inside[x + 1, y];
                bool vertGap = y > 0 && y < ny - 1 && inside[x, y - 1] && inside[x, y + 1];
                if (horzGap || vertGap)
                    inside[x, y] = true;
            }
        }
    }

    static float[] MergeCoords(SortedSet<float> values)
    {
        const float epsilon = 2f;
        var merged = new List<float>(values.Count);
        foreach (float v in values)
        {
            if (merged.Count == 0 || v - merged[merged.Count - 1] >= epsilon)
                merged.Add(v);
        }

        return merged.ToArray();
    }

    static bool OverlapsInclusive(Rect a, Rect b)
    {
        return a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;
    }

    static List<List<Vector2>> ExtractLoops(float[] xs, float[] ys, bool[,] inside)
    {
        var loops = new List<List<Vector2>>();
        int nx = xs.Length - 1;
        int ny = ys.Length - 1;
        var edges = new List<(Vector2 a, Vector2 b)>();

        for (int x = 0; x < nx; x++)
        {
            for (int y = 0; y < ny; y++)
            {
                if (!inside[x, y])
                    continue;

                if (x == 0 || !inside[x - 1, y])
                    edges.Add((new Vector2(xs[x], ys[y + 1]), new Vector2(xs[x], ys[y])));
                if (x == nx - 1 || !inside[x + 1, y])
                    edges.Add((new Vector2(xs[x + 1], ys[y]), new Vector2(xs[x + 1], ys[y + 1])));
                if (y == 0 || !inside[x, y - 1])
                    edges.Add((new Vector2(xs[x], ys[y]), new Vector2(xs[x + 1], ys[y])));
                if (y == ny - 1 || !inside[x, y + 1])
                    edges.Add((new Vector2(xs[x + 1], ys[y + 1]), new Vector2(xs[x], ys[y + 1])));
            }
        }

        var used = new bool[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i])
                continue;

            var loop = new List<Vector2> { edges[i].a, edges[i].b };
            used[i] = true;
            while (true)
            {
                Vector2 tail = loop[loop.Count - 1];
                int next = -1;
                for (int j = 0; j < edges.Count; j++)
                {
                    if (used[j])
                        continue;
                    if ((edges[j].a - tail).sqrMagnitude > 0.01f)
                        continue;
                    next = j;
                    break;
                }

                if (next < 0)
                    break;

                used[next] = true;
                loop.Add(edges[next].b);
                if ((edges[next].b - loop[0]).sqrMagnitude <= 0.01f)
                    break;
            }

            if (loop.Count >= 2 && (loop[0] - loop[loop.Count - 1]).sqrMagnitude <= 0.01f)
                loop.RemoveAt(loop.Count - 1);
            if (loop.Count >= 3)
                loops.Add(CollapseColinear(loop));
        }

        return loops;
    }

    static List<Vector2> CollapseColinear(List<Vector2> loop)
    {
        if (loop.Count < 3)
            return loop;

        var pts = new List<Vector2>(loop.Count);
        int n = loop.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = loop[(i - 1 + n) % n];
            Vector2 curr = loop[i];
            Vector2 next = loop[(i + 1) % n];
            Vector2 d0 = curr - prev;
            Vector2 d1 = next - curr;
            if (d0.sqrMagnitude < 0.0001f || d1.sqrMagnitude < 0.0001f)
                continue;
            float cross = d0.x * d1.y - d0.y * d1.x;
            if (Mathf.Abs(cross) < 0.01f * d0.magnitude * d1.magnitude)
                continue;
            pts.Add(curr);
        }

        return pts.Count >= 3 ? pts : loop;
    }

    static List<Vector2> FilletLoop(List<Vector2> loop, float joinRadius, float endRadius, List<Vector2> endCorners)
    {
        if (loop == null || loop.Count < 3)
            return loop ?? new List<Vector2>();

        const int ArcSegments = 7;
        int n = loop.Count;
        HashSet<int> overlapVertices = OverlapVertexIndices(loop, endCorners);
        var pts = new List<Vector2>(n * (ArcSegments + 1));
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = loop[(i - 1 + n) % n];
            Vector2 curr = loop[i];
            Vector2 next = loop[(i + 1) % n];
            Vector2 d0 = curr - prev;
            Vector2 d1 = next - curr;
            float len0 = d0.magnitude;
            float len1 = d1.magnitude;
            if (len0 < 0.01f || len1 < 0.01f)
            {
                pts.Add(curr);
                continue;
            }

            d0 /= len0;
            d1 /= len1;
            float cross = d0.x * d1.y - d0.y * d1.x;
            if (Mathf.Abs(cross) < 0.001f)
            {
                pts.Add(curr);
                continue;
            }

            float radius = IsOverlapVertex(i, overlapVertices) ? endRadius : joinRadius;
            float r = Mathf.Min(Mathf.Max(0f, radius), len0 * 0.45f, len1 * 0.45f);
            if (r < 1f)
            {
                pts.Add(curr);
                continue;
            }

            Vector2 p0 = curr - d0 * r;
            Vector2 p1 = curr + d1 * r;
            Vector2 inward = cross > 0f ? new Vector2(-d0.y, d0.x) : new Vector2(d0.y, -d0.x);
            Vector2 center = p0 + inward * r;
            float a0 = Mathf.Atan2(p0.y - center.y, p0.x - center.x);
            float a1 = Mathf.Atan2(p1.y - center.y, p1.x - center.x);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            pts.Add(p0);
            for (int s = 1; s < ArcSegments; s++)
            {
                float t = s / (float)ArcSegments;
                float ang = a0 + delta * t;
                pts.Add(center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
            }
            pts.Add(p1);
        }

        return pts;
    }

    static HashSet<int> OverlapVertexIndices(List<Vector2> loop, List<Vector2> overlapCorners)
    {
        var marked = new HashSet<int>();
        if (loop == null || overlapCorners == null || loop.Count == 0)
            return marked;

        const float maxDistSq = 24f * 24f;
        for (int c = 0; c < overlapCorners.Count; c++)
        {
            int best = -1;
            float bestDist = maxDistSq;
            for (int i = 0; i < loop.Count; i++)
            {
                float d = (loop[i] - overlapCorners[c]).sqrMagnitude;
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            if (best >= 0)
                marked.Add(best);
        }

        return marked;
    }

    static bool IsOverlapVertex(int index, HashSet<int> overlapVertices)
    {
        return overlapVertices != null && overlapVertices.Contains(index);
    }

    static void AddStroke(VertexHelper vh, Vector2 p0, Vector2 p1, float thickness, Color c0, Color c1)
    {
        Vector2 dir = p1 - p0;
        float len = dir.magnitude;
        if (len < 0.01f)
            return;

        dir /= len;
        Vector2 n = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
        AddQuad(vh, p0 - n, p1 - n, p1 + n, p0 + n, c0, c1, c1, c0);
    }

    static void AddDisc(VertexHelper vh, Vector2 center, float radius, Color color)
    {
        const int Segments = 12;
        if (radius < 0.5f)
            return;

        int i = vh.currentVertCount;
        Color32 c = color;
        vh.AddVert(center, c, Vector2.zero);
        for (int s = 0; s < Segments; s++)
        {
            float ang = s / (float)Segments * Mathf.PI * 2f;
            vh.AddVert(center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius, c, Vector2.zero);
        }

        for (int s = 0; s < Segments; s++)
            vh.AddTriangle(i, i + 1 + s, i + 1 + (s + 1) % Segments);
    }

    static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, ca, Vector2.zero);
        vh.AddVert(b, cb, Vector2.zero);
        vh.AddVert(c, cc, Vector2.zero);
        vh.AddVert(d, cd, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
