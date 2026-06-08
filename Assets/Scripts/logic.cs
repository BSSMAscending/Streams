using System;

class StreamScoreCalculator
{
    static void Main()
    {
        // 사용자로부터 카드 숫자 입력
        string input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) return;

        // 쉼표나 공백으로 분리하여 배열 생성
        string[] cards = input.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        int totalScore = CalculateScore(cards);
        
        Console.WriteLine(totalScore); 
    }

    static int CalculateScore(string[] cards)
    {
        int totalScore = 0;
        int streamLength = 0;
        int prevValue = -1;
        //점수 계산
        int[] scoreTable = new int[] { 0, 1, 3, 5, 7, 9, 11, 15, 20, 25, 30, 35, 40, 50, 60, 70, 85, 100, 150, 300 };

        foreach (var card in cards)
        {
            int value;
            string cleanCard = card.Trim().ToUpper();

            if (cleanCard == "J")//조커 카드 처리
            {
                value = (prevValue == -1) ? 0 : prevValue;
            }
            else
            {
                if (!int.TryParse(cleanCard, out value)) continue;
            }

            if (streamLength > 0 && value < prevValue)
            {
                totalScore += (streamLength < scoreTable.Length) ? scoreTable[streamLength] : scoreTable[scoreTable.Length - 1];
                streamLength = 0;
            }

            streamLength++;
            prevValue = value;
        }

        if (streamLength > 0)
        {
            totalScore += (streamLength < scoreTable.Length) ? scoreTable[streamLength] : scoreTable[scoreTable.Length - 1];
        }

        return totalScore;
    }
}