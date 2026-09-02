using UnityEngine;

/// <summary>숫자 카드 프리팹 스케일 — 부모 lossyScale 보정 + extra 배율.</summary>
public static class StreamsCardModelScale
{
    public static Vector3 CompensatedLocalScale(Vector3 prefabLocalScale, Vector3 parentLossyScale)
    {
        return new Vector3(
            prefabLocalScale.x / Mathf.Max(Mathf.Abs(parentLossyScale.x), 1e-6f),
            prefabLocalScale.y / Mathf.Max(Mathf.Abs(parentLossyScale.y), 1e-6f),
            prefabLocalScale.z / Mathf.Max(Mathf.Abs(parentLossyScale.z), 1e-6f));
    }

    public static void Apply(Transform card, Transform parent, Vector3 prefabLocalScale, float extraScale)
    {
        if (card == null || parent == null)
            return;

        Vector3 local = CompensatedLocalScale(prefabLocalScale, parent.lossyScale);
        if (Mathf.Abs(extraScale - 1f) > 1e-4f)
        {
            local.x *= extraScale;
            local.y *= extraScale;
            local.z *= extraScale;
        }

        card.localScale = local;
    }
}
