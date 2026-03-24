using UnityEngine;
using Unity.InferenceEngine;

public class StreamsAIController : MonoBehaviour
{
    [Header("AI Model Setting")]
    public Unity.InferenceEngine.ModelAsset modelAsset;
    
    private Unity.InferenceEngine.Worker m_Worker;

    void Start()
    {
        if (modelAsset == null)
        {
            Debug.LogError("StreamsAIController: modelAsset이 할당되지 않았습니다.");
            return;
        }

        // 1. 모델 로드
        var model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        
        // Worker 생성 (필요 시 BackendType.CPU 사용)
        m_Worker = new Unity.InferenceEngine.Worker(model, Unity.InferenceEngine.BackendType.GPUCompute);
    }

    public int GetBestPosition(int[] board, int newTile)
    {
        // 1. 입력 데이터 준비 (보드 20칸 + 새 타일 1개 = 총 21개)
        float[] inputData = new float[21];
        for (int i = 0; i < 20; i++)
        {
            inputData[i] = (float)board[i];
        }
        inputData[20] = (float)newTile;

        // 2. 텐서(Tensor) 생성 (Shape: 1행 21열)
        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 21), inputData);

        // 3. AI 추론 실행
        m_Worker.Schedule(inputTensor);

        // 4. 결과값(각 칸의 확률) 가져오기
        var outputTensor = m_Worker.PeekOutput() as Tensor<float>;
        
        // GPU에 있는 데이터를 CPU가 읽을 수 있게 복사 (Inference Engine 2.x)
        float[] probabilities = outputTensor.DownloadToArray();

        // 5. 빈 칸 중에서 가장 확률이 높은 위치 찾기
        float maxProb = -float.MaxValue;
        int bestPos = -1;

        for (int i = 0; i < 20; i++)
        {
            // 빈 칸(-1) 중 출력 확률이 가장 큰 칸 선택
            if (board[i] == -1 && probabilities[i] > maxProb)
            {
                maxProb = probabilities[i];
                bestPos = i;
            }
        }

        return bestPos;
    }

    private void OnDestroy()
    {
        m_Worker?.Dispose();
    }
}