using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>MCTS 시뮬 횟수 프리셋. AI 오브젝트마다 따로 설정합니다.</summary>
public enum StreamsAiMctsPerformance
{
    [InspectorName("약함")]
    Weak,
    [InspectorName("보통 (기본)")]
    Normal,
    [InspectorName("강함")]
    Strong
}

public readonly struct StreamsAiPositionDecision
{
    public readonly int BestPosition;
    public readonly float[] SlotPercentages;

    public StreamsAiPositionDecision(int bestPosition, float[] slotPercentages)
    {
        BestPosition = bestPosition;
        SlotPercentages = slotPercentages ?? new float[20];
    }
}

public class StreamsAIController : MonoBehaviour
{
    [Header("모드")]
    [Tooltip("켜면 화면 점수 규칙과 동일한 롤아웃 MCTS 사용. ONNX는 끌 때만 로드.")]
    public bool useMcts = true;

    [Header("MCTS")]
    [Tooltip("약함=1회 학습, 보통=1000회 학습, 강함=1000000회 학습 (mctsNSimOverride가 0일 때만 적용).")]
    public StreamsAiMctsPerformance mctsPerformance = StreamsAiMctsPerformance.Normal;

    [Tooltip("0이면 위 성능 프리셋, 0보다 크면 매 턴 이 값으로 고정(프리셋 무시).")]
    public int mctsNSimOverride;

    [Tooltip("고정 시드(같은 상황 재현). <0이면 비재현 랜덤.")]
    public int mctsRandomSeed = -1;

    [Header("ONNX (useMcts 꺼짐)")]
    [Tooltip("외부 가중치(.onnx.data 등) 파일명이 모델에 기록된 경로와 일치해야 로드됩니다.")]
    public Unity.InferenceEngine.ModelAsset modelAsset;

    Unity.InferenceEngine.Worker m_Worker;
    System.Random m_MctsRng;
    readonly int[] m_MctsBoardScratch = new int[20];

    void Awake()
    {
        m_MctsRng = mctsRandomSeed >= 0
            ? new System.Random(mctsRandomSeed)
            : new System.Random(Guid.NewGuid().GetHashCode());
    }

    void Start()
    {
        if (useMcts)
            return;

        if (modelAsset == null)
        {
            Debug.LogError("StreamsAIController: useMcts가 꺼져 있는데 modelAsset이 없습니다.");
            return;
        }

        var model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        m_Worker = new Unity.InferenceEngine.Worker(model, Unity.InferenceEngine.BackendType.GPUCompute);
    }

    /// <param name="remainingDeckMcts">
    /// 현재 턴 카드 제거 후 남은 덱. 조커는 0. useMcts일 때만 사용.
    /// </param>
    /// <param name="futureTileCount">이번 수 이후 채울 칸 수 (=남은 빈칸 − 1).</param>
    /// <param name="unityJokerValue"><see cref="StreamsGameFlowController.jokerModelValue"/></param>
    public int GetBestPosition(int[] board, int newTile, IList<int> remainingDeckMcts, int futureTileCount, int unityJokerValue)
    {
        return GetPositionDecision(board, newTile, remainingDeckMcts, futureTileCount, unityJokerValue).BestPosition;
    }

    /// <summary>최적 칸과 빈 칸별 선택 비율(합 100%)을 반환합니다.</summary>
    public StreamsAiPositionDecision GetPositionDecision(
        int[] board,
        int newTile,
        IList<int> remainingDeckMcts,
        int futureTileCount,
        int unityJokerValue)
    {
        if (useMcts)
            return GetPositionDecisionMcts(board, newTile, remainingDeckMcts, futureTileCount, unityJokerValue);

        return GetPositionDecisionOnnx(board, newTile);
    }

    StreamsAiPositionDecision GetPositionDecisionMcts(
        int[] board,
        int newTile,
        IList<int> remainingDeckMcts,
        int futureTileCount,
        int unityJokerValue)
    {
        CopyBoardToMcts(board, unityJokerValue);
        int tileM = ToMctsTile(newTile, unityJokerValue);

        int? nsim = ResolveMctsNsim();

        var pool = remainingDeckMcts ?? System.Array.Empty<int>();
        var weights = new float[20];
        int best = StreamsMctsCore.MctsPositionWithWeights(
            m_MctsBoardScratch, tileM, pool, futureTileCount, m_MctsRng, nsim, weights);

        return new StreamsAiPositionDecision(best, NormalizeWeightsToPercent(board, weights));
    }

    int? ResolveMctsNsim()
    {
        if (mctsNSimOverride > 0)
            return mctsNSimOverride;

        switch (mctsPerformance)
        {
            case StreamsAiMctsPerformance.Weak:
                return 1;
            case StreamsAiMctsPerformance.Strong:
                return 10000;
            default:
                return 100;
        }
    }

    void CopyBoardToMcts(int[] board, int unityJokerValue)
    {
        for (int i = 0; i < 20; i++)
        {
            int v = board[i];
            if (v == unityJokerValue) v = 0;
            m_MctsBoardScratch[i] = v;
        }
    }

    static int ToMctsTile(int v, int unityJokerValue)
    {
        return v == unityJokerValue ? 0 : v;
    }

    StreamsAiPositionDecision GetPositionDecisionOnnx(int[] board, int newTile)
    {
        if (m_Worker == null)
            return new StreamsAiPositionDecision(-1, new float[20]);

        var inputData = new float[21];
        for (int i = 0; i < 20; i++)
            inputData[i] = board[i];
        inputData[20] = newTile;

        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 21), inputData);
        m_Worker.Schedule(inputTensor);

        var outputTensor = m_Worker.PeekOutput() as Tensor<float>;
        float[] probabilities = outputTensor.DownloadToArray();

        var weights = new float[20];
        float maxProb = float.NegativeInfinity;
        int bestPos = -1;

        for (int i = 0; i < 20; i++)
        {
            if (board[i] != -1)
                continue;

            weights[i] = probabilities[i];
            if (probabilities[i] > maxProb)
            {
                maxProb = probabilities[i];
                bestPos = i;
            }
        }

        return new StreamsAiPositionDecision(bestPos, NormalizeWeightsToPercent(board, weights));
    }

    static float[] NormalizeWeightsToPercent(int[] board, float[] weights)
    {
        var percents = new float[20];
        double sum = 0.0;
        int emptyCount = 0;

        for (int i = 0; i < 20; i++)
        {
            if (board[i] != -1)
                continue;

            emptyCount++;
            if (weights[i] > 0f)
                sum += weights[i];
        }

        if (emptyCount == 0)
            return percents;

        if (sum <= 0.0)
        {
            float each = 100f / emptyCount;
            for (int i = 0; i < 20; i++)
            {
                if (board[i] == -1)
                    percents[i] = each;
            }

            return percents;
        }

        for (int i = 0; i < 20; i++)
        {
            if (board[i] == -1)
                percents[i] = (float)(weights[i] / sum * 100.0);
        }

        return percents;
    }

    void OnDestroy()
    {
        m_Worker?.Dispose();
    }
}