using System;
using UnityEngine;
using UnityEngine.UI;

// 3D 슬롯 오브젝트에 직접 붙여서 사용할 컴포넌트입니다.
public class Slot3D : MonoBehaviour
{
    public Transform modelContainer;  // 숫자 모델이 배치될 부모 위치
    public MeshRenderer slotRenderer; // 색상을 변경할 모델의 렌더러
    [Tooltip("기차칸 카드 localScale에 곱합니다. num_path Start에서 배율이 설정됩니다.")]
    [Min(0.01f)]
    public float placedModelExtraScale = 10f;
    private GameObject currentNumberModel; // 현재 생성된 숫자 모델 인스턴스

    public System.Action onSlotClick; // 클릭 시 CardPlacementGame에 알릴 콜백

    public bool isFilled { get; private set; }
    public string cardValue { get; private set; }

    static readonly Color emptyColor  = Color.white;
    static readonly Color filledColor = new Color(0.9f, 0.9f, 0.9f);

    void OnMouseDown()
    {
        onSlotClick?.Invoke();
        
    }

    public void SetEmpty()
    {
        isFilled = false;
        cardValue = null;

        if (currentNumberModel != null) Destroy(currentNumberModel);

        if (slotRenderer != null)
            slotRenderer.enabled = true;
        else
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = true;
        }

        foreach (Transform child in transform)
            child.gameObject.SetActive(true);

        Image image = GetComponent<Image>();
        if (image != null) image.enabled = true;

        CanvasRenderer canvasRenderer = GetComponent<CanvasRenderer>();
        if (canvasRenderer != null) canvasRenderer.SetAlpha(1f);

        SetColor(emptyColor);
    }

    public void SetCard(GameObject prefab, string value)
    {
        isFilled = true;
        cardValue = value;

        if (currentNumberModel != null) Destroy(currentNumberModel);
        if (prefab != null && modelContainer != null)
        {
            currentNumberModel = Instantiate(prefab, modelContainer.position, modelContainer.rotation);
            currentNumberModel.transform.SetParent(modelContainer, true);
            if (Mathf.Abs(placedModelExtraScale - 1f) > 1e-4f)
                currentNumberModel.transform.localScale *= placedModelExtraScale;
        }

        Image image = GetComponent<Image>();
        if (image != null) image.enabled = false;
        
        CanvasRenderer canvasRenderer = GetComponent<CanvasRenderer>();
        if (canvasRenderer != null) canvasRenderer.SetAlpha(0f);
            
        SetColor(filledColor);
    }

    public void PlaceExistingObject(GameObject obj, string value, Quaternion rotation)
{
    isFilled = true;
    cardValue = value;

    if (currentNumberModel != null) Destroy(currentNumberModel);
    
    currentNumberModel = obj;
    Transform parent = modelContainer != null ? modelContainer : transform;
    Quaternion worldRot = parent.rotation * rotation;
    currentNumberModel.transform.SetPositionAndRotation(parent.position, worldRot);
    currentNumberModel.transform.SetParent(parent, true);
    if (Mathf.Abs(placedModelExtraScale - 1f) > 1e-4f)
        currentNumberModel.transform.localScale *= placedModelExtraScale;

    if (slotRenderer != null)
    {
        slotRenderer.enabled = false; 
    }
    else
    {
        var myRenderer = GetComponent<MeshRenderer>();
        if (myRenderer != null) myRenderer.enabled = false;
    }

    foreach (Transform child in transform)
    {
        if (child != modelContainer && child != obj.transform)
        {
            child.gameObject.SetActive(false);
        }
    }

    SetColor(filledColor);
}
public void SetColor(Color color)
{
    if (slotRenderer != null && !isFilled)
    {
        slotRenderer.material.color = color;
    }

    if (currentNumberModel == null) return;

    var renderers = currentNumberModel.GetComponentsInChildren<Renderer>(true);
    foreach (var r in renderers)
    {
        if (ShouldSkipTintingDigits(r))
            continue;

        var mats = r.materials;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null && ShouldSkipTintingMaterial(mats[i]))
                continue;
            mats[i].color = color;
        }
        r.materials = mats;
    }
}

    /// <summary>슬롯 하이라이트용 tint는 메시 바디에만 적용. TMP·폰트 셰이더에 color를 쓰면 검정 글자가 하얗게 깨집니다.</summary>
    static bool ShouldSkipTintingMaterial(Material mat)
    {
        if (mat == null || mat.shader == null)
            return false;
        string n = mat.shader.name;
        if (n.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("TMPro", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("GUI/Text", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return false;
    }

    static bool ContainsTextModelName(string objName)
    {
        return !string.IsNullOrEmpty(objName)
            && objName.IndexOf("textmodel", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 이름만 "텍스트"인 MeshRenderer 등: URP Lit에도 material.color 틴트가 들어가면 글자색이 하얗게 깨짐.
    /// <see cref="string.Contains(string)"/>로 "text"만 쓰면 "Texture"/"Context" 오탐이 나서 접두·접미·정확 일치 위주.
    /// </summary>
    static bool LooksLikeDedicatedTextMeshName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Contains("텍스트")) return true;
        if (string.Equals(name, "Text", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(name, "text", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_Text", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("Text_", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("Text", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>숫자/텍스트 메시 이름이 textmodel 부모 등과 다를 때 재질.color 곱하면 숫자가 바뀌어 보입니다.</summary>
    bool ShouldSkipTintingDigits(Renderer r)
    {
        if (r.GetComponent<TextMesh>() != null)
            return true;

        // TMPro 네임스페이스 없이도 동작: Shader Graph TMP는 셰이더 이름이 달라질 수 있음
        foreach (var comp in r.GetComponents<Component>())
        {
            if (comp == null) continue;
            string tn = comp.GetType().Name;
            if (tn == "TextMeshPro" || tn == "TextMeshProUGUI" || tn.Contains("TMP_Text"))
                return true;
        }

        for (Transform t = r.transform; t != null && t != transform; t = t.parent)
        {
            if (ContainsTextModelName(t.name) || LooksLikeDedicatedTextMeshName(t.name))
                return true;
        }

        return false;
    }

}