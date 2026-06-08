using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 4분할 화면 모드 (낙관적 업데이트).
/// 플레이어가 카드를 놓으면 즉시 다음 라운드로 진행.
/// AI 3명은 각자 독립 큐에서 비동기로 계산·배치.
/// 레이아웃: 플레이어 좌측 절반 / AI1·2·3 우측 1/3씩.
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

    [Header("4분할 카메라 (비워 두면 mainCamera 복제해 자동 생성)")]
    [Tooltip("0=플레이어(좌측절반), 1=AI1(우상), 2=AI2(우중), 3=AI3(우하). 비워 두면 자동 생성.")]
    public Camera[] boardCameras = new Camera[4];

    [Header("카메라 (GameBoard_0~3 기준)")]
    public Vector3 cameraLocalPosition = Vector3.zero;
    public Vector3 cameraLocalEuler = new Vector3(90f, 0f, 0f);

    [Header("탑다운 카메라 (GameBoard 부모 없을 때만)")]
    public float topDownMinHeightAboveBoard = 38f;
    public float topDownExtentScale = 1.15f;

    [Header("AI 입력(학습 파이프라인과 맞추기)")]
    [Tooltip("빈 칸: -1. 조커 카드의 정수 표현.")]
    public int jokerModelValue = 21;

    [Header("게임 종료")]
    public string endSceneName = "EndScene";
    public float endSceneDelay = 1.5f;

    // 플레이어: 좌측 절반 / AI1~3: 우측 열을 1/3씩
    static readonly Rect[] k_SplitRects = new Rect[]
    {
        new Rect(0f,   0.5f, 0.5f, 0.5f), // 0: 플레이어 (좌상)
        new Rect(0.5f, 0.5f, 0.5f, 0.5f), // 1: AI1      (우상)
        new Rect(0f,   0f,   0.5f, 0.5f), // 2: AI2      (좌하)
        new Rect(0.5f, 0f,   0.5f, 0.5f), // 3: AI3      (우하)
    };

    struct AiJob
    {
        public string card;
        public List<string> deckSnapshot;
    }

    Queue<AiJob>[] _aiQueues;
    bool _gameEnded;
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
            foreach (var ai in aiBoards)
                if (ai != null) ai.isPlayerControlledBoard = false;

        _aiQueues = new Queue<AiJob>[3];
        for (int i = 0; i < 3; i++)
        {
            _aiQueues[i] = new Queue<AiJob>();
            StartCoroutine(AiWorker(i));
        }

        SetupSplitScreenCameras();
        StartCoroutine(GameLoopRoutine());
    }

    void OnDestroy()
    {
        _gameEnded = true;
        if (playerBoard != null)
            playerBoard.OnPlayerCardPlaced -= OnPlayerCardPlaced;
    }

    void OnPlayerCardPlaced() => _waitingForPlayer = false;

    // ──────────────────────────────────────────────
    // 카메라 4분할 설정
    // ──────────────────────────────────────────────

    void SetupSplitScreenCameras()
    {
        if (mainCamera == null) return;

        if (boardCameras == null || boardCameras.Length != 4)
            boardCameras = new Camera[4];

        num_path[] allBoards = new num_path[4];
        allBoards[0] = playerBoard;
        for (int i = 0; i < 3; i++)
            allBoards[i + 1] = (aiBoards != null && i < aiBoards.Length) ? aiBoards[i] : null;

        for (int i = 0; i < 4; i++)
        {
            if (boardCameras[i] == null)
            {
                if (i == 0)
                {
                    boardCameras[0] = mainCamera;
                }
                else
                {
                    var go = new GameObject($"BoardCamera_{i}");
                    var cam = go.AddComponent<Camera>();
                    cam.clearFlags      = mainCamera.clearFlags;
                    cam.backgroundColor = mainCamera.backgroundColor;
                    cam.cullingMask     = mainCamera.cullingMask;
                    cam.fieldOfView     = mainCamera.fieldOfView;
                    cam.nearClipPlane   = mainCamera.nearClipPlane;
                    cam.farClipPlane    = mainCamera.farClipPlane;
                    cam.depth           = mainCamera.depth + i;
                    boardCameras[i]     = cam;
                }
            }

            boardCameras[i].rect = k_SplitRects[i];

            if (allBoards[i] != null)
                PositionCamera(boardCameras[i], allBoards[i]);
        }
    }

    void PositionCamera(Camera cam, num_path board)
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
        cam.transform.SetPositionAndRotation(pos, rot);
    }

    // ──────────────────────────────────────────────
    // 메인 게임 루프 (플레이어 전용, AI를 기다리지 않음)
    // ──────────────────────────────────────────────

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

            int pick = Random.Range(0, deck.Count);
            string currentCard = deck[pick];
            deck.RemoveAt(pick);

            _waitingForPlayer = true;
            // #region agent log
            StreamsAgentLog.Line("H00", "StreamsGameFlow.GameLoopRoutine", "before playerBoard.ReceiveCard", $"{{\"currentCard\":\"{StreamsAgentLog.Esc(currentCard)}\"}}");
            // #endregion
            playerBoard.ReceiveCard(currentCard);

            // 플레이어가 놓을 때까지만 대기
            while (_waitingForPlayer)
                yield return null;

            // AI 큐에 작업 추가 후 즉시 다음 라운드 진행 (낙관적 업데이트)
            var deckSnapshot = new List<string>(deck);
            for (int a = 0; a < aiBoards.Length; a++)
            {
                if (aiBoards[a] == null) continue;
                _aiQueues[a].Enqueue(new AiJob { card = currentCard, deckSnapshot = deckSnapshot });
            }
        }

        // 게임 루프 종료 — AI 큐가 모두 비워질 때까지 대기 후 결과 저장
        _gameEnded = true;
        yield return WaitForAllAiQueues();

        StreamsGameResults.SaveFromBoards(playerBoard, aiBoards);

        if (endSceneDelay > 0f)
            yield return new WaitForSeconds(endSceneDelay);

        if (!string.IsNullOrWhiteSpace(endSceneName))
            SceneManager.LoadScene(endSceneName);
    }

    IEnumerator WaitForAllAiQueues()
    {
        bool anyPending;
        do
        {
            anyPending = false;
            for (int a = 0; a < _aiQueues.Length; a++)
                if (_aiQueues[a].Count > 0) { anyPending = true; break; }
            if (anyPending) yield return null;
        } while (anyPending);
    }

    // ──────────────────────────────────────────────
    // AI 비동기 워커 (보드 1개에 1개 코루틴)
    // ──────────────────────────────────────────────

    IEnumerator AiWorker(int a)
    {
        while (true)
        {
            while (_aiQueues[a].Count == 0)
            {
                if (_gameEnded) yield break;
                yield return null;
            }

            var job = _aiQueues[a].Dequeue();
            var board = aiBoards[a];
            var model = (aiModels != null && a < aiModels.Length) ? aiModels[a] : null;

            // 현재 보드 상태 스냅샷 (메인 스레드에서)
            int[] state = SnapshotBoardState(board);
            int emptyCount = 0;
            foreach (var v in state) if (v == -1) emptyCount++;
            int futureTileCount = Mathf.Max(0, emptyCount - 1);

            var remainingMcts = new List<int>(job.deckSnapshot.Count);
            foreach (var s in job.deckSnapshot) remainingMcts.Add(CardStringToMctsInt(s));

            int newTile = CardStringToModelInt(job.card);

            // AI 계산을 백그라운드 스레드에서 실행 (메인 스레드 블로킹 방지)
            int slot = -1;
            if (model != null)
            {
                var capturedState  = state;
                var capturedDeck   = remainingMcts;
                int capturedTile   = newTile;
                int capturedFuture = futureTileCount;
                int capturedJoker  = jokerModelValue;

                var task = Task.Run(() =>
                    model.GetBestPosition(capturedState, capturedTile, capturedDeck, capturedFuture, capturedJoker));

                while (!task.IsCompleted) yield return null;

                if (task.IsFaulted)
                {
                    Debug.LogWarning($"AI[{a}] 계산 오류, 첫 번째 빈 칸으로 대체: {task.Exception?.GetBaseException().Message}");
                    slot = -1;
                }
                else
                {
                    slot = task.Result;
                }
            }

            // 메인 스레드에서 유효성 검증 후 배치
            if (slot < 0 || slot >= board.slots.Count || board.slots[slot] == null || board.slots[slot].isFilled)
                slot = FirstEmptySlotIndex(board);

            if (slot >= 0)
                board.PlaceCardFromAI(slot, job.card);
        }
    }

    // ──────────────────────────────────────────────
    // 유틸리티
    // ──────────────────────────────────────────────

    int[] SnapshotBoardState(num_path board)
    {
        int n = Mathf.Min(20, board.slots.Count);
        var state = new int[20];
        for (int i = 0; i < 20; i++)
        {
            if (i >= n || board.slots[i] == null || !board.slots[i].isFilled)
                state[i] = -1;
            else
                state[i] = CardStringToModelInt(board.slots[i].cardValue);
        }
        return state;
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
            if (board.slots[i] != null && !board.slots[i].isFilled) return i;
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
}
