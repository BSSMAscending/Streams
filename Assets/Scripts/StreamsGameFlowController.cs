using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 1라운드: 플레이어 팔이 카메라 → 카드 자동 뽑기 → 플레이어 배치까지 대기.
/// 이어서 AI 판 세 곳을 순서대로 카메라로 보여 주며 추론 배치 → 다시 플레이어 팔.
/// randomoutnum은 Awake에서 비활성화되며, 덱은 이 컴포넌트가 동일 규칙으로 관리합니다.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class StreamsGameFlowController : MonoBehaviour
{
    [Header("필수 연결")]
    public Camera mainCamera;
    public num_path playerBoard;
    public num_path[] aiBoards = new num_path[3];
    [Tooltip("AI 판 1·2·3 순서. 기본은 MCTS(useMcts); ONNX 쓰려면 해당 컴포넌트에서 useMcts 끄고 모델 할당.")]
    public StreamsAIController[] aiModels = new StreamsAIController[3];

    [Header("카메라 뷰 (레거시: 자동 탑다운 사용 시 미사용)")]
    public Transform playerCameraView;
    public Transform[] aiCameraViews = new Transform[3];

    [Header("카메라 (GameBoard_0~3 기준)")]
    [Tooltip("GameBoard 피벗 기준 로컬 위치. 사진2는 GameBoard_3와 동일 월드 좌표이므로 보통 (0,0,0). GameBoard 부모가 없을 때만 아래 탑다운 대체값을 씁니다.")]
    public Vector3 cameraLocalPosition = Vector3.zero;
    [Tooltip("각 GameBoard 로컬 공간에서 카메라 오일러 각 (사진2: 90, 0, 0).")]
    public Vector3 cameraLocalEuler = new Vector3(90f, 0f, 0f);

    [Header("탑다운 카메라 (GameBoard 부모 없을 때만)")]
    [Tooltip("보드에서 카메라까지 최소 거리(보드 평면에 수직). 바운드가 크면 자동으로 더 올라갑니다.")]
    public float topDownMinHeightAboveBoard = 38f;
    [Tooltip("바운드 반경(가로·세로 중 큰 값)에 곱해 여유를 둡니다.")]
    public float topDownExtentScale = 1.15f;

    [Header("타이밍")]
    public float cameraMoveDuration = 0.65f;
    public float delayAfterCameraArrive = 0.12f;
    public float delayAfterAIPlacement = 0.4f;

    [Header("AI 입력(학습 파이프라인과 맞추기)")]
    [Tooltip("빈 칸: -1. 조커 카드의 정수 표현.")]
    public int jokerModelValue = 21;

    [Header("게임 종료")]
    public string endSceneName = "EndScene";
    public float endSceneDelay = 1.5f;

    bool _waitingForPlayer;

    void Awake()
    {
        foreach (var r in FindObjectsByType<randomoutnum>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            r.enabled = false;
    }

    void Start()
    {
        if (playerBoard != null)
        {
            playerBoard.isPlayerControlledBoard = true;
            playerBoard.OnPlayerCardPlaced += OnPlayerCardPlaced;
        }

        if (aiBoards != null)
        {
            foreach (var ai in aiBoards)
            {
                if (ai != null) ai.isPlayerControlledBoard = false;
            }
        }

        if (mainCamera != null && playerBoard != null)
            ApplyTopDownCamera(playerBoard);

        StartCoroutine(GameLoopRoutine());
    }

    void OnDestroy()
    {
        if (playerBoard != null)
            playerBoard.OnPlayerCardPlaced -= OnPlayerCardPlaced;
    }

    void OnPlayerCardPlaced()
    {
        _waitingForPlayer = false;
    }

    IEnumerator GameLoopRoutine()
    {
        var deck = BuildDeck();
        if (playerBoard == null)
        {
            Debug.LogError("StreamsGameFlowController: playerBoard가 없습니다.");
            yield break;
        }

        for (int round = 0; round < num_path.DRAW_LIMIT; round++)
        {
            if (deck.Count == 0)
            {
                Debug.LogWarning("덱이 비어 라운드를 중단합니다.");
                break;
            }

            yield return MoveCameraToBoard(playerBoard);

            int pick = Random.Range(0, deck.Count);
            string currentCard = deck[pick];
            deck.RemoveAt(pick);

            _waitingForPlayer = true;
            // #region agent log
            StreamsAgentLog.Line("H00", "StreamsGameFlow.GameLoopRoutine", "before playerBoard.ReceiveCard", $"{{\"currentCard\":\"{StreamsAgentLog.Esc(currentCard)}\"}}");
            // #endregion
            playerBoard.ReceiveCard(currentCard);
            while (_waitingForPlayer)
                yield return null;

            for (int a = 0; a < aiBoards.Length; a++)
            {
                var ai = aiBoards[a];
                if (ai == null) continue;

                yield return MoveCameraToBoard(ai);

                StreamsAIController model = (aiModels != null && a < aiModels.Length) ? aiModels[a] : null;
                int slot = PickAiSlot(ai, currentCard, model, deck);
                if (slot >= 0)
                    ai.PlaceCardFromAI(slot, currentCard);

                yield return new WaitForSeconds(delayAfterAIPlacement);
            }
        }

        yield return MoveCameraToBoard(playerBoard);

        StreamsGameResults.SaveFromBoards(playerBoard, aiBoards);

        if (endSceneDelay > 0f)
            yield return new WaitForSeconds(endSceneDelay);

        if (!string.IsNullOrWhiteSpace(endSceneName))
            SceneManager.LoadScene(endSceneName);
    }

    static List<string> BuildDeck()
    {
        return new List<string>
        {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
            "11", "12", "13", "14", "15", "16", "17", "18", "19",
            "11", "12", "13", "14", "15", "16", "17", "18", "19",
            "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "J"
        };
    }

    int PickAiSlot(num_path board, string card, StreamsAIController aiController, List<string> remainingDeckStrings)
    {
        if (board == null || board.slots == null || board.slots.Count == 0) return -1;

        int n = Mathf.Min(20, board.slots.Count);
        var state = new int[20];
        for (int i = 0; i < 20; i++)
        {
            if (i >= n || board.slots[i] == null || !board.slots[i].isFilled)
                state[i] = -1;
            else
                state[i] = CardStringToModelInt(board.slots[i].cardValue);
        }

        int emptyCount = 0;
        for (int i = 0; i < 20; i++)
        {
            if (state[i] == -1) emptyCount++;
        }

        int futureTileCount = Mathf.Max(0, emptyCount - 1);

        var remainingMcts = new List<int>(remainingDeckStrings != null ? remainingDeckStrings.Count : 0);
        if (remainingDeckStrings != null)
        {
            foreach (var s in remainingDeckStrings)
                remainingMcts.Add(CardStringToMctsInt(s));
        }

        int newTile = CardStringToModelInt(card);
        int best = aiController != null
            ? aiController.GetBestPosition(state, newTile, remainingMcts, futureTileCount, jokerModelValue)
            : -1;

        if (best < 0 || best >= board.slots.Count || board.slots[best] == null || board.slots[best].isFilled)
            best = FirstEmptySlotIndex(board);

        return best;
    }

    /// <summary>MCTS·Python 규약: 조커 0 (모델 입력용 <see cref="jokerModelValue"/>와 별개).</summary>
    int CardStringToMctsInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        s = s.Trim().ToUpper();
        if (s == "J") return 0;
        if (int.TryParse(s, out int v)) return v;
        return 0;
    }

    int FirstEmptySlotIndex(num_path board)
    {
        for (int i = 0; i < board.slots.Count; i++)
        {
            if (board.slots[i] != null && !board.slots[i].isFilled) return i;
        }

        return -1;
    }

    int CardStringToModelInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        s = s.Trim().ToUpper();
        if (s == "J") return jokerModelValue;
        if (int.TryParse(s, out int v)) return v;
        return 0;
    }

    void ApplyTopDownCamera(num_path board)
    {
        StreamsBoardCameraPose.GetCameraPose(
            board.transform,
            board.slots,
            cameraLocalPosition,
            cameraLocalEuler,
            topDownMinHeightAboveBoard,
            topDownExtentScale,
            out Vector3 pos,
            out Quaternion rot);
        mainCamera.transform.SetPositionAndRotation(pos, rot);
    }

    IEnumerator MoveCameraToBoard(num_path board)
    {
        if (mainCamera == null || board == null) yield break;

        StreamsBoardCameraPose.GetCameraPose(
            board.transform,
            board.slots,
            cameraLocalPosition,
            cameraLocalEuler,
            topDownMinHeightAboveBoard,
            topDownExtentScale,
            out Vector3 p1,
            out Quaternion r1);

        var cam = mainCamera.transform;
        Vector3 p0 = cam.position;
        Quaternion r0 = cam.rotation;
        float dur = Mathf.Max(0.05f, cameraMoveDuration);
        float elapsed = 0f;

        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            t = t * t * (3f - 2f * t);
            cam.position = Vector3.Lerp(p0, p1, t);
            cam.rotation = Quaternion.Slerp(r0, r1, t);
            yield return null;
        }

        cam.SetPositionAndRotation(p1, r1);
        yield return new WaitForSeconds(delayAfterCameraArrive);
    }
}
