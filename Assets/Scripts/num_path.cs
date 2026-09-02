using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.InputSystem;

[System.Serializable]
public class NumberModelMapping
{
    public string value;
    public GameObject prefab;   // 세로 버전
    public GameObject prefabH;  // 가로 버전
}

public class num_path : MonoBehaviour
{
    [Header("UI")]
    public Button drawButton;
    public Text scoreText;
    public Text scoreText2;
    [Tooltip("비우면 PlayerScoreText / AIScoreText 이름으로 찾습니다.")]
    public TextMeshProUGUI uiScoreLabel;
    public List<Slot3D> slots;
    [Tooltip("비우면 PlayerBoard/AIBoard의 Slots 자식에서 자동으로 찾습니다.")]
    public List<StreamsUiSlot> uiSlots;

    [Header("3D Models")]
    public List<NumberModelMapping> numberPrefabs;
    public Transform holdingPoint;
    [Tooltip("holdingPoint 로컬 기준으로 미리보기를 이동합니다.")]
    public Vector3 cardPreviewLocalOffset = Vector3.zero;
    [Tooltip("미리보기 추가 회전(Euler, holdingPoint 로컬). 면이 아래를 보면 X를 -90 근처로 조정합니다.")]
    public Vector3 cardPreviewLocalEuler = new Vector3(-90f, 0f, 0f);
    [Tooltip("GameBoard 앵커의 up(없으면 holdingPoint up)으로 월드 이동해 판에서 띄웁니다.")]
    public float cardPreviewLiftWorld = 18f;
    [Tooltip("미리보기 루트 localScale에 곱합니다. holdingPoint 부모 스케일로 작아 보일 때 조정합니다.")]
    [Min(0.01f)]
    public float cardPreviewExtraScale = 10f;
    [Tooltip("0보다 크면 기차칸에만 이 배율을 씁니다. 씬에 원래 박힌 카드보다 작으면 여기를 올리고(예: 12~14), 0이면 미리보기와 동일합니다.")]
    [Min(0f)]
    public float cardSlotExtraScale = 0f;
    [Tooltip("pocket에서 holdingPoint로 이동하는 시간(초). 0이면 즉시 스폰합니다.")]
    [Min(0f)]
    public float cardDealAnimDuration = 0.45f;
    [Tooltip("pocket 기준 시작 Y 회전 오프셋(도).")]
    public float cardDealStartYawDegrees = -45f;
    [Tooltip("비어 있으면 이 판 GameBoard_x 하위에서 이름 pocket을 찾습니다.")]
    public Transform cardDealPocketOverride;

    [Tooltip("플레이어만 클릭 입력을 받습니다. AI 판은 끄세요.")]
    public bool isPlayerControlledBoard = true;

    [Header("AI 확률 표시")]
    [Tooltip("3D TextMesh 라벨 부모. Canvas 하위가 아닌 GameBoard 직속 Transform.")]
    public Transform aiProbabilityRoot;

    const string ProbabilityLabelRootName = "AiProbability";
    const string PlayerScoreObjectName = "PlayerScoreText";
    const string AiScoreObjectName = "AIScoreText";
    const string BoardCanvasName = "BoardCanvas";
    const string LegacyBoardsRootName = "GameBoards";

    /// <summary>플레이어가 이번에 뽑은 카드를 칸에 놓았을 때 한 번 호출됩니다.</summary>
    public event System.Action OnPlayerCardPlaced;

    public const int DRAW_LIMIT = 20;
    string pendingCard = null;
    int drawnCount = 0;

    private GameObject currentSpawnedModel = null;
    Camera _flowMainCamera;
    bool _previewAnimating;
    Coroutine _dealCoroutine;
    Slot3D _aiLastBrightSlot;
    bool _probabilityBindingsDone;
    int _displayedUiScore;
    bool _hasDisplayedUiScore;
    Vector3 _uiScoreBaseScale = Vector3.one;
    bool _uiScoreScaleCaptured;
    Coroutine _scorePop;
    bool _placementRestricted;
    int _allowedPlacementSlot = -1;

    int[] scoreTable = new int[]
    {
        0, 1, 3, 5, 7, 9, 11, 15, 20, 25,
        30, 35, 40, 50, 60, 70, 85, 100, 150, 300
    };

    void Awake()
    {
        ApplyBoardRoleFromAnchor();
        ResolveFlowCamera();
    }

    void ApplyBoardRoleFromAnchor()
    {
        Transform anchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(transform);
        if (anchor == null)
            return;

        if (anchor.name == "GameBoard_Player")
            isPlayerControlledBoard = true;
        else if (anchor.name == "GameBoard_AI")
            isPlayerControlledBoard = false;
    }

    void ResolveFlowCamera()
    {
        var flow = UnityEngine.Object.FindFirstObjectByType<StreamsGameFlowController>();
        if (flow != null)
        {
            _flowMainCamera = isPlayerControlledBoard ? flow.PlayerCamera : flow.AiCamera;
            if (_flowMainCamera == null)
                _flowMainCamera = flow.mainCamera;
        }
    }

    Camera GameplayCameraOrMain()
    {
        if (_flowMainCamera == null)
            ResolveFlowCamera();
        return _flowMainCamera != null ? _flowMainCamera : Camera.main;
    }

    void Start()
    {
        BindUiScoreLabel();
        if (isPlayerControlledBoard && uiScoreLabel == null && (scoreText == null || scoreText2 == null))
            Debug.LogError($"{name}: 플레이어 보드의 점수 라벨이 연결되지 않았습니다.");

        if (holdingPoint == null && StreamsBoardCameraPose.TryFindGameBoardAnchor(transform) == null)
            Debug.LogError("num_path: holdingPoint가 비어 있고 GameBoard 앵커도 없어 미리보기 위치를 찾을 수 없습니다.");
        else if (holdingPoint == null)
            Debug.LogWarning("num_path: holdingPoint 미지정 — 런타임에 이 판의 GameBoard 하위에서 이름 'holdingPoint'를 찾습니다.");
        if (numberPrefabs == null || numberPrefabs.Count == 0)
            Debug.LogError("num_path: numberPrefabs가 비어 있어 카드 모델을 생성할 수 없습니다.");

        if (scoreText != null)
            scoreText.text = "";
        if (scoreText2 != null)
        {
            scoreText2.text = isPlayerControlledBoard ? "현재 점수: 0" : "";
            ApplyCurrentScoreLabelLayout(scoreText2);
        }

        SetUiScore(0);

        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    float slotScale = SlotExtraScale;
                    slots[i].placedModelExtraScale = slotScale;
                    slots[i].SetSlotIndex(i);
                    slots[i].SetDimmedPalette(!isPlayerControlledBoard);
                    slots[i].SetEmpty();
                }
            }
        }

        BindUiSlots();

        if (!isPlayerControlledBoard)
            BindSlotProbabilityLabels();
    }

    /// <summary>
    /// zip 피드백: 점수 글자를 기차 흰 선에서 피하고 키웁니다.
    /// </summary>
    static void ApplyCurrentScoreLabelLayout(Text label)
    {
        if (label == null)
            return;

        label.fontSize = 30;
        label.rectTransform.anchoredPosition = new Vector2(0f, -300f);
    }

    /// <summary>
    /// 인스펙터의 <see cref="holdingPoint"/>가 다른 GameBoard에 잘못 연결된 경우,
    /// 이 <c>num_path</c>가 속한 <c>GameBoard_0~3</c> 앵커 아래에서 이름이 <c>holdingPoint</c>인 자식을 찾아 같은 판 위에 미리보기를 띄웁니다.
    /// </summary>
    Transform ResolvePreviewSpawnTransform()
    {
        Transform boardAnchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(transform);

        if (holdingPoint == null)
            return boardAnchor != null ? FindNamedDeepChild(boardAnchor, "holdingPoint") : null;

        Transform hpAnchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(holdingPoint);
        if (boardAnchor != null && hpAnchor != null && hpAnchor != boardAnchor)
        {
            Transform underSameBoard = FindNamedDeepChild(boardAnchor, "holdingPoint");
            if (underSameBoard != null)
            {
                Debug.LogWarning($"num_path: holdingPoint가 다른 보드({hpAnchor.name})에 연결되어 있어, 이 판({boardAnchor.name}) 아래 holdingPoint로 미리보기를 옮깁니다.");
                return underSameBoard;
            }

            Debug.LogWarning($"num_path: holdingPoint가 {hpAnchor.name}에 있고 {boardAnchor.name} 아래 holdingPoint를 찾지 못했습니다. 인스펙터 연결을 확인하세요.");
        }

        return holdingPoint;
    }

    bool HasUiSlots => uiSlots != null && uiSlots.Count > 0;

    public int SlotCount
    {
        get
        {
            if (HasUiSlots)
                return uiSlots.Count;
            return slots != null ? slots.Count : 0;
        }
    }

    public bool IsSlotFilled(int index)
    {
        if (HasUiSlots)
            return index < 0 || index >= uiSlots.Count || uiSlots[index] == null || uiSlots[index].isFilled;
        if (slots == null || index < 0 || index >= slots.Count || slots[index] == null)
            return true;
        return slots[index].isFilled;
    }

    public string GetSlotCardValue(int index)
    {
        if (HasUiSlots)
        {
            if (index < 0 || index >= uiSlots.Count || uiSlots[index] == null || !uiSlots[index].isFilled)
                return null;
            return uiSlots[index].cardValue;
        }

        if (slots == null || index < 0 || index >= slots.Count || slots[index] == null || !slots[index].isFilled)
            return null;
        return slots[index].cardValue;
    }

    public int FirstEmptySlotIndex()
    {
        int n = SlotCount;
        for (int i = 0; i < n; i++)
        {
            if (!IsSlotFilled(i))
                return i;
        }

        return -1;
    }

    public void EnsureUiSlotsBound()
    {
        BindUiScoreLabel();
        BindUiSlots();
    }

    /// <summary>튜토리얼: 이 칸만 배치를 받습니다. <paramref name="index"/>가 음수면 어떤 칸도 받지 않습니다.</summary>
    public void SetAllowedPlacementSlot(int index)
    {
        _placementRestricted = true;
        _allowedPlacementSlot = index;
    }

    public void ClearAllowedPlacementSlot()
    {
        _placementRestricted = false;
        _allowedPlacementSlot = -1;
    }

    void BindUiSlots()
    {
        if (uiSlots != null && uiSlots.Count > 0)
        {
            WireUiSlotClicks();
            RefreshUiRunOutlines();
            return;
        }

        string boardName = isPlayerControlledBoard ? "PlayerBoard" : "AIBoard";
        Transform board = FindUiBoardRoot(boardName);
        if (board == null)
            return;

        Transform root = board.Find("Slots");
        if (root == null)
            root = FindNamedDeepChild(board, "Slots");
        if (root == null)
            return;

        uiSlots = new List<StreamsUiSlot>();
        for (int i = 0; i < root.childCount; i++)
        {
            var slot = root.GetChild(i).GetComponent<StreamsUiSlot>();
            if (slot == null || slot.IsHoldingPreview)
                continue;
            uiSlots.Add(slot);
        }

        if (uiSlots.Count > 20)
            uiSlots.RemoveRange(20, uiSlots.Count - 20);

        WireUiSlotClicks();
        if (!HasUiSlots)
            Debug.LogWarning($"{name}: {boardName}/Slots에서 StreamsUiSlot을 찾지 못했습니다. 자식 순서와 컴포넌트를 확인하세요.");
        RefreshUiRunOutlines();
    }

    static Transform FindUiBoardRoot(string boardName)
    {
        var named = GameObject.Find(boardName);
        if (named != null)
            return named.transform;

        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas == null)
                continue;
            Transform found = canvas.transform.Find(boardName);
            if (found != null)
                return found;
            found = FindNamedDeepChild(canvas.transform, boardName);
            if (found != null)
                return found;
        }

        return null;
    }

    void WireUiSlotClicks()
    {
        if (!HasUiSlots)
            return;

        for (int i = 0; i < uiSlots.Count; i++)
        {
            if (uiSlots[i] == null)
                continue;
            uiSlots[i].BindPlacement(i, OnSlotClicked, isPlayerControlledBoard);
        }
    }

    void ClearUiHints()
    {
        if (!HasUiSlots)
            return;

        foreach (var slot in uiSlots)
        {
            if (slot != null && !slot.isFilled)
                slot.SetEmpty();
        }
    }

    void ClearHoldingPreview()
    {
        foreach (var slot in FindObjectsByType<StreamsUiSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (slot != null && slot.IsHoldingPreview)
                slot.SetEmpty();
        }
    }

    static Transform FindNamedDeepChild(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName)) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (string.Equals(c.name, targetName, StringComparison.OrdinalIgnoreCase))
                return c;
            Transform nested = FindNamedDeepChild(c, targetName);
            if (nested != null) return nested;
        }

        return null;
    }

    Transform ResolveDealPocketTransform()
    {
        if (cardDealPocketOverride != null)
            return cardDealPocketOverride;

        Transform boardAnchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(transform);
        return boardAnchor != null ? FindNamedDeepChild(boardAnchor, "pocket") : null;
    }

    void ClearPreviewSpawn()
    {
        if (_dealCoroutine != null)
        {
            StopCoroutine(_dealCoroutine);
            _dealCoroutine = null;
        }

        _previewAnimating = false;
        if (currentSpawnedModel != null)
        {
            Destroy(currentSpawnedModel);
            currentSpawnedModel = null;
        }
    }

    float PreviewExtraScale => cardPreviewExtraScale;

    float SlotExtraScale => cardSlotExtraScale > 0.0001f ? cardSlotExtraScale : cardPreviewExtraScale;

    void ApplyPreviewModelScale(Transform card, Transform parent, Vector3 prefabLocalScale)
    {
        StreamsCardModelScale.Apply(card, parent, prefabLocalScale, PreviewExtraScale);
    }

    // #region agent log
    void LogPreviewScale(string hypothesisId, string location, string phase, Transform card, Transform spawnParent, Vector3 prefabLocalScale, bool useDealAnim)
    {
        if (card == null) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var p = spawnParent != null ? spawnParent.lossyScale : Vector3.one;
        var ls = card.lossyScale;
        var loc = card.localScale;
        StreamsDebug9b.Line(hypothesisId, location, phase,
            $"{{\"useDealAnim\":{(useDealAnim ? "true" : "false")},\"extraScale\":{cardPreviewExtraScale.ToString("F4", inv)},\"prefabLocal\":\"{V3(prefabLocalScale, inv)}\",\"parentLossy\":\"{V3(p, inv)}\",\"cardLocal\":\"{V3(loc, inv)}\",\"cardLossy\":\"{V3(ls, inv)}\"}}");
    }

    static string V3(Vector3 v, System.Globalization.CultureInfo inv) =>
        v.x.ToString("F4", inv) + "," + v.y.ToString("F4", inv) + "," + v.z.ToString("F4", inv);
    // #endregion

    void ApplyPreviewLocalPose(Transform card, Transform spawnParent)
    {
        card.SetParent(spawnParent, true);
        card.localPosition = cardPreviewLocalOffset;
        if (Mathf.Abs(cardPreviewLiftWorld) > 1e-4f)
        {
            Transform boardForLift = StreamsBoardCameraPose.TryFindGameBoardAnchor(transform);
            Vector3 liftAxis = boardForLift != null ? boardForLift.up : spawnParent.up;
            card.position += liftAxis.normalized * cardPreviewLiftWorld;
        }

        card.localRotation = Quaternion.Euler(cardPreviewLocalEuler);
    }

    void ComputePreviewFinalWorldPose(Transform spawnParent, Vector3 prefabLocalScale, out Vector3 worldPos, out Quaternion worldRot)
    {
        var poseRef = new GameObject("PreviewPoseRef");
        poseRef.transform.SetPositionAndRotation(spawnParent.position, spawnParent.rotation);
        ApplyPreviewLocalPose(poseRef.transform, spawnParent);
        ApplyPreviewModelScale(poseRef.transform, spawnParent, prefabLocalScale);
        worldPos = poseRef.transform.position;
        worldRot = poseRef.transform.rotation;
        Destroy(poseRef);
    }

    void SpawnPreviewInstant(GameObject prefab, Transform spawnParent)
    {
        Vector3 prefabLocalScale = prefab.transform.localScale;
        currentSpawnedModel = Instantiate(prefab, spawnParent.position, spawnParent.rotation);
        ApplyPreviewLocalPose(currentSpawnedModel.transform, spawnParent);
        ApplyPreviewModelScale(currentSpawnedModel.transform, spawnParent, prefabLocalScale);
    }

    IEnumerator AnimateCardPreviewDeal(Transform spawnParent, Vector3 endWorldPos, Quaternion endWorldRot, Vector3 prefabLocalScale)
    {
        _previewAnimating = true;
        Transform tr = currentSpawnedModel.transform;
        Vector3 startPos = tr.position;
        Quaternion startRot = tr.rotation;
        float duration = Mathf.Max(cardDealAnimDuration, 1e-4f);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            tr.position = Vector3.Lerp(startPos, endWorldPos, t);
            tr.rotation = Quaternion.Slerp(startRot, endWorldRot, t);
            yield return null;
        }

        ApplyPreviewLocalPose(tr, spawnParent);
        _previewAnimating = false;
        _dealCoroutine = null;
    }

    public void ReceiveCard(string drawn)
    {
        // #region agent log
        StreamsAgentLog.Line("H0", "num_path.ReceiveCard:entry", "ReceiveCard called", $"{{\"drawn\":\"{StreamsAgentLog.Esc(drawn)}\",\"drawnCountBefore\":{drawnCount},\"board\":\"{StreamsAgentLog.Esc(gameObject.name)}\"}}");
        // #endregion

        if (drawnCount >= DRAW_LIMIT)
        {
            // #region agent log
            StreamsAgentLog.Line("H3", "num_path.ReceiveCard", "early exit draw limit", $"{{\"drawnCount\":{drawnCount}}}");
            // #endregion
            return;
        }

        pendingCard = drawn;
        drawnCount++;

        if (drawButton != null)
            drawButton.interactable = false;

        EnsureUiSlotsBound();
        if (HasUiSlots)
            return;

        GameObject prefab = GetModelPrefab(drawn, false);
        if (prefab == null)
        {
            Debug.LogWarning($"num_path: '{drawn}' 값에 대응하는 prefab 매핑이 없어 미리보기를 띄울 수 없습니다.");
            // #region agent log
            StreamsAgentLog.Line("H1", "num_path.ReceiveCard", "prefab null no Instantiate", $"{{\"drawn\":\"{StreamsAgentLog.Esc(drawn)}\"}}");
            // #endregion
            return;
        }

        Transform spawnParent = ResolvePreviewSpawnTransform();
        if (spawnParent == null)
        {
            Debug.LogWarning("num_path: 미리보기 부모(holdingPoint)를 찾을 수 없습니다. 인스펙터의 holdingPoint 또는 GameBoard_x 하위 이름 holdingPoint를 확인하세요.");
            // #region agent log
            StreamsAgentLog.Line("H2", "num_path.ReceiveCard", "spawnParent null no Instantiate", "{}");
            // #endregion
            return;
        }

        ClearPreviewSpawn();

        Transform dealPocket = ResolveDealPocketTransform();
        bool useDealAnim = dealPocket != null && cardDealAnimDuration > 1e-4f;

        if (useDealAnim)
        {
            Vector3 prefabLocalScale = prefab.transform.localScale;
            Quaternion startRot = dealPocket.rotation * Quaternion.Euler(0f, cardDealStartYawDegrees, 0f);
            currentSpawnedModel = Instantiate(prefab, dealPocket.position, startRot);
            currentSpawnedModel.transform.SetParent(spawnParent, true);
            ApplyPreviewModelScale(currentSpawnedModel.transform, spawnParent, prefabLocalScale);

            ComputePreviewFinalWorldPose(spawnParent, prefabLocalScale, out Vector3 endWorldPos, out Quaternion endWorldRot);
            _dealCoroutine = StartCoroutine(AnimateCardPreviewDeal(spawnParent, endWorldPos, endWorldRot, prefabLocalScale));
        }
        else
        {
            SpawnPreviewInstant(prefab, spawnParent);
        }

        if (currentSpawnedModel != null)
        {
            // #region agent log
            var tr = currentSpawnedModel.transform;
            var hp = spawnParent.position;
            int nR = currentSpawnedModel.GetComponentsInChildren<Renderer>(true).Length;
            var ls = tr.lossyScale;
            var camMain = Camera.main;
            var camFlow = _flowMainCamera;
            float distMain = camMain != null ? Vector3.Distance(camMain.transform.position, tr.position) : -1f;
            float distFlow = camFlow != null ? Vector3.Distance(camFlow.transform.position, tr.position) : -1f;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string distMainJ = distMain.ToString("F2", inv);
            string distFlowJ = distFlow.ToString("F2", inv);
            Transform ba = StreamsBoardCameraPose.TryFindGameBoardAnchor(transform);
            string baName = ba != null ? ba.name : "null";
            bool sameCam = camMain != null && camFlow != null && camMain == camFlow;
            StreamsAgentLog.Line("H4", "num_path.ReceiveCard", "after Instantiate preview", "{\"instance\":\"" + StreamsAgentLog.Esc(currentSpawnedModel.name) + "\",\"activeSelf\":" + (currentSpawnedModel.activeSelf ? "true" : "false") + ",\"activeInHierarchy\":" + (currentSpawnedModel.activeInHierarchy ? "true" : "false") + ",\"layer\":" + currentSpawnedModel.layer + ",\"worldPos\":\"" + tr.position.x.ToString("F2", inv) + "," + tr.position.y.ToString("F2", inv) + "," + tr.position.z.ToString("F2", inv) + "\",\"spawnParent\":\"" + StreamsAgentLog.Esc(spawnParent.name) + "\",\"boardAnchor\":\"" + StreamsAgentLog.Esc(baName) + "\",\"holdingWorld\":\"" + hp.x.ToString("F2", inv) + "," + hp.y.ToString("F2", inv) + "," + hp.z.ToString("F2", inv) + "\",\"lossyScale\":\"" + ls.x.ToString("F4", inv) + "," + ls.y.ToString("F4", inv) + "," + ls.z.ToString("F4", inv) + "\",\"rendererCount\":" + nR + ",\"distToCameraMain\":" + distMainJ + ",\"distToFlowMainCam\":" + distFlowJ + ",\"mainEqualsFlowCam\":" + (sameCam ? "true" : "false") + ",\"dealAnim\":" + (useDealAnim ? "true" : "false") + "}");
            // #endregion
        }
    }

    void Update()
    {
        if (!isPlayerControlledBoard || HasUiSlots) return;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            HandleInteraction();
        }
    }

    void HandleInteraction()
    {
        var cam = GameplayCameraOrMain();
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            Slot3D clickedSlot = hit.collider.GetComponentInParent<Slot3D>();
            if (clickedSlot != null)
            {
                if (slots == null) return;
                int idx = slots.IndexOf(clickedSlot);
                if (idx != -1) OnSlotClicked(idx);
            }
        }
    }

    static bool IsVerticalSlot(int idx) => (idx >= 0 && idx <= 4) || (idx >= 12 && idx <= 16);

    static Quaternion RotationForSlot(int idx) => Quaternion.identity;

    bool TryCreateCardModelForSlot(int idx, string cardToPlace, out GameObject model, out Quaternion rotation)
    {
        model = null;
        rotation = RotationForSlot(idx);

        if (IsVerticalSlot(idx))
        {
            GameObject prefabV = GetModelPrefab(cardToPlace, false);
            if (prefabV != null)
                model = Instantiate(prefabV);
        }
        else
        {
            GameObject prefabH = GetModelPrefab(cardToPlace, true);
            if (prefabH != null)
                model = Instantiate(prefabH);
        }

        return model != null;
    }

    void OnSlotClicked(int idx)
    {
        if (StreamsCardDrawCinematic.IsBlockingPlacement)
            return;
        if (!HasUiSlots && _previewAnimating)
            return;
        if (pendingCard == null)
            return;
        if (!HasUiSlots && currentSpawnedModel == null)
            return;
        if (idx < 0 || idx >= SlotCount)
            return;
        if (IsSlotFilled(idx))
            return;
        if (_placementRestricted && idx != _allowedPlacementSlot)
            return;

        string cardToPlace = pendingCard;
        pendingCard = null;

        if (HasUiSlots && idx < uiSlots.Count && uiSlots[idx] != null)
            uiSlots[idx].SetFilled(cardToPlace);

        ClearUiHints();
        ClearHoldingPreview();

        if (!HasUiSlots && slots != null && idx < slots.Count && slots[idx] != null && !slots[idx].isFilled)
        {
            if (TryCreateCardModelForSlot(idx, cardToPlace, out GameObject placedModel, out Quaternion rotation))
            {
                if (currentSpawnedModel != null)
                    Destroy(currentSpawnedModel);
                currentSpawnedModel = placedModel;
                slots[idx].PlaceExistingObject(currentSpawnedModel, cardToPlace, rotation);
            }
            else if (currentSpawnedModel != null)
            {
                slots[idx].PlaceExistingObject(currentSpawnedModel, cardToPlace, RotationForSlot(idx));
            }
        }
        else if (currentSpawnedModel != null)
        {
            Destroy(currentSpawnedModel);
        }

        currentSpawnedModel = null;

        if (drawButton != null)
            drawButton.interactable = true;

        UpdateCurrentScore();
        CheckAllFilled();

        if (isPlayerControlledBoard)
            OnPlayerCardPlaced?.Invoke();
    }

    /// <summary>AI·스크립트용: 이미 확정된 칸에 카드 모델을 올립니다. (ReceiveCard 없이 drawnCount만 증가)</summary>
    public void PlaceCardFromAI(int idx, string cardToPlace)
    {
        if (idx < 0 || idx >= SlotCount || IsSlotFilled(idx)) return;

        if (HasUiSlots && idx < uiSlots.Count && uiSlots[idx] != null)
            uiSlots[idx].SetFilled(cardToPlace);

        if (slots != null && idx < slots.Count && slots[idx] != null && !slots[idx].isFilled)
        {
            if (!TryCreateCardModelForSlot(idx, cardToPlace, out GameObject model, out Quaternion rotation))
            {
                drawnCount++;
                UpdateCurrentScore();
                CheckAllFilled();
                return;
            }

            if (!isPlayerControlledBoard && _aiLastBrightSlot != null)
                _aiLastBrightSlot.SetCardModelBright(false);

            slots[idx].PlaceExistingObject(model, cardToPlace, rotation);

            if (!isPlayerControlledBoard)
            {
                _aiLastBrightSlot = slots[idx];
                _aiLastBrightSlot.SetCardModelBright(true);
            }
        }

        drawnCount++;

        UpdateCurrentScore();
        CheckAllFilled();
    }

    /// <summary>AI가 고른 각 칸의 선택 비율(%)을 빈 슬롯 위에 표시합니다. 이전 턴에 설치된 칸은 숨깁니다.</summary>
    public void ShowSlotProbabilities(float[] slotPercentages)
    {
        if (slots == null || slotPercentages == null)
            return;

        BindSlotProbabilityLabels();

        int count = Mathf.Min(slots.Count, slotPercentages.Length);
        var emptySlots = new List<(int index, float percent)>();
        for (int i = 0; i < count; i++)
        {
            if (slots[i] == null || slots[i].isFilled)
                continue;

            emptySlots.Add((i, slotPercentages[i]));
        }

        emptySlots.Sort((a, b) => a.percent.CompareTo(b.percent));

        var opacityRelativeByIndex = new Dictionary<int, float>();
        var colorRelativeByIndex = new Dictionary<int, float>();
        var isHighestRankByIndex = new Dictionary<int, bool>();
        int emptyCount = emptySlots.Count;
        int rank = 0;
        while (rank < emptyCount)
        {
            int tieEnd = rank;
            float tiedPercent = emptySlots[tieEnd].percent;
            while (tieEnd < emptyCount && Mathf.Approximately(emptySlots[tieEnd].percent, tiedPercent))
                tieEnd++;

            float avgRank = (rank + tieEnd - 1) * 0.5f;
            bool isHighestRank = tieEnd == emptyCount;

            float opacityRelative = emptyCount <= 1
                ? 1f
                : avgRank / (emptyCount - 1);

            float colorRelative = 1f;
            if (!isHighestRank)
            {
                colorRelative = emptyCount <= 2
                    ? 1f
                    : avgRank / (emptyCount - 2);
            }

            for (int j = rank; j < tieEnd; j++)
            {
                int slotIndex = emptySlots[j].index;
                opacityRelativeByIndex[slotIndex] = opacityRelative;
                colorRelativeByIndex[slotIndex] = colorRelative;
                isHighestRankByIndex[slotIndex] = isHighestRank;
            }

            rank = tieEnd;
        }

        for (int i = 0; i < count; i++)
        {
            if (slots[i] == null)
                continue;

            if (slots[i].isFilled)
                slots[i].ClearProbabilityLabel();
            else
                slots[i].SetProbabilityPercent(
                    slotPercentages[i],
                    opacityRelativeByIndex[i],
                    colorRelativeByIndex[i],
                    isHighestRankByIndex[i]);
        }
    }

    public void ClearSlotProbabilities()
    {
        if (slots == null)
            return;

        foreach (var slot in slots)
            slot?.ClearProbabilityLabel();
    }

    public Transform GetProbabilityLabelRoot()
    {
        if (IsValidProbabilityLabelRoot(aiProbabilityRoot))
            return aiProbabilityRoot;

        Transform boardAnchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(transform);
        if (boardAnchor != null)
        {
            foreach (Transform child in boardAnchor)
            {
                if (child.name == ProbabilityLabelRootName && IsValidProbabilityLabelRoot(child))
                {
                    aiProbabilityRoot = child;
                    return child;
                }
            }
        }

        Transform parent = boardAnchor != null ? boardAnchor : transform;
        var go = new GameObject(ProbabilityLabelRootName);
        go.transform.SetParent(parent, false);
        aiProbabilityRoot = go.transform;
        return aiProbabilityRoot;
    }

    /// <summary>Canvas 하위가 아닌 3D 라벨 루트.</summary>
    public static bool IsValidProbabilityLabelRoot(Transform root)
    {
        if (root == null)
            return false;

        return root.GetComponentInParent<Canvas>() == null;
    }

    void BindSlotProbabilityLabels()
    {
        if (isPlayerControlledBoard || _probabilityBindingsDone || slots == null)
            return;

        Transform labelRoot = GetProbabilityLabelRoot();
        DestroyMisplacedProbabilityLabels(labelRoot);

        foreach (var slot in slots)
            slot?.BindProbabilityLabelRoot(labelRoot, this);

        _probabilityBindingsDone = true;
    }

    static void DestroyMisplacedProbabilityLabels(Transform labelRoot)
    {
        var stale = new List<GameObject>();

        foreach (var root in UnityEngine.Object.FindObjectsByType<num_path>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (root.isPlayerControlledBoard)
                continue;

            Transform boardAnchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(root.transform);
            if (boardAnchor == null)
                continue;

            foreach (Transform t in boardAnchor.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("AiProbabilityLabel_", StringComparison.Ordinal))
                    continue;

                if (labelRoot != null && t.IsChildOf(labelRoot))
                    continue;

                stale.Add(t.gameObject);
            }
        }

        foreach (var go in stale)
        {
            if (go != null)
                Destroy(go);
        }
    }

    public Vector3 GetBoardCenterWorld()
    {
        if (slots == null || slots.Count == 0)
            return transform.position;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var slot in slots)
        {
            if (slot == null)
                continue;

            sum += slot.transform.position;
            count++;
        }

        return count > 0 ? sum / count : transform.position;
    }

    void UpdateCurrentScore()
    {
        int currentScore = GetBoardScore();
        if (scoreText2 != null)
            scoreText2.text = $"현재 점수: {currentScore}";
        SetUiScore(currentScore, popIfGained: true);
        RefreshUiRunOutlines();
    }

    void BindUiScoreLabel()
    {
        if (uiScoreLabel != null)
            return;

        string objectName = isPlayerControlledBoard ? PlayerScoreObjectName : AiScoreObjectName;
        uiScoreLabel = FindNamedTmp(objectName);
        if (uiScoreLabel != null)
            SetUiScore(GetBoardScore());
    }

    void SetUiScore(int score, bool popIfGained = false)
    {
        if (uiScoreLabel == null)
            BindUiScoreLabel();
        if (uiScoreLabel == null)
            return;

        CaptureUiScoreScaleIfNeeded();
        uiScoreLabel.text = score.ToString() + "점";

        bool gained = _hasDisplayedUiScore && score > _displayedUiScore;
        _displayedUiScore = score;
        _hasDisplayedUiScore = true;

        if (popIfGained && gained)
            PlayScorePop();
    }

    void CaptureUiScoreScaleIfNeeded()
    {
        if (_uiScoreScaleCaptured || uiScoreLabel == null)
            return;

        _uiScoreBaseScale = uiScoreLabel.transform.localScale;
        _uiScoreScaleCaptured = true;
    }

    void PlayScorePop()
    {
        if (uiScoreLabel == null)
            return;

        MonoBehaviour host = uiScoreLabel;
        if (!host.isActiveAndEnabled)
            return;

        if (_scorePop != null)
            host.StopCoroutine(_scorePop);

        uiScoreLabel.transform.localScale = _uiScoreBaseScale;
        _scorePop = host.StartCoroutine(ScorePopRoutine(uiScoreLabel.transform));
    }

    IEnumerator ScorePopRoutine(Transform target)
    {
        Vector3 from = _uiScoreBaseScale;
        Vector3 peak = from * 1.35f;
        const float upDuration = 0.08f;
        const float downDuration = 0.16f;

        float t = 0f;
        while (t < upDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / upDuration));
            target.localScale = Vector3.LerpUnclamped(from, peak, u);
            yield return null;
        }

        t = 0f;
        while (t < downDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / downDuration));
            target.localScale = Vector3.LerpUnclamped(peak, from, u);
            yield return null;
        }

        target.localScale = from;
        _scorePop = null;
    }

    static TextMeshProUGUI FindNamedTmp(string objectName)
    {
        TextMeshProUGUI fallback = null;
        foreach (var tmp in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tmp == null || tmp.name != objectName)
                continue;

            if (IsUnderNamedParent(tmp.transform, LegacyBoardsRootName))
                continue;

            if (IsUnderNamedParent(tmp.transform, BoardCanvasName))
                return tmp;

            fallback = tmp;
        }

        return fallback;
    }

    static bool IsUnderNamedParent(Transform start, string parentName)
    {
        for (Transform t = start; t != null; t = t.parent)
        {
            if (t.name == parentName)
                return true;
        }

        return false;
    }

    void RefreshUiRunOutlines()
    {
        if (!HasUiSlots)
            return;

        var cards = new List<string>();
        int n = SlotCount;
        for (int i = 0; i < n; i++)
            cards.Add(GetSlotCardValue(i) ?? "");

        StreamsRunOutlineOverlay.Refresh(uiSlots, StreamsAscendingRuns.FromCards(cards));
    }

    /// <summary>슬롯 상태 기준 최종 Streams 점수(조커 규칙 포함).</summary>
    public int GetBoardScore()
    {
        var currentCards = new List<string>();
        int n = SlotCount;
        if (n <= 0)
            return 0;

        for (int i = 0; i < n; i++)
            currentCards.Add(GetSlotCardValue(i) ?? "");

        return CalculateScore(currentCards);
    }

    int CalculateScore(List<string> cards)
{
    // J 위치 찾기
    int jokerIdx = -1;
    for (int i = 0; i < cards.Count; i++)
    {
        if (cards[i].Trim().ToUpper() == "J")
        {
            jokerIdx = i;
            break;
        }
    }

    // J가 없으면 기존 방식
    if (jokerIdx == -1)
        return CalculateScoreNoJoker(cards);

    // J를 앞 run에 붙였을 때 점수
    List<string> frontList = new List<string>(cards);
    frontList[jokerIdx] = StreamsAscendingRuns.CopyNeighborValue(cards, jokerIdx - 1);
    int frontScore = CalculateScoreNoJoker(frontList);

    // J를 뒤 run에 붙였을 때 점수
    List<string> backList = new List<string>(cards);
    backList[jokerIdx] = StreamsAscendingRuns.CopyNeighborValue(cards, jokerIdx + 1);
    int backScore = CalculateScoreNoJoker(backList);

    return Mathf.Max(frontScore, backScore);
}

int CalculateScoreNoJoker(List<string> cards)
{
    int totalScore = 0;
    int currentRun = 0;
    int prevValue = -1;

    for (int i = 0; i < cards.Count; i++)
    {
        string clean = cards[i].Trim().ToUpper();
        if (string.IsNullOrEmpty(clean))
        {
            if (currentRun > 0) totalScore += GetScore(currentRun);
            currentRun = 0;
            prevValue = -1;
            continue;
        }

        if (int.TryParse(clean, out int value))
        {
            if (currentRun > 0 && prevValue != -1 && value < prevValue)
            {
                totalScore += GetScore(currentRun);
                currentRun = 1;
            }
            else
            {
                currentRun++;
            }
            prevValue = value;
        }
    }
    if (currentRun > 0) totalScore += GetScore(currentRun);
    return totalScore;
}

    void HighlightAscendingRuns(List<string> cards)
{
    foreach (var slot in slots)
    {
        if (slot != null) slot.SetColor(Color.white);
    }

    // J 위치 찾기
    int jokerIdx = -1;
    for (int i = 0; i < cards.Count; i++)
    {
        if (cards[i].Trim().ToUpper() == "J")
        {
            jokerIdx = i;
            break;
        }
    }

    // J를 앞/뒤 중 더 점수 높은 쪽으로 교체한 리스트 만들기
    List<string> processedCards = new List<string>(cards);
    if (jokerIdx != -1)
    {
        List<string> frontList = new List<string>(cards);
        frontList[jokerIdx] = StreamsAscendingRuns.CopyNeighborValue(cards, jokerIdx - 1);
        int frontScore = CalculateScoreNoJoker(frontList);

        List<string> backList = new List<string>(cards);
        backList[jokerIdx] = StreamsAscendingRuns.CopyNeighborValue(cards, jokerIdx + 1);
        int backScore = CalculateScoreNoJoker(backList);

        processedCards = (frontScore >= backScore) ? frontList : backList;
    }

    int runStart = 0;
    int runIndex = 0;
    int prevValue = -1;

    for (int i = 0; i < processedCards.Count; i++)
    {
        string clean = processedCards[i].Trim().ToUpper();
        if (string.IsNullOrEmpty(clean))
        {
            ApplyRunColor(runStart, i - 1, StreamsAscendingRuns.RunColor(runIndex++));
            runStart = i + 1;
            prevValue = -1;
            continue;
        }

        if (int.TryParse(clean, out int value))
        {
            if (prevValue != -1 && value < prevValue)
            {
                ApplyRunColor(runStart, i - 1, StreamsAscendingRuns.RunColor(runIndex++));
                runStart = i;
            }
            prevValue = value;
        }
    }
    ApplyRunColor(runStart, processedCards.Count - 1, StreamsAscendingRuns.RunColor(runIndex));
}

    void ApplyRunColor(int start, int end, Color color)
    {
        if (start > end) return;
        if (slots == null) return;

        for (int i = start; i <= end; i++)
        {
            if (i >= 0 && i < slots.Count && slots[i] != null && slots[i].isFilled)
                slots[i].SetColor(color);
        }
    }

    void CheckAllFilled()
    {
        int n = SlotCount;
        if (n <= 0)
            return;

        for (int i = 0; i < n; i++)
        {
            if (!IsSlotFilled(i))
                return;
        }

        if (!isPlayerControlledBoard)
        {
            if (_aiLastBrightSlot != null)
            {
                _aiLastBrightSlot.SetCardModelBright(false);
                _aiLastBrightSlot = null;
            }
        }

        List<string> currentCards = new List<string>();
        for (int i = 0; i < n; i++)
            currentCards.Add(GetSlotCardValue(i) ?? "");

        if (!isPlayerControlledBoard)
        {
            Transform boardAnchor = StreamsBoardCameraPose.TryFindGameBoardAnchor(transform);
            StreamsBoardBackgroundDimmer.Instance?.RestoreTilesForBoard(boardAnchor);
        }

        HighlightAscendingRuns(currentCards);

        int finalScore = GetBoardScore();
        if (isPlayerControlledBoard)
            StreamsGameResults.SetPlayerFinalScore(finalScore);

        if (scoreText != null && scoreText2 != null)
            scoreText.text = "최종 " + scoreText2.text;
        if (drawButton != null)
            drawButton.interactable = false;
    }

    GameObject GetModelPrefab(string val, bool horizontal)
    {
        var match = numberPrefabs.Find(m => m.value == val);
        if (match == null) return null;
        return horizontal ? match.prefabH : match.prefab;
    }

    int GetScore(int length)
    {
        if (length <= 0) return 0;
        return scoreTable[Mathf.Clamp(length, 0, scoreTable.Length - 1)];
    }
}

// #region agent log
static class StreamsDebug9b
{
    public static void Line(string hypothesisId, string location, string message, string dataJson)
    {
    }
}

static class StreamsAgentLog
{
    public static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
    }

    public static void Line(string hypothesisId, string location, string message, string dataJson)
    {
    }
}
// #endregion