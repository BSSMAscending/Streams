using UnityEngine;
using UnityEngine.UI;

/// <summary>화면 전체 기준 좌·우 절반 (960+960 = 1920, 1080).</summary>
public static class StreamsHalfScreenLayout
{
    public static readonly Rect PlayerViewport = new Rect(0f, 0f, 0.5f, 1f);
    public static readonly Rect AiViewport = new Rect(0.5f, 0f, 0.5f, 1f);

    public static void ApplyCameraViewport(Camera camera, Rect viewport)
    {
        if (camera == null)
            return;

        camera.targetTexture = null;
        camera.rect = viewport;
        camera.enabled = true;
    }

    public static void BindWorldCanvas(Canvas canvas, Camera camera)
    {
        if (canvas == null || camera == null)
            return;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
    }

    /// <summary>보드 월드 Canvas(기본 1080x960)가 뷰포트 절반을 채우도록 카메라 거리·FOV를 맞춥니다.</summary>
    public static void FitCameraToBoardCanvas(Camera camera, Transform boardRoot)
    {
        if (camera == null || boardRoot == null)
            return;

        Transform anchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(boardRoot) ?? boardRoot;
        Canvas canvas = anchor.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
            return;

        var rt = canvas.GetComponent<RectTransform>();
        if (rt == null)
            return;

        Vector3 lossy = rt.lossyScale;
        float worldWidth = rt.sizeDelta.x * Mathf.Abs(lossy.x);
        float worldHeight = rt.sizeDelta.y * Mathf.Abs(lossy.y);
        if (worldWidth < 0.01f || worldHeight < 0.01f)
            return;

        Vector3 center = rt.position;
        float halfViewAspect = (Screen.width * 0.5f) / Mathf.Max(1f, Screen.height);
        float contentAspect = worldWidth / worldHeight;

        Vector3 toCam = camera.transform.position - center;
        float dist = toCam.magnitude;
        if (dist < 0.01f)
            return;

        float halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float visibleHeight = 2f * dist * Mathf.Tan(halfFovRad);
        float visibleWidth = visibleHeight * halfViewAspect;

        float scaleW = visibleWidth / worldWidth;
        float scaleH = visibleHeight / worldHeight;
        float fill = Mathf.Min(scaleW, scaleH);
        if (fill <= 0f)
            return;

        float targetFill = 0.92f;
        if (fill < targetFill)
        {
            float zoom = targetFill / fill;
            camera.fieldOfView = Mathf.Clamp(camera.fieldOfView / zoom, 10f, 120f);
        }
        else if (fill > 1.05f)
        {
            float zoom = fill / targetFill;
            camera.fieldOfView = Mathf.Clamp(camera.fieldOfView * zoom, 10f, 120f);
        }

        if (contentAspect > halfViewAspect * 1.1f)
            camera.fieldOfView = Mathf.Clamp(camera.fieldOfView * (contentAspect / halfViewAspect) * 0.85f, 10f, 120f);
    }
}
