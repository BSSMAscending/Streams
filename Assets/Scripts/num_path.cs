using System;
using System.Collections;
using TMPro;
using UnityEngine;
using System.Collections.Generic;

public class num_path : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("비우면 PlayerScoreText / AIScoreText 이름으로 찾습니다.")]
    public TextMeshProUGUI uiScoreLabel;
    [Tooltip("비우면 PlayerBoard/AIBoard의 Slots 자식에서 자동으로 찾습니다.")]
    public List<StreamsUiSlot> uiSlots;

    [Tooltip("플레이어만 클릭 입력을 받습니다. AI 판은 끄세요.")]
    public bool isPlayerControlledBoard = true;

    const string PlayerScoreObjectName = "PlayerScoreText";
    const string AiScoreObjectName = "AIScoreText";
    const string BoardCanvasName = "BoardCanvas";

    /// <summary>플레이어가 이번에 뽑은 카드를 칸에 놓았을 때 한 번 호출됩니다.</summary>
    public event System.Action OnPlayerCardPlaced;

    public const int DRAW_LIMIT = 20;
    string pendingCard = null;
    int drawnCount = 0;

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

    void Start()
    {
        BindUiScoreLabel();
        if (isPlayerControlledBoard && uiScoreLabel == null)
            Debug.LogError($"{name}: 플레이어 보드의 점수 라벨이 연결되지 않았습니다.");

        SetUiScore(0);
        BindUiSlots();
    }

    bool HasUiSlots => uiSlots != null && uiSlots.Count > 0;

    public int SlotCount => HasUiSlots ? uiSlots.Count : 0;

    public bool IsSlotFilled(int index)
    {
        return index < 0 || index >= SlotCount || uiSlots[index] == null || uiSlots[index].isFilled;
    }

    public string GetSlotCardValue(int index)
    {
        if (index < 0 || index >= SlotCount || uiSlots[index] == null || !uiSlots[index].isFilled)
            return null;
        return uiSlots[index].cardValue;
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

    public void ReceiveCard(string drawn)
    {
        if (drawnCount >= DRAW_LIMIT)
            return;

        pendingCard = drawn;
        drawnCount++;

        EnsureUiSlotsBound();
    }

    void OnSlotClicked(int idx)
    {
        if (StreamsCardDrawCinematic.IsBlockingPlacement)
            return;
        if (pendingCard == null)
            return;
        if (idx < 0 || idx >= SlotCount)
            return;
        if (IsSlotFilled(idx))
            return;
        if (_placementRestricted && idx != _allowedPlacementSlot)
            return;

        string cardToPlace = pendingCard;
        pendingCard = null;

        uiSlots[idx].SetFilled(cardToPlace);

        ClearUiHints();
        ClearHoldingPreview();

        UpdateCurrentScore();
        CheckAllFilled();

        if (isPlayerControlledBoard)
            OnPlayerCardPlaced?.Invoke();
    }

    /// <summary>AI·스크립트용: 이미 확정된 칸에 카드를 올립니다. (ReceiveCard 없이 drawnCount만 증가)</summary>
    public void PlaceCardFromAI(int idx, string cardToPlace)
    {
        if (idx < 0 || idx >= SlotCount || IsSlotFilled(idx)) return;

        uiSlots[idx].SetFilled(cardToPlace);
        drawnCount++;

        UpdateCurrentScore();
        CheckAllFilled();
    }

    void UpdateCurrentScore()
    {
        SetUiScore(GetBoardScore(), popIfGained: true);
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

        if (isPlayerControlledBoard)
            StreamsGameResults.SetPlayerFinalScore(GetBoardScore());
    }

    int GetScore(int length)
    {
        if (length <= 0) return 0;
        return scoreTable[Mathf.Clamp(length, 0, scoreTable.Length - 1)];
    }
}
