using System;
using System.Collections.Generic;

/// <summary>
/// Python streams_mcts.py와 동일한 롤아웃 MCTS·점수 규칙 (비내림 스트림, 조커 0).
/// 화면 점수(<see cref="num_path"/>)와 다를 수 있음 — AI 목적함수는 본 클래스 기준.
/// </summary>
public static class StreamsMctsCore
{
    static readonly int[] ScoreTable =
    {
        0, 0, 1, 3, 5, 7, 9, 11, 15, 20, 25, 30, 35, 40, 50, 60, 70, 85, 100, 150, 300
    };

    public static int CalcScore(int[] board)
    {
        var b = new int[20];
        Array.Copy(board, b, 20);

        for (int i = 0; i < 20; i++)
        {
            if (b[i] != 0) continue;
            int lv = 0;
            for (int k = i - 1; k >= 0; k--)
            {
                if (b[k] != -1 && b[k] != 0)
                {
                    lv = b[k];
                    break;
                }
            }

            int rv = 30;
            for (int k = i + 1; k < 20; k++)
            {
                if (b[k] != -1 && b[k] != 0)
                {
                    rv = b[k];
                    break;
                }
            }

            b[i] = lv <= rv ? (lv + rv) / 2 : lv;
        }

        int t = 0;
        int idx = 0;
        while (idx < 20)
        {
            int r = 1;
            int j = idx + 1;
            while (j < 20 && b[j] >= b[j - 1])
            {
                r++;
                j++;
            }

            t += ScoreTable[r];
            idx = j;
        }

        return t;
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
        var empty = new List<int>(20);
        for (int i = 0; i < 20; i++)
            if (board[i] == -1) empty.Add(i);

        if (empty.Count <= 1)
            return empty[0];

        int nEmpty = empty.Count;
        int nsim = nSimOverride ?? GetAdaptiveNsim(nEmpty);

        var rem = new List<int>(remainingPool.Count);
        foreach (var x in remainingPool) rem.Add(x);

        int bestPos = empty[0];
        double bestSc = -1.0;

        var baseBoard = new int[20];
        var work = new int[20];

        foreach (int pos in empty)
        {
            Array.Copy(board, baseBoard, 20);
            baseBoard[pos] = tile;

            double total = 0.0;
            for (int sim = 0; sim < nsim; sim++)
            {
                Shuffle(rem, rng);

                Array.Copy(baseBoard, work, 20);

                int useCount = futureTileCount < rem.Count ? futureTileCount : rem.Count;
                for (int fi = 0; fi < useCount; fi++)
                {
                    int t2 = rem[fi];
                    int p = GreedyPos(work, t2);
                    if (p >= 0) work[p] = t2;
                }

                total += CalcScore(work);
            }

            double avg = total / nsim;
            if (avg > bestSc)
            {
                bestSc = avg;
                bestPos = pos;
            }
        }

        return bestPos;
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
