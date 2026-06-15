using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 보드 카메라 포즈. <c>GameBoard_0</c>~<c>GameBoard_3</c> 부모가 있으면 그 트랜스폼 기준
/// 로컬 위치·회전(기본: 위치 0,0,-210 / 회전 90,0,0)을 월드 좌표로 변환합니다.
/// 없으면 슬롯·렌더 바운드 기반 탑다운으로 대체합니다.
/// </summary>
public static class StreamsBoardCameraPose
{
    const string GameBoard0 = "GameBoard_0";
    const string GameBoard1 = "GameBoard_1";
    const string GameBoard2 = "GameBoard_2";
    const string GameBoard3 = "GameBoard_3";

    /// <summary>
    /// <paramref name="start"/>에서 부모를 따라 올라가며 GameBoard_0~3 중 하나를 찾습니다.
    /// </summary>
    public static Transform TryFindGameBoardAnchor(Transform start)
    {
        for (Transform p = start; p != null; p = p.parent)
        {
            var n = p.name;
            if (n == GameBoard0 || n == GameBoard1 || n == GameBoard2 || n == GameBoard3)
                return p;
        }

        return null;
    }

    /// <summary>
    /// GameBoard 앵커가 있으면 그 기준 로컬 포즈, 없으면 기존 탑다운 계산.
    /// </summary>
    public static void GetCameraPose(
        Transform boardRoot,
        IList<Slot3D> slots,
        Vector3 cameraLocalPosition,
        Vector3 cameraLocalEuler,
        float fallbackMinHeight,
        float fallbackExtentScale,
        out Vector3 worldPosition,
        out Quaternion worldRotation)
    {
        Transform anchor = TryFindGameBoardAnchor(boardRoot);
        if (anchor != null)
        {
            worldPosition = anchor.TransformPoint(cameraLocalPosition);
            worldRotation = anchor.rotation * Quaternion.Euler(cameraLocalEuler);
            return;
        }

        GetTopDownPose(boardRoot, slots, fallbackMinHeight, fallbackExtentScale, out worldPosition, out worldRotation);
    }

    public static Bounds GetBoardWorldBounds(Transform boardRoot)
    {
        if (boardRoot == null)
            return new Bounds(Vector3.zero, Vector3.one * 4f);

        var renderers = boardRoot.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return new Bounds(boardRoot.position, Vector3.one * 4f);

        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                b.Encapsulate(renderers[i].bounds);
        }

        return b;
    }

    /// <summary>
    /// <paramref name="slots"/>가 있으면 슬롯 월드 좌표로 중심·판 법선·크기를 구합니다.
    /// 없으면 <paramref name="boardRoot"/> 하위 Renderer 바운드로 대체합니다.
    /// </summary>
    public static void GetTopDownPose(
        Transform boardRoot,
        IList<Slot3D> slots,
        float minHeightAboveBoard,
        float extentScale,
        out Vector3 worldPosition,
        out Quaternion worldRotation)
    {
        if (boardRoot == null)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.Euler(90f, 0f, 0f);
            return;
        }

        if (TryComputeFromSlots(slots, boardRoot, minHeightAboveBoard, extentScale, out worldPosition, out worldRotation))
            return;

        Bounds b = GetBoardWorldBounds(boardRoot);
        Vector3 center = b.center;
        float half = Mathf.Max(Mathf.Max(b.extents.x, b.extents.z), 0.5f);
        float height = Mathf.Max(minHeightAboveBoard, half * extentScale);
        float yaw = boardRoot.rotation.eulerAngles.y;

        worldRotation = Quaternion.Euler(90f, yaw, 0f);
        worldPosition = center + boardRoot.up * height;
    }

    static bool TryComputeFromSlots(
        IList<Slot3D> slots,
        Transform boardRoot,
        float minHeightAboveBoard,
        float extentScale,
        out Vector3 worldPosition,
        out Quaternion worldRotation)
    {
        worldPosition = default;
        worldRotation = default;

        if (slots == null || slots.Count == 0)
            return false;

        Vector3 sum = Vector3.zero;
        Vector3 upSum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null) continue;
            sum += slots[i].transform.position;
            upSum += slots[i].transform.up;
            count++;
        }

        if (count == 0)
            return false;

        Vector3 center = sum / count;
        Vector3 n = (upSum / count).normalized;
        if (n.sqrMagnitude < 1e-6f)
            n = Vector3.up;

        float maxR = 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null) continue;
            Vector3 planar = Vector3.ProjectOnPlane(slots[i].transform.position - center, n);
            float r = planar.magnitude;
            if (r > maxR) maxR = r;
        }

        float height = Mathf.Max(minHeightAboveBoard, Mathf.Max(maxR, 0.5f) * extentScale);
        worldPosition = center + n * height;

        Slot3D refSlot = null;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null) { refSlot = slots[i]; break; }
        }

        Vector3 camForward = (-n).normalized;
        Vector3 rollHint = refSlot != null
            ? Vector3.ProjectOnPlane(refSlot.transform.forward, camForward)
            : Vector3.ProjectOnPlane(boardRoot.forward, camForward);
        if (rollHint.sqrMagnitude < 1e-6f && refSlot != null)
            rollHint = Vector3.ProjectOnPlane(refSlot.transform.right, camForward);
        if (rollHint.sqrMagnitude < 1e-6f)
            rollHint = Vector3.ProjectOnPlane(Vector3.forward, camForward);
        rollHint.Normalize();

        worldRotation = Quaternion.LookRotation(camForward, rollHint);
        return true;
    }
}
