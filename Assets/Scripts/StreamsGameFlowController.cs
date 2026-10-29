using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1대1 모드. BoardCanvas의 플레이어 / AI 보드로 대전을 진행합니다.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class StreamsGameFlowController : MonoBehaviour
{
    [Header("필수 연결")]
    public Camera mainCamera;
    public num_path playerBoard;
    [Tooltip("비우면 Ai Boards[0] 또는 GameBoard_AI의 num_path를 찾습니다.")]
    public num_path aiBoard;
    [Tooltip("하위 호환. 첫 번째만 사용합니다.")]
    public num_path[] aiBoards = new num_path[1];

    [Header("AI")]
    [Tooltip("비우면 StartScene 선택 난이도 또는 Ai Models[0] 사용.")]
    public StreamsAIController aiModel;
    public StreamsAIController[] aiModels = new StreamsAIController[3];

    [Header("AI 입력")]
    public int jokerModelValue = 21;

    [Header("게임 종료")]
    public string startSceneName = "StartScene";
    [Tooltip("비우면 씬에서 이름 EndButton을 찾습니다. 누르면 StartScene으로 갑니다.")]
    public Button endButton;
    [Tooltip("비우면 씬에서 이름 NextButton을 찾습니다. 대전 종료 후 켜지고, 누르면 ResultCanvas를 띄웁니다.")]
    public Button nextButton;
    [Tooltip("비우면 씬에서 이름 ResultCanvas를 찾습니다.")]
    public Canvas resultCanvas;

    const string EndButtonObjectName = "EndButton";
    const string NextButtonObjectName = "NextButton";
    const string ResultCanvasName = "ResultCanvas";
    const string WinnerTextObjectName = "WinnerText";
    const string BoardCanvasName = "BoardCanvas";

    public Camera PlayerCamera => mainCamera != null ? mainCamera : Camera.main;

    struct AiJob
    {
        public string card;
        public List<string> deckSnapshot;
    }

    readonly Queue<AiJob> _aiQueue = new Queue<AiJob>();
    int _aiJobsInFlight;
    bool _gameEnded;
    bool _waitingForPlayer;
    Button _resultEndButton;
    string _winnerTextOverride;

    void Awake()
    {
        ResolveBoards();
        ResolveAiModel();

        if (playerBoard != null)
        {
            playerBoard.isPlayerControlledBoard = true;
            playerBoard.OnPlayerCardPlaced += OnPlayerCardPlaced;
            playerBoard.EnsureUiSlotsBound();
        }

        if (AiBoard != null)
        {
            AiBoard.isPlayerControlledBoard = false;
            AiBoard.EnsureUiSlotsBound();
        }

        BindResultCanvas();
    }

    void Start()
    {
        StreamsGameResults.Clear();
        BindHudButtons();

        if (StreamsTutorialSelection.IsActive)
        {
            var tutorial = GetComponent<StreamsTutorialController>();
            if (tutorial == null)
                tutorial = gameObject.AddComponent<StreamsTutorialController>();
            tutorial.Begin(this);
            return;
        }

        StartCoroutine(AiWorker());
        StartCoroutine(GameLoopRoutine());
    }

    num_path AiBoard
    {
        get
        {
            if (aiBoard != null)
                return aiBoard;
            if (aiBoards != null && aiBoards.Length > 0 && aiBoards[0] != null)
                return aiBoards[0];
            return null;
        }
    }

    public num_path OpponentBoard => AiBoard;

    void ResolveBoards()
    {
        if (aiBoard == null && aiBoards != null && aiBoards.Length > 0)
            aiBoard = aiBoards[0];

        if (aiBoard != null)
            return;

        var aiRoot = GameObject.Find("GameBoard_AI");
        if (aiRoot != null)
            aiBoard = aiRoot.GetComponentInChildren<num_path>(true);
    }

    void ResolveAiModel()
    {
        if (aiModel != null)
            return;

        var target = StreamsOpponentSelection.HasSelection
            ? StreamsOpponentSelection.SelectedOpponent
            : StreamsAiMctsPerformance.Normal;

        if (aiModels != null)
        {
            foreach (var candidate in aiModels)
            {
                if (candidate == null)
                    continue;

                bool selected = candidate.mctsPerformance == target;
                candidate.gameObject.SetActive(selected);
                if (selected)
                    aiModel = candidate;
            }
        }

        if (aiModel == null && aiModels != null && aiModels.Length > 0)
            aiModel = aiModels[0];
    }

    void OnDestroy()
    {
        _gameEnded = true;
        if (playerBoard != null)
            playerBoard.OnPlayerCardPlaced -= OnPlayerCardPlaced;

        if (endButton != null)
            endButton.onClick.RemoveListener(OnEndButtonPressed);
        if (_resultEndButton != null)
            _resultEndButton.onClick.RemoveListener(OnEndButtonPressed);
        if (nextButton != null)
            nextButton.onClick.RemoveListener(OnNextButtonPressed);
    }

    void OnPlayerCardPlaced() => _waitingForPlayer = false;

    IEnumerator GameLoopRoutine()
    {
        var deck = BuildDeck();
        if (playerBoard == null)
        {
            Debug.LogError("StreamsGameFlowController: playerBoard가 없습니다.");
            yield break;
        }

        if (AiBoard == null)
        {
            Debug.LogError("StreamsGameFlowController: aiBoard가 없습니다.");
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
            yield return StreamsCardDrawCinematic.PlayNow(currentCard);
            playerBoard.ReceiveCard(currentCard);

            while (_waitingForPlayer)
                yield return null;

            _aiQueue.Enqueue(new AiJob { card = currentCard, deckSnapshot = new List<string>(deck) });
        }

        _gameEnded = true;
        yield return WaitForAllAiWork();

        StreamsGameResults.SaveFromDuel(playerBoard, AiBoard);

        if (nextButton != null)
            nextButton.gameObject.SetActive(true);
        else
            ShowResultCanvas();
    }

    void BindHudButtons()
    {
        if (endButton == null)
            endButton = FindNamedButton(EndButtonObjectName);
        if (nextButton == null)
            nextButton = FindNamedButton(NextButtonObjectName);

        WireButton(endButton, OnEndButtonPressed, hide: false);
        WireButton(nextButton, OnNextButtonPressed, hide: true);
        BindResultCanvas();
    }

    static void WireButton(Button button, UnityEngine.Events.UnityAction onClick, bool hide)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(onClick);
        button.onClick.AddListener(onClick);
        if (hide)
            button.gameObject.SetActive(false);
        else
            button.gameObject.SetActive(true);
    }

    static Button FindNamedButton(string objectName)
    {
        Button fallback = null;
        foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button == null || button.name != objectName)
                continue;

            if (IsUnderNamedParent(button.transform, BoardCanvasName))
                return button;

            fallback = button;
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

    void OnEndButtonPressed()
    {
        if (string.IsNullOrWhiteSpace(startSceneName))
            return;

        StreamsTutorialSelection.Clear();
        StreamsSceneTransition.Load(startSceneName);
    }

    /// <summary>튜토리얼이 NextButton / 결과 화면을 쓸 수 있게 점수를 확정합니다.</summary>
    public void PrepareTutorialResult()
    {
        _winnerTextOverride = null;
        _gameEnded = true;
        StreamsGameResults.SaveFromDuel(playerBoard, AiBoard);
    }

    public void ShowTutorialResult()
    {
        ShowResultCanvas();
    }

    void OnNextButtonPressed()
    {
        if (!_gameEnded)
            return;

        ShowResultCanvas();
    }

    void BindResultCanvas()
    {
        if (resultCanvas == null)
            resultCanvas = FindNamedCanvas(ResultCanvasName);

        if (resultCanvas == null)
            return;

        resultCanvas.sortingOrder = Mathf.Max(resultCanvas.sortingOrder, 50);
        resultCanvas.gameObject.SetActive(false);

        if (_resultEndButton == null)
            _resultEndButton = FindNamedButtonUnder(resultCanvas.transform, EndButtonObjectName);

        WireButton(_resultEndButton, OnEndButtonPressed, hide: false);
    }

    void ShowResultCanvas()
    {
        if (resultCanvas == null)
            BindResultCanvas();
        if (resultCanvas == null)
        {
            Debug.LogWarning("StreamsGameFlowController: ResultCanvas가 없습니다.");
            return;
        }

        resultCanvas.gameObject.SetActive(true);
        FillWinnerText();

        var overlay = resultCanvas.GetComponent<StreamsResultOverlay>();
        if (overlay == null)
            overlay = resultCanvas.gameObject.AddComponent<StreamsResultOverlay>();
        overlay.Play();
    }

    void FillWinnerText()
    {
        TextMeshProUGUI label = FindNamedTmpUnder(resultCanvas != null ? resultCanvas.transform : null, WinnerTextObjectName);
        if (label == null)
            return;

        if (!string.IsNullOrEmpty(_winnerTextOverride))
        {
            label.text = _winnerTextOverride;
            return;
        }

        int playerScore = StreamsGameResults.PlayerScore;
        int aiScore = StreamsGameResults.OpponentScore;
        if (playerScore > aiScore)
            label.text = "플레이어 승리!";
        else if (aiScore > playerScore)
            label.text = "AI 승리!";
        else
            label.text = "무승부";
    }

    static Canvas FindNamedCanvas(string objectName)
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas != null && canvas.name == objectName)
                return canvas;
        }

        return null;
    }

    static Button FindNamedButtonUnder(Transform root, string objectName)
    {
        if (root == null)
            return null;

        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            if (button != null && button.name == objectName)
                return button;
        }

        return null;
    }

    static TextMeshProUGUI FindNamedTmpUnder(Transform root, string objectName)
    {
        if (root == null)
            return null;

        foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (tmp != null && tmp.name == objectName)
                return tmp;
        }

        return null;
    }

    IEnumerator WaitForAllAiWork()
    {
        while (_aiQueue.Count > 0 || _aiJobsInFlight > 0)
            yield return null;
    }

    IEnumerator AiWorker()
    {
        while (true)
        {
            while (_aiQueue.Count == 0)
            {
                if (_gameEnded && _aiJobsInFlight == 0)
                    yield break;
                yield return null;
            }

            var job = _aiQueue.Dequeue();
            _aiJobsInFlight++;

            try
            {
                var board = AiBoard;
                if (aiModel == null)
                    ResolveAiModel();

                int[] state = SnapshotBoardState(board);
                int emptyCount = 0;
                foreach (var v in state)
                    if (v == -1) emptyCount++;
                int futureTileCount = Mathf.Max(0, emptyCount - 1);

                var remainingMcts = new List<int>(job.deckSnapshot.Count);
                foreach (var s in job.deckSnapshot)
                    remainingMcts.Add(CardStringToMctsInt(s));

                int newTile = CardStringToModelInt(job.card);
                int slot = -1;

                if (aiModel != null)
                {
                    if (Application.platform == RuntimePlatform.WebGLPlayer)
                    {
                        // WebGL은 Task.Run이 완료되지 않거나 메인 스레드를 멈춥니다.
                        if (aiModel.useMcts)
                        {
                            var search = aiModel.CreateMctsSearch(
                                state, newTile, remainingMcts, futureTileCount, jokerModelValue);
                            while (!search.IsDone)
                            {
                                float sliceStart = Time.realtimeSinceStartup;
                                while (!search.IsDone && Time.realtimeSinceStartup - sliceStart < 0.005f)
                                    search.Advance(1);
                                if (!search.IsDone)
                                    yield return null;
                            }

                            slot = aiModel.DecisionFromSearch(state, search).BestPosition;
                        }
                        else
                        {
                            slot = aiModel.GetPositionDecision(
                                state, newTile, remainingMcts, futureTileCount, jokerModelValue).BestPosition;
                        }
                    }
                    else
                    {
                        var task = Task.Run(() =>
                            aiModel.GetPositionDecision(state, newTile, remainingMcts, futureTileCount, jokerModelValue));

                        while (!task.IsCompleted)
                            yield return null;

                        if (task.IsFaulted)
                        {
                            Debug.LogWarning($"AI 계산 오류: {task.Exception?.GetBaseException().Message}");
                            slot = -1;
                        }
                        else
                        {
                            slot = task.Result.BestPosition;
                        }
                    }
                }

                if (slot < 0 || slot >= board.SlotCount || board.IsSlotFilled(slot))
                    slot = board.FirstEmptySlotIndex();

                if (slot >= 0)
                    board.PlaceCardFromAI(slot, job.card);
            }
            finally
            {
                _aiJobsInFlight--;
            }
        }
    }

    int[] SnapshotBoardState(num_path board)
    {
        var state = new int[20];
        for (int i = 0; i < 20; i++)
        {
            string value = board.GetSlotCardValue(i);
            state[i] = string.IsNullOrEmpty(value) ? -1 : CardStringToModelInt(value);
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

    int CardStringToModelInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        s = s.Trim().ToUpper();
        if (s == "J") return jokerModelValue;
        if (int.TryParse(s, out int v)) return v;
        return 0;
    }
}
