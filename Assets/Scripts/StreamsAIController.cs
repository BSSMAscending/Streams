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

public class StreamsAIController : MonoBehaviour
{
    [Header("모드")]
    [Tooltip("켜면 Python streams_mcts.py와 동일한 롤아웃 MCTS 사용. ONNX는 끌 때만 로드.")]
    public bool useMcts = true;

    [Header("MCTS")]
    [Tooltip("약함=1회, 보통=100회, 강함=10000회(mctsNSimOverride가 0일 때만 적용).")]
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
        if (useMcts)
            return GetBestPositionMcts(board, newTile, remainingDeckMcts, futureTileCount, unityJokerValue);

        return GetBestPositionOnnx(board, newTile);
    }

    int GetBestPositionMcts(int[] board, int newTile, IList<int> remainingDeckMcts, int futureTileCount, int unityJokerValue)
    {
        CopyBoardToMcts(board, unityJokerValue);
        int tileM = ToMctsTile(newTile, unityJokerValue);

        int? nsim = ResolveMctsNsim();

        var pool = remainingDeckMcts ?? System.Array.Empty<int>();
        return StreamsMctsCore.MctsPosition(m_MctsBoardScratch, tileM, pool, futureTileCount, m_MctsRng, nsim);
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

    int GetBestPositionOnnx(int[] board, int newTile)
    {
        if (m_Worker == null)
            return -1;

        var inputData = new float[21];
        for (int i = 0; i < 20; i++)
            inputData[i] = board[i];
        inputData[20] = newTile;

        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 21), inputData);
        m_Worker.Schedule(inputTensor);

        var outputTensor = m_Worker.PeekOutput() as Tensor<float>;
        float[] probabilities = outputTensor.DownloadToArray();

        float maxProb = float.NegativeInfinity;
        int bestPos = -1;

        for (int i = 0; i < 20; i++)
        {
            if (board[i] == -1 && probabilities[i] > maxProb)
            {
                maxProb = probabilities[i];
                bestPos = i;
            }
        }

        return bestPos;
    }

    void OnDestroy()
    {
        m_Worker?.Dispose();
    }
}