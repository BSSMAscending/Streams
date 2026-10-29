using System;
using System.Collections.Generic;

/// <summary>
/// 롤아웃 MCTS. 보드: -1=빈칸, 0=조커, 1~30=카드.
/// <see cref="CalcScore"/>는 화면 점수(<see cref="num_path"/>)와 동일한 규칙을 사용합니다.
/// </summary>
public static class StreamsMctsCore
{
    static readonly int[] ScoreTable =
    {
        0, 1, 3, 5, 7, 9, 11, 15, 20, 25,
        30, 35, 40, 50, 60, 70, 85, 100, 150, 300
    };

    public static int CalcScore(int[] board)
    {
        int jokerIdx = -1;
        for (int i = 0; i < 20; i++)
        {
            if (board[i] == 0)
            {
                jokerIdx = i;
                break;
            }
        }

        if (jokerIdx == -1)
            return CalcScoreNoJoker(board);

        var front = new int[20];
        var back = new int[20];
        Array.Copy(board, front, 20);
        Array.Copy(board, back, 20);
        front[jokerIdx] = ResolveJokerReplacement(board, jokerIdx, attachToFront: true);
        back[jokerIdx] = ResolveJokerReplacement(board, jokerIdx, attachToFront: false);

        return Math.Max(CalcScoreNoJoker(front), CalcScoreNoJoker(back));
    }

    /// <summary>num_path 조커 규칙: 앞 구간=왼쪽 카드(없으면 0), 뒤 구간=오른쪽 카드(없으면 0).</summary>
    static int ResolveJokerReplacement(int[] board, int jokerIdx, bool attachToFront)
    {
        if (attachToFront)
            return jokerIdx > 0 ? board[jokerIdx - 1] : 0;

        return jokerIdx < 19 ? board[jokerIdx + 1] : 0;
    }

    static int CalcScoreNoJoker(int[] board)
    {
        int totalScore = 0;
        int currentRun = 0;
        int prevValue = -1;

        for (int i = 0; i < 20; i++)
        {
            int value = board[i];
            if (value == -1)
            {
                if (currentRun > 0) totalScore += GetScore(currentRun);
                currentRun = 0;
                prevValue = -1;
                continue;
            }

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

        if (currentRun > 0) totalScore += GetScore(currentRun);
        return totalScore;
    }

    static int GetScore(int length)
    {
        if (length <= 0) return 0;
        int idx = length < ScoreTable.Length ? length : ScoreTable.Length - 1;
        return ScoreTable[idx];
    }

    public static int GreedyPos(int[] b, int tile)
    {
        int emptyCount = 0;
        for (int i = 0; i < 20; i++)
            if (b[i] == -1) emptyCount++;

        if (emptyCount == 0) return -1;

        int t = tile != 0 ? tile : 15;
        double ideal = (t - 1) / 29.0 * 19.0;

        int bestI = -1;
        TupleCompare bestS = default;
        bool hasBest = false;

        for (int i = 0; i < 20; i++)
        {
            if (b[i] != -1) continue;

            int l = Lv(b, i);
            int r = Rv(b, i);
            double al = 1.0 - Math.Abs(i - ideal) / 19.0;

            TupleCompare s;
            if (l <= t && t <= r) s = new TupleCompare(3, -(r - l), al);
            else if (l <= t) s = new TupleCompare(2, -r, al);
            else if (t <= r) s = new TupleCompare(1, l, al);
            else s = new TupleCompare(0, 0, al);

            if (!hasBest || s.CompareTo(bestS) > 0)
            {
                hasBest = true;
                bestS = s;
                bestI = i;
            }
        }

        return bestI;
    }

    struct TupleCompare : IComparable<TupleCompare>
    {
        public readonly int A;
        public readonly int B;
        public readonly double C;

        public TupleCompare(int a, int b, double c)
        {
            A = a;
            B = b;
            C = c;
        }

        public int CompareTo(TupleCompare other)
        {
            int c = A.CompareTo(other.A);
            if (c != 0) return c;
            c = B.CompareTo(other.B);
            if (c != 0) return c;
            return C.CompareTo(other.C);
        }
    }

    static int Lv(int[] b, int i)
    {
        for (int k = i - 1; k >= 0; k--)
            if (b[k] != -1) return b[k];
        return 0;
    }

    static int Rv(int[] b, int i)
    {
        for (int k = i + 1; k < 20; k++)
            if (b[k] != -1) return b[k];
        return 31;
    }

    /// <summary>
    /// Python mcts_position과 동일. remainingPool.Count > futureTileCount 이면 매 시뮬에서 풀을 셔플한 뒤 앞 futureTileCount장만 사용(Unity 덱 잔량 대응).
    /// </summary>
    public static int MctsPosition(int[] board, int tile, IList<int> remainingPool, int futureTileCount, Random rng, int? nSimOverride = null)
    {
        var weights = new float[20];
        return MctsPositionWithWeights(board, tile, remainingPool, futureTileCount, rng, nSimOverride, weights);
    }

    /// <summary>빈 칸마다 롤아웃 평균 점수를 outWeights에 채우고 최고 칸 인덱스를 반환합니다.</summary>
    public static int MctsPositionWithWeights(
        int[] board,
        int tile,
        IList<int> remainingPool,
        int futureTileCount,
        Random rng,
        int? nSimOverride,
        float[] outWeights)
    {
        for (int i = 0; i < 20; i++)
            outWeights[i] = board[i] == -1 ? 0f : -1f;

        var empty = new List<int>(20);
        for (int i = 0; i < 20; i++)
            if (board[i] == -1) empty.Add(i);

        if (empty.Count == 0)
            return -1;

        if (empty.Count == 1)
        {
            outWeights[empty[0]] = 1f;
            return empty[0];
        }

        var search = new MctsStepper(board, tile, remainingPool, futureTileCount, rng, nSimOverride);
        while (!search.IsDone)
            search.Advance(int.MaxValue);

        var weights = search.Weights;
        for (int i = 0; i < 20; i++)
            outWeights[i] = weights[i];
        return search.BestPosition;
    }

    /// <summary>
    /// 메인 스레드에서 프레임마다 나눠 돌릴 수 있는 MCTS.
    /// WebGL에는 스레드가 없어 <see cref="MctsPositionWithWeights"/>를 한 번에 돌리면 탭이 멈춥니다.
    /// </summary>
    public sealed class MctsStepper
    {
        readonly int[] _board = new int[20];
        readonly int[] _baseBoard = new int[20];
        readonly int[] _work = new int[20];
        readonly float[] _weightValues = new float[20];
        readonly List<int> _empty = new List<int>(20);
        readonly List<int> _rem;
        readonly int _tile;
        readonly int _futureTileCount;
        readonly Random _rng;
        readonly int _nsim;

        int _posIndex;
        int _simIndex;
        double _total;
        double _bestScore = -1.0;

        public bool IsDone { get; private set; }
        public int BestPosition { get; private set; }
        public float[] Weights => _weightValues;

        public MctsStepper(int[] board, int tile, IList<int> remainingPool, int futureTileCount, Random rng, int? nSimOverride)
        {
            Array.Copy(board, _board, 20);
            _tile = tile;
            _futureTileCount = futureTileCount;
            _rng = rng;

            for (int i = 0; i < 20; i++)
            {
                _weightValues[i] = board[i] == -1 ? 0f : -1f;
                if (board[i] == -1)
                    _empty.Add(i);
            }

            int count = remainingPool != null ? remainingPool.Count : 0;
            _rem = new List<int>(count);
            if (remainingPool != null)
            {
                for (int i = 0; i < count; i++)
                    _rem.Add(remainingPool[i]);
            }

            if (_empty.Count == 0)
            {
                BestPosition = -1;
                IsDone = true;
                _nsim = 1;
                return;
            }

            if (_empty.Count == 1)
            {
                _weightValues[_empty[0]] = 1f;
                BestPosition = _empty[0];
                IsDone = true;
                _nsim = 1;
                return;
            }

            int nsim = nSimOverride ?? GetAdaptiveNsim(_empty.Count);
            _nsim = nsim < 1 ? 1 : nsim;
            BestPosition = _empty[0];
        }

        public void Advance(int maxSims)
        {
            if (IsDone || maxSims <= 0)
                return;

            int ran = 0;
            while (ran < maxSims && _posIndex < _empty.Count)
            {
                int pos = _empty[_posIndex];
                if (_simIndex == 0)
                {
                    Array.Copy(_board, _baseBoard, 20);
                    _baseBoard[pos] = _tile;
                    _total = 0.0;
                }

                Shuffle(_rem, _rng);
                Array.Copy(_baseBoard, _work, 20);

                int useCount = _futureTileCount < _rem.Count ? _futureTileCount : _rem.Count;
                for (int fi = 0; fi < useCount; fi++)
                {
                    int t2 = _rem[fi];
                    int p = GreedyPos(_work, t2);
                    if (p >= 0)
                        _work[p] = t2;
                }

                _total += CalcScore(_work);
                _simIndex++;
                ran++;

                if (_simIndex >= _nsim)
                {
                    double avg = _total / _nsim;
                    _weightValues[pos] = (float)avg;
                    if (avg > _bestScore)
                    {
                        _bestScore = avg;
                        BestPosition = pos;
                    }

                    _simIndex = 0;
                    _posIndex++;
                }
            }

            if (_posIndex >= _empty.Count)
                IsDone = true;
        }
    }

    /// <summary>Python과 동일한 적응형 시뮬 횟수.</summary>
    public static int GetAdaptiveNsim(int emptyCellCount)
    {
        int n = emptyCellCount;
        if (n <= 2) return 500;
        if (n <= 4) return 200;
        if (n <= 6) return 80;
        if (n <= 10) return 20;
        return 6;
    }

    static void Shuffle(List<int> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }
}
