using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// GameBoard_3(플레이어)을 제외한 보드의 Tiles·Trains 및 하위 렌더러 밝기/색상을 낮춥니다.
/// 원본 색상을 캡처한 뒤 밝기 비율(0=어둡게, 1=원본)로 보간해 색조를 유지합니다.
/// </summary>
[DefaultExecutionOrder(1000)]
public class StreamsBoardBackgroundDimmer : MonoBehaviour
{
    public string brightBoardName = "GameBoard_3";

    [Range(0f, 1f)]
    public float brightness = 0.45f;

    public Color colorTint = new Color(0.82f, 0.86f, 0.92f, 1f);

    [Header("조명 영향 제거")]
    public bool disableLightProbes = true;
    public bool disableReflectionProbes = true;
    public bool disableReceiveShadows = true;
    public bool useUnlitShader = true;

    static readonly string[] TargetRootNames = { "Tiles", "Trains" };

    static StreamsBoardBackgroundDimmer _instance;
    static Shader _urpUnlitShader;
    static Shader _builtinUnlitShader;

    public static StreamsBoardBackgroundDimmer Instance => _instance;

    void Awake() => _instance = this;

    public Color ScaleColor(Color c) =>
        new Color(
            c.r * brightness * colorTint.r,
            c.g * brightness * colorTint.g,
            c.b * brightness * colorTint.b,
            c.a);

    public void Apply()
    {
        Transform boardsRoot = transform;
        for (int i = 0; i < boardsRoot.childCount; i++)
        {
            Transform board = boardsRoot.GetChild(i);
            if (board.name == brightBoardName)
                continue;

            foreach (string rootName in TargetRootNames)
            {
                Transform[] roots = FindChildrenRecursive(board, rootName);
                foreach (Transform root in roots)
                    ApplyStaticDimToHierarchy(root);
            }
        }
    }

    /// <summary>런타임에 등장한 카드 등 — sharedMaterial 기준 원본 팔레트를 저장합니다.</summary>
    public void CaptureHierarchyPalette(Transform root)
    {
        if (root == null)
            return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            EnsurePaletteCaptured(renderer);
    }

    /// <summary>highlight01: 0=어둡게(원본×dim), 1=원본 색 그대로.</summary>
    public void ApplyRelativeBrightnessHierarchy(Transform root, float highlight01)
    {
        if (root == null)
            return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            ApplyRelativeBrightness(renderer, highlight01);
    }

    public void ApplyRelativeBrightness(Renderer renderer, float highlight01)
    {
        if (renderer == null || ShouldSkipRenderer(renderer))
            return;

        highlight01 = Mathf.Clamp01(highlight01);
        var state = GetOrCreateState(renderer);
        EnsurePaletteCaptured(renderer, state);
        PrepareRendererLighting(renderer);

        if (renderer is SpriteRenderer spriteRenderer)
        {
            Color dim = ScaleColor(state.spriteOriginal);
            spriteRenderer.color = Color.Lerp(dim, state.spriteOriginal, highlight01);
            return;
        }

        Material[] shared = renderer.sharedMaterials;
        Material[] instances = new Material[shared.Length];

        for (int i = 0; i < shared.Length; i++)
        {
            Material source = shared[i];
            if (source == null)
                continue;

            Material instance = new Material(source);
            instances[i] = instance;
            renderer.SetPropertyBlock(null, i);

            if (IsTextMaterial(instance))
            {
                Color dimFace = ScaleColor(state.textFaceOriginal);
                Color dimOutline = ScaleColor(state.textOutlineOriginal);
                if (instance.HasProperty("_FaceColor"))
                    instance.SetColor("_FaceColor", Color.Lerp(dimFace, state.textFaceOriginal, highlight01));
                if (instance.HasProperty("_OutlineColor"))
                    instance.SetColor("_OutlineColor", Color.Lerp(dimOutline, state.textOutlineOriginal, highlight01));
                continue;
            }

            if (ShouldConvertToUnlit(renderer, instance))
                ConvertMaterialToUnlit(instance);

            Color original = i < state.baseColors.Length ? state.baseColors[i] : ReadMaterialColor(source);
            Color dim = ScaleColor(original);
            SetMaterialColor(instance, Color.Lerp(dim, original, highlight01));
        }

        renderer.materials = instances;
    }

    public void ApplyIntendedTint(Renderer renderer, Color intendedColor)
    {
        if (renderer == null || ShouldSkipRenderer(renderer))
            return;

        var state = GetOrCreateState(renderer);
        if (state.staticDimmed)
        {
            ApplyRelativeBrightness(renderer, 0f);
            return;
        }

        PrepareRendererLighting(renderer);

        if (renderer is SpriteRenderer spriteRenderer)
        {
            spriteRenderer.color = ScaleColor(intendedColor);
            return;
        }

        Material[] materials = renderer.materials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material mat = materials[i];
            if (mat == null)
                continue;

            if (IsTextMaterial(mat))
                ApplyTextDim(renderer, mat);
            else
                ApplySurfaceColor(renderer, mat, ScaleColor(intendedColor), i);
        }
    }

    public void ApplyTextDim(Renderer renderer, Material mat)
    {
        if (renderer == null || mat == null)
            return;

        var state = GetOrCreateState(renderer);
        EnsurePaletteCaptured(renderer, state);

        if (mat.HasProperty("_FaceColor"))
            mat.SetColor("_FaceColor", ScaleColor(state.textFaceOriginal));

        if (mat.HasProperty("_OutlineColor"))
            mat.SetColor("_OutlineColor", ScaleColor(state.textOutlineOriginal));
    }

    public void RestoreBright(Renderer renderer)
    {
        if (renderer == null || ShouldSkipRenderer(renderer))
            return;

        var marker = GetOrCreateState(renderer);
        RestoreRendererLighting(renderer, marker);
        ClearDimPropertyBlocks(renderer);
        ApplyRelativeBrightness(renderer, 1f);
    }

    public void RestoreBrightHierarchy(Transform root)
    {
        if (root == null)
            return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!ShouldRestoreAsTileBackground(renderer))
                continue;

            RestoreBright(renderer);
        }
    }

    /// <summary>오름차순 run 강조 — 원본 팔레트와 무관하게 의도 색을 그대로 칠합니다(플레이어 보드와 동일).</summary>
    public void ApplyHighlightTintHierarchy(Transform root, Color highlightColor)
    {
        if (root == null)
            return;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            ApplyHighlightTint(renderer, highlightColor);
    }

    public void ApplyHighlightTint(Renderer renderer, Color highlightColor)
    {
        if (renderer == null || ShouldSkipRenderer(renderer))
            return;

        PrepareRendererLighting(renderer);

        if (renderer is SpriteRenderer spriteRenderer)
        {
            spriteRenderer.color = highlightColor;
            return;
        }

        Material[] shared = renderer.sharedMaterials;
        Material[] instances = new Material[shared.Length];
        for (int i = 0; i < shared.Length; i++)
        {
            Material source = shared[i];
            if (source == null)
                continue;

            Material instance = new Material(source);
            instances[i] = instance;
            renderer.SetPropertyBlock(null, i);

            if (IsTextMaterial(instance))
                continue;

            if (ShouldConvertToUnlit(renderer, instance))
                ConvertMaterialToUnlit(instance);

            SetMaterialColor(instance, highlightColor);
        }

        renderer.materials = instances;
    }

    static bool ShouldRestoreAsTileBackground(Renderer renderer)
    {
        if (renderer == null)
            return true;

        var slot = renderer.GetComponentInParent<Slot3D>();
        if (slot == null || !slot.isFilled)
            return true;

        if (slot.modelContainer != null && renderer.transform.IsChildOf(slot.modelContainer))
            return false;

        return true;
    }

    public void RestoreTilesForBoard(Transform gameBoard)
    {
        if (gameBoard == null)
            return;

        foreach (string rootName in TargetRootNames)
        {
            if (rootName != "Tiles")
                continue;

            Transform[] roots = FindChildrenRecursive(gameBoard, rootName);
            foreach (Transform root in roots)
                RestoreBrightHierarchy(root);
        }
    }

    void EnsurePaletteCaptured(Renderer renderer)
    {
        EnsurePaletteCaptured(renderer, GetOrCreateState(renderer));
    }

    void EnsurePaletteCaptured(Renderer renderer, StreamsDimmedRendererState state)
    {
        if (state.paletteCaptured)
            return;

        if (renderer is SpriteRenderer spriteRenderer)
        {
            state.spriteOriginal = spriteRenderer.color;
            state.spriteCaptured = true;
        }

        Material[] shared = renderer.sharedMaterials;
        if (state.baseColors == null || state.baseColors.Length != shared.Length)
            state.baseColors = new Color[shared.Length];
        if (state.originalShaders == null || state.originalShaders.Length != shared.Length)
            state.originalShaders = new Shader[shared.Length];

        for (int i = 0; i < shared.Length; i++)
        {
            Material source = shared[i];
            if (source == null)
                continue;

            state.originalShaders[i] = source.shader;

            if (IsTextMaterial(source))
            {
                if (source.HasProperty("_FaceColor"))
                    state.textFaceOriginal = source.GetColor("_FaceColor");
                if (source.HasProperty("_OutlineColor"))
                    state.textOutlineOriginal = source.GetColor("_OutlineColor");
                state.textCaptured = true;
                continue;
            }

            state.baseColors[i] = ReadMaterialColor(source);
        }

        state.paletteCaptured = true;
    }

    void ApplyStaticDimToHierarchy(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            ApplyStaticDim(renderer);
    }

    void ApplyStaticDim(Renderer renderer)
    {
        if (ShouldSkipRenderer(renderer))
            return;

        var marker = GetOrCreateState(renderer);
        marker.staticDimmed = true;
        ApplyRelativeBrightness(renderer, 0f);
    }

    void PrepareRendererLighting(Renderer renderer)
    {
        var state = GetOrCreateState(renderer);
        if (state.lightingPrepared)
            return;

        state.originalLightProbeUsage = renderer.lightProbeUsage;
        state.originalReflectionProbeUsage = renderer.reflectionProbeUsage;
        state.originalReceiveShadows = renderer.receiveShadows;
        state.originalShadowCastingMode = renderer.shadowCastingMode;

        if (disableLightProbes)
            renderer.lightProbeUsage = LightProbeUsage.Off;

        if (disableReflectionProbes)
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        if (disableReceiveShadows)
            renderer.receiveShadows = false;

        renderer.shadowCastingMode = ShadowCastingMode.Off;

        state.lightingPrepared = true;
    }

    static void RestoreRendererLighting(Renderer renderer, StreamsDimmedRendererState state)
    {
        if (renderer == null || state == null || !state.lightingPrepared)
            return;

        renderer.lightProbeUsage = state.originalLightProbeUsage;
        renderer.reflectionProbeUsage = state.originalReflectionProbeUsage;
        renderer.receiveShadows = state.originalReceiveShadows;
        renderer.shadowCastingMode = state.originalShadowCastingMode;
    }

    void ApplySurfaceColor(Renderer renderer, Material mat, Color dimmedColor, int materialIndex)
    {
        if (ShouldConvertToUnlit(renderer, mat))
        {
            ConvertMaterialToUnlit(mat);
            SetMaterialColor(mat, dimmedColor);
            return;
        }

        FlattenLitMaterial(mat);
        SetMaterialColor(mat, dimmedColor);
    }

    void ConvertMaterialToUnlit(Material mat)
    {
        if (IsAlreadyUnlitShader(mat.shader) || IsTextMaterial(mat))
            return;

        Shader unlit = GetUnlitShader();
        if (unlit == null)
            return;

        Texture baseMap = null;
        if (mat.HasProperty("_BaseMap"))
            baseMap = mat.GetTexture("_BaseMap");
        else if (mat.HasProperty("_MainTex"))
            baseMap = mat.GetTexture("_MainTex");

        mat.shader = unlit;

        if (baseMap != null)
        {
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", baseMap);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", baseMap);
        }
    }

    static void FlattenLitMaterial(Material mat)
    {
        if (mat == null || !IsLitShader(mat.shader))
            return;

        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0f);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0f);
        if (mat.HasProperty("_SpecularHighlights"))
            mat.SetFloat("_SpecularHighlights", 0f);
        if (mat.HasProperty("_EnvironmentReflections"))
            mat.SetFloat("_EnvironmentReflections", 0f);
        if (mat.HasProperty("_EmissionColor"))
            mat.SetColor("_EmissionColor", Color.black);

        mat.DisableKeyword("_EMISSION");
    }

    static void ClearDimPropertyBlocks(Renderer renderer)
    {
        if (renderer == null)
            return;

        int count = Mathf.Max(1, renderer.sharedMaterials.Length);
        for (int i = 0; i < count; i++)
            renderer.SetPropertyBlock(null, i);
    }

    static Shader GetUnlitShader()
    {
        if (_urpUnlitShader == null)
            _urpUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (_urpUnlitShader != null)
            return _urpUnlitShader;

        if (_builtinUnlitShader == null)
            _builtinUnlitShader = Shader.Find("Unlit/Texture") ?? Shader.Find("Unlit/Color");
        return _builtinUnlitShader;
    }

    static bool IsLitShader(Shader shader)
    {
        if (shader == null)
            return false;

        string name = shader.name;
        if (name.IndexOf("Unlit", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (name.IndexOf("TextMeshPro", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (name.IndexOf("TMPro", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        return name.IndexOf("Lit", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Universal Render Pipeline", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Standard", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool ShouldSkipRenderer(Renderer renderer)
    {
        if (renderer == null)
            return true;

        if (renderer.GetComponentInParent<Canvas>() != null)
            return true;

        return false;
    }

    static Color ReadMaterialColor(Material mat)
    {
        if (mat.HasProperty("_BaseColor"))
            return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color"))
            return mat.GetColor("_Color");
        return mat.color;
    }

    static void SetMaterialColor(Material mat, Color color)
    {
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
        mat.color = color;
    }

    bool ShouldConvertToUnlit(Renderer renderer, Material mat)
    {
        if (!useUnlitShader || mat == null || IsTextMaterial(mat))
            return false;

        return !IsAlreadyUnlitShader(mat.shader);
    }

    static bool IsAlreadyUnlitShader(Shader shader)
    {
        if (shader == null)
            return false;

        string name = shader.name;
        return name.IndexOf("Unlit", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsTextMaterial(Material mat)
    {
        if (mat == null || mat.shader == null)
            return false;

        string name = mat.shader.name;
        return name.IndexOf("TextMeshPro", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("TMPro", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("GUI/Text", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static StreamsDimmedRendererState GetOrCreateState(Renderer renderer)
    {
        var state = renderer.GetComponent<StreamsDimmedRendererState>();
        if (state == null)
            state = renderer.gameObject.AddComponent<StreamsDimmedRendererState>();
        return state;
    }

    static Transform[] FindChildrenRecursive(Transform parent, string name)
    {
        var results = new System.Collections.Generic.List<Transform>();
        CollectChildrenRecursive(parent, name, results);
        return results.ToArray();
    }

    static void CollectChildrenRecursive(Transform parent, string name, System.Collections.Generic.List<Transform> results)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
                results.Add(child);
            CollectChildrenRecursive(child, name, results);
        }
    }
}

sealed class StreamsDimmedRendererState : MonoBehaviour
{
    public bool staticDimmed;
    public bool paletteCaptured;
    public bool lightingPrepared;
    public LightProbeUsage originalLightProbeUsage = LightProbeUsage.BlendProbes;
    public ReflectionProbeUsage originalReflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
    public bool originalReceiveShadows = true;
    public ShadowCastingMode originalShadowCastingMode = ShadowCastingMode.On;
    public bool spriteCaptured;
    public Color spriteOriginal = Color.white;
    public Color[] baseColors;
    public Shader[] originalShaders;
    public bool textCaptured;
    public Color textFaceOriginal = Color.white;
    public Color textOutlineOriginal = Color.white;
}
