using System;
using System.Collections;
using System.IO;
using System.Text;
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
    public List<Slot3D> slots;

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

    /// <summary>플레이어가 이번에 뽑은 카드를 칸에 놓았을 때 한 번 호출됩니다.</summary>
    public event System.Action OnPlayerCardPlaced;

    public const int DRAW_LIMIT = 20;
    string pendingCard = null;
    int drawnCount = 0;

    private GameObject currentSpawnedModel = null;
    Camera _flowMainCamera;
    bool _previewAnimating;
    Coroutine _dealCoroutine;

    int[] scoreTable = new int[]
    {
        0, 1, 3, 5, 7, 9, 11, 15, 20, 25,
        30, 35, 40, 50, 60, 70, 85, 100, 150, 300
    };

    void Awake()
    {
        var flow = UnityEngine.Object.FindFirstObjectByType<StreamsGameFlowController>();
        if (flow != null)
            _flowMainCamera = flow.mainCamera;
    }

    Camera GameplayCameraOrMain() => _flowMainCamera != null ? _flowMainCamera : Camera.main;

    void Start()
    {
        if (drawButton == null || scoreText == null || scoreText2 == null)
            Debug.LogError("UI 구성요소가 연결되지 않았습니다!");
        if (holdingPoint == null && StreamsBoardCameraPose.TryFindGameBoardAnchor(transform) == null)
            Debug.LogError("num_path: holdingPoint가 비어 있고 GameBoard_0~3 앵커도 없어 미리보기 위치를 찾을 수 없습니다.");
        else if (holdingPoint == null)
            Debug.LogWarning("num_path: holdingPoint 미지정 — 런타임에 이 판의 GameBoard_x 하위에서 이름 'holdingPoint'를 찾습니다.");
        if (numberPrefabs == null || numberPrefabs.Count == 0)
            Debug.LogError("num_path: numberPrefabs가 비어 있어 카드 모델을 생성할 수 없습니다.");

        scoreText.text = "";
        scoreText2.text = "현재 점수: 0";

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null)
            {
                float slotScale = cardSlotExtraScale > 0.0001f ? cardSlotExtraScale : cardPreviewExtraScale;
                slots[i].placedModelExtraScale = slotScale;
                slots[i].SetEmpty();
            }
        }
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

    static Vector3 CompensatedLocalScaleForParent(Vector3 prefabLocalScale, Vector3 parentLossy)
    {
        return new Vector3(
            prefabLocalScale.x / Mathf.Max(Mathf.Abs(parentLossy.x), 1e-6f),
            prefabLocalScale.y / Mathf.Max(Mathf.Abs(parentLossy.y), 1e-6f),
            prefabLocalScale.z / Mathf.Max(Mathf.Abs(parentLossy.z), 1e-6f));
    }

    void ApplyPreviewExtraScaleMultiply(Transform card)
    {
        if (Mathf.Abs(cardPreviewExtraScale - 1f) > 1e-4f)
            card.localScale *= cardPreviewExtraScale;
    }

    void ApplyPreviewScaleAfterParent(Transform card, Transform spawnParent, Vector3 prefabLocalScale)
    {
        card.localScale = CompensatedLocalScaleForParent(prefabLocalScale, spawnParent.lossyScale);
        ApplyPreviewExtraScaleMultiply(card);
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
        ApplyPreviewScaleAfterParent(poseRef.transform, spawnParent, prefabLocalScale);
        worldPos = poseRef.transform.position;
        worldRot = poseRef.transform.rotation;
        Destroy(poseRef);
    }

    void SpawnPreviewInstant(GameObject prefab, Transform spawnParent)
    {
        Vector3 prefabLocalScale = prefab.transform.localScale;
        currentSpawnedModel = Instantiate(prefab, spawnParent.position, spawnParent.rotation);
        ApplyPreviewLocalPose(currentSpawnedModel.transform, spawnParent);
        // #region agent log
        LogPreviewScale("H-A", "num_path.SpawnPreviewInstant", "afterParent_beforeScale", currentSpawnedModel.transform, spawnParent, prefabLocalScale, false);
        // #endregion
        ApplyPreviewExtraScaleMultiply(currentSpawnedModel.transform);
        // #region agent log
        LogPreviewScale("H-A", "num_path.SpawnPreviewInstant", "afterMultiplyScale", currentSpawnedModel.transform, spawnParent, prefabLocalScale, false);
        // #endregion
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
        // #region agent log
        LogPreviewScale("H-C", "num_path.AnimateCardPreviewDeal", "beforeFinalScaleApply", tr, spawnParent, prefabLocalScale, true);
        // #endregion
        ApplyPreviewScaleAfterParent(tr, spawnParent, prefabLocalScale);
        // #region agent log
        LogPreviewScale("H-C", "num_path.AnimateCardPreviewDeal", "afterMultiplyScale", tr, spawnParent, prefabLocalScale, true);
        // #endregion
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

        drawButton.interactable = false;

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
            // #region agent log
            LogPreviewScale("H-D", "num_path.ReceiveCard", "deal_afterParent_beforeScale", currentSpawnedModel.transform, spawnParent, prefabLocalScale, true);
            // #endregion
            ApplyPreviewExtraScaleMultiply(currentSpawnedModel.transform);
            // #region agent log
            LogPreviewScale("H-D", "num_path.ReceiveCard", "deal_afterMultiplyScale", currentSpawnedModel.transform, spawnParent, prefabLocalScale, true);
            // #endregion

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
        if (!isPlayerControlledBoard) return;
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

    void OnSlotClicked(int idx)
{
    if (_previewAnimating) return;
    if (pendingCard == null || currentSpawnedModel == null) return;
    if (idx < 0 || idx >= slots.Count || slots[idx] == null) return;
    if (slots[idx].isFilled) return;

    string cardToPlace = pendingCard;
    pendingCard = null;

    bool isHorizontal = (idx >= 3 && idx <= 9) || (idx >= 14 && idx <= 19);
    bool isVertical = (idx >= 10 && idx <= 13);

    Quaternion rotation = Quaternion.identity;

    if (isHorizontal)
    {
        GameObject prefabH = GetModelPrefab(cardToPlace, true);
        if (prefabH != null)
        {
            Destroy(currentSpawnedModel);
            currentSpawnedModel = Instantiate(prefabH);
        }
    }
    else if (isVertical)
    {
        GameObject prefabV = GetModelPrefab(cardToPlace, false);
        if (prefabV != null)
        {
            Destroy(currentSpawnedModel);
            currentSpawnedModel = Instantiate(prefabV);
            rotation = Quaternion.Euler(0, 0, 180);
        }
    }
    else
    {
        GameObject prefabDefault = GetModelPrefab(cardToPlace, false);
        if (prefabDefault != null)
        {
            Destroy(currentSpawnedModel);
            currentSpawnedModel = Instantiate(prefabDefault);
        }
    }

    slots[idx].PlaceExistingObject(currentSpawnedModel, cardToPlace, rotation); // 한번만 호출

    currentSpawnedModel = null;

    drawButton.interactable = true;

    UpdateCurrentScore();
    CheckAllFilled();

    if (isPlayerControlledBoard)
        OnPlayerCardPlaced?.Invoke();
}

    /// <summary>AI·스크립트용: 이미 확정된 칸에 카드 모델을 올립니다. (ReceiveCard 없이 drawnCount만 증가)</summary>
    public void PlaceCardFromAI(int idx, string cardToPlace)
    {
        if (idx < 0 || idx >= slots.Count || slots[idx] == null || slots[idx].isFilled) return;

        bool isHorizontal = (idx >= 3 && idx <= 9) || (idx >= 14 && idx <= 19);
        bool isVertical = (idx >= 10 && idx <= 13);
        Quaternion rotation = Quaternion.identity;
        GameObject model = null;

        if (isHorizontal)
        {
            GameObject prefabH = GetModelPrefab(cardToPlace, true);
            if (prefabH != null)
            {
                model = Instantiate(prefabH);
            }
        }
        else if (isVertical)
        {
            GameObject prefabV = GetModelPrefab(cardToPlace, false);
            if (prefabV != null)
            {
                model = Instantiate(prefabV);
                rotation = Quaternion.Euler(0, 0, 180);
            }
        }
        else
        {
            GameObject prefab = GetModelPrefab(cardToPlace, false);
            if (prefab != null)
            {
                model = Instantiate(prefab);
            }
        }

        if (model == null) return;

        slots[idx].PlaceExistingObject(model, cardToPlace, rotation);
        drawnCount++;

        UpdateCurrentScore();
        CheckAllFilled();
    }
    void UpdateCurrentScore()
    {
        int currentScore = GetBoardScore();
        scoreText2.text = $"현재 점수: {currentScore}";
    }

    /// <summary>슬롯 상태 기준 최종 Streams 점수(조커 규칙 포함).</summary>
    public int GetBoardScore()
    {
        if (slots == null) return 0;

        var currentCards = new List<string>();
        foreach (var slot in slots)
        {
            if (slot != null) currentCards.Add(slot.isFilled ? slot.cardValue : "");
            else currentCards.Add("");
        }

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
    frontList[jokerIdx] = (jokerIdx > 0) ? cards[jokerIdx - 1] : "0";
    int frontScore = CalculateScoreNoJoker(frontList);

    // J를 뒤 run에 붙였을 때 점수
    List<string> backList = new List<string>(cards);
    backList[jokerIdx] = (jokerIdx < cards.Count - 1) ? cards[jokerIdx + 1] : "0";
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
        frontList[jokerIdx] = (jokerIdx > 0) ? cards[jokerIdx - 1] : "0";
        int frontScore = CalculateScoreNoJoker(frontList);

        List<string> backList = new List<string>(cards);
        backList[jokerIdx] = (jokerIdx < cards.Count - 1) ? cards[jokerIdx + 1] : "0";
        int backScore = CalculateScoreNoJoker(backList);

        processedCards = (frontScore >= backScore) ? frontList : backList;
    }

    Color[] runColors = new Color[] {
        new Color32(222, 218, 0, 255), new Color32(55, 115, 222, 255),
        new Color32(231, 7, 44, 255), new Color32(28, 231, 20, 255), new Color32(134, 17, 231, 255)
    };

    int runStart = 0;
    int runIndex = 0;
    int prevValue = -1;

    for (int i = 0; i < processedCards.Count; i++)
    {
        string clean = processedCards[i].Trim().ToUpper();
        if (string.IsNullOrEmpty(clean))
        {
            ApplyRunColor(runStart, i - 1, runColors[runIndex++ % runColors.Length]);
            runStart = i + 1;
            prevValue = -1;
            continue;
        }

        if (int.TryParse(clean, out int value))
        {
            if (prevValue != -1 && value < prevValue)
            {
                ApplyRunColor(runStart, i - 1, runColors[runIndex++ % runColors.Length]);
                runStart = i;
            }
            prevValue = value;
        }
    }
    ApplyRunColor(runStart, processedCards.Count - 1, runColors[runIndex % runColors.Length]);
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
        foreach (var slot in slots) if (!slot.isFilled) return;

        List<string> currentCards = new List<string>();
        foreach (var slot in slots)
        {
            if (slot != null) currentCards.Add(slot.isFilled ? slot.cardValue : "");
            else currentCards.Add("");
        }
        HighlightAscendingRuns(currentCards);

        scoreText.text = "최종 " + scoreText2.text;
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
        long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        var sb = new StringBuilder(384);
        sb.Append("{\"sessionId\":\"9b10aa\",\"hypothesisId\":\"").Append(StreamsAgentLog.Esc(hypothesisId));
        sb.Append("\",\"location\":\"").Append(StreamsAgentLog.Esc(location));
        sb.Append("\",\"message\":\"").Append(StreamsAgentLog.Esc(message));
        sb.Append("\",\"data\":").Append(string.IsNullOrEmpty(dataJson) ? "{}" : dataJson);
        sb.Append(",\"timestamp\":").Append(ts).Append("}\n");
        try { File.AppendAllText(Path.Combine(Application.dataPath, "..", "debug-9b10aa.log"), sb.ToString()); }
        catch { }
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
        long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        var sb = new StringBuilder(320);
        sb.Append("{\"sessionId\":\"a65c5d\",\"hypothesisId\":\"").Append(Esc(hypothesisId));
        sb.Append("\",\"location\":\"").Append(Esc(location));
        sb.Append("\",\"message\":\"").Append(Esc(message));
        sb.Append("\",\"data\":").Append(string.IsNullOrEmpty(dataJson) ? "{}" : dataJson);
        sb.Append(",\"timestamp\":").Append(ts).Append("}\n");
        string line = sb.ToString();
        try { File.AppendAllText(Path.Combine(Application.dataPath, "..", "debug-a65c5d.log"), line); }
        catch { }
        try { File.AppendAllText(Path.Combine(Application.persistentDataPath, "debug-a65c5d.log"), line); }
        catch { }
    }
}
// #endregion