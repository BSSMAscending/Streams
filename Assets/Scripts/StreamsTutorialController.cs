using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>SampleScene 튜토리얼. 고정 카드와 스포트라이트로 배치·점수·조커·덱 구성을 안내합니다.</summary>
public class StreamsTutorialController : MonoBehaviour
{
    static readonly string[] RemainingCards =
    {
        "3", "4", "5", "6", "7", "8", "9", "10", "11",
        "12", "12", "13", "13", "14", "15", "16", "17"
    };

    StreamsGameFlowController _flow;
    StreamsTutorialSpotlight _spotlight;
    System.Action _onPlaced;

    public void Begin(StreamsGameFlowController flow)
    {
        _flow = flow;
        StopAllCoroutines();
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (_flow != null && _flow.playerBoard != null && _onPlaced != null)
            _flow.playerBoard.OnPlayerCardPlaced -= _onPlaced;

        _flow?.playerBoard?.ClearAllowedPlacementSlot();
        _spotlight?.Hide();
    }

    IEnumerator Run()
    {
        if (_flow == null || _flow.playerBoard == null)
        {
            Debug.LogError("StreamsTutorialController: playerBoard가 없습니다.");
            yield break;
        }

        var board = _flow.playerBoard;
        board.EnsureUiSlotsBound();
        _spotlight = StreamsTutorialSpotlight.Ensure();

        yield return null;
        yield return new WaitForEndOfFrame();

        yield return DrawAndPlace(board, "1", 0, "기차 칸을 눌러 카드를 배치해 보세요.");
        yield return ShowMessageOn(ScoreTargets(board), "카드를 배치해서 점수를 얻었습니다.");

        yield return DrawAndPlace(board, "2", 1, "기차 칸을 눌러 카드를 배치해 보세요.");
        yield return ShowMessageOn(
            ScoreAndSlots(board, 0, 1),
            "숫자를 오름차순으로 배치하면 얻는 점수가 더 늘어납니다.");

        yield return DrawAndPlace(board, "J", 2, "조커 카드! 어디에 배치해도 오름차순으로 인정돼요.");

        _spotlight.Hide();
        board.ClearAllowedPlacementSlot();
        yield return AutoFillRemaining(board);
        yield return ShowMessageOn(
            FindSlotsWithValues(board, "12", "13"),
            "10~19 카드는 2개씩 들어있어요.");

        _flow.PrepareTutorialResult("AI보다 더 높은 점수를 획득하면 승리해요!");
        yield return WaitForResultButton();
        _spotlight.Hide();
    }

    IEnumerator DrawAndPlace(num_path board, string card, int slotIndex, string message)
    {
        yield return StreamsCardDrawCinematic.PlayNow(card);
        board.ReceiveCard(card);
        board.SetAllowedPlacementSlot(slotIndex);

        var slot = GetSlot(board, slotIndex);
        if (slot != null)
            slot.SetHint();

        bool placed = false;
        _onPlaced = () => placed = true;
        board.OnPlayerCardPlaced += _onPlaced;

        _spotlight.Show(
            SlotRects(slot),
            message,
            blockClicks: false,
            worldCamera: _flow.PlayerCamera);

        while (!placed)
            yield return null;

        board.OnPlayerCardPlaced -= _onPlaced;
        _onPlaced = null;
        board.ClearAllowedPlacementSlot();
        _spotlight.Hide();
        PlaceAiRandom(card);
    }

    IEnumerator ShowMessageOn(IList<RectTransform> targets, string message)
    {
        _spotlight.Show(targets, message, blockClicks: true, worldCamera: _flow.PlayerCamera);
        yield return _spotlight.WaitForTap();
        _spotlight.Hide();
    }

    IEnumerator AutoFillRemaining(num_path board)
    {
        var aiSlots = EmptySlots(_flow.OpponentBoard);
        Shuffle(aiSlots);

        int slot = 3;
        int aiIndex = 0;
        for (int i = 0; i < RemainingCards.Length; i++)
        {
            if (slot >= board.SlotCount)
                break;

            board.PlaceCardFromAI(slot, RemainingCards[i]);
            if (aiIndex < aiSlots.Count)
                PlaceAiAt(aiSlots[aiIndex++], RemainingCards[i]);
            slot++;
            yield return new WaitForSeconds(0.06f);
        }
    }

    void PlaceAiRandom(string card)
    {
        var ai = _flow != null ? _flow.OpponentBoard : null;
        if (ai == null)
            return;

        int slot = RandomEmptySlot(ai);
        if (slot >= 0)
            ai.PlaceCardFromAI(slot, card);
    }

    void PlaceAiAt(int slot, string card)
    {
        var ai = _flow != null ? _flow.OpponentBoard : null;
        if (ai == null)
            return;
        ai.PlaceCardFromAI(slot, card);
    }

    static int RandomEmptySlot(num_path board)
    {
        var empty = EmptySlots(board);
        if (empty.Count == 0)
            return -1;
        return empty[Random.Range(0, empty.Count)];
    }

    static List<int> EmptySlots(num_path board)
    {
        var empty = new List<int>();
        if (board == null)
            return empty;

        int n = board.SlotCount;
        for (int i = 0; i < n; i++)
        {
            if (!board.IsSlotFilled(i))
                empty.Add(i);
        }

        return empty;
    }

    static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    IEnumerator WaitForResultButton()
    {
        Button next = _flow.nextButton;
        if (next == null)
        {
            _flow.ShowTutorialResult();
            yield break;
        }

        next.gameObject.SetActive(true);
        yield return null;
        var nextRt = next.transform as RectTransform;
        _spotlight.Show(
            nextRt != null ? new[] { nextRt } : null,
            "",
            blockClicks: false,
            worldCamera: _flow.PlayerCamera);

        Canvas result = _flow.resultCanvas;
        while (result == null || !result.gameObject.activeSelf)
        {
            if (result == null)
                result = _flow.resultCanvas;
            yield return null;
        }
    }

    static StreamsUiSlot GetSlot(num_path board, int index)
    {
        if (board == null || board.uiSlots == null)
            return null;
        if (index < 0 || index >= board.uiSlots.Count)
            return null;
        return board.uiSlots[index];
    }

    static List<RectTransform> SlotRects(StreamsUiSlot slot)
    {
        var list = new List<RectTransform>();
        if (slot != null)
            list.Add(slot.transform as RectTransform);
        return list;
    }

    static List<RectTransform> ScoreTargets(num_path board)
    {
        var list = new List<RectTransform>();
        if (board != null && board.uiScoreLabel != null)
            list.Add(board.uiScoreLabel.rectTransform);
        return list;
    }

    static List<RectTransform> ScoreAndSlots(num_path board, params int[] slotIndices)
    {
        var list = ScoreTargets(board);
        if (slotIndices == null)
            return list;

        for (int i = 0; i < slotIndices.Length; i++)
        {
            var slot = GetSlot(board, slotIndices[i]);
            if (slot != null)
                list.Add(slot.transform as RectTransform);
        }

        return list;
    }

    static List<RectTransform> FindSlotsWithValues(num_path board, params string[] values)
    {
        var list = new List<RectTransform>();
        if (board == null || board.uiSlots == null || values == null || values.Length == 0)
            return list;

        for (int i = 0; i < board.uiSlots.Count; i++)
        {
            var slot = board.uiSlots[i];
            if (slot == null || !slot.isFilled)
                continue;

            for (int v = 0; v < values.Length; v++)
            {
                string want = values[v] == null ? "" : values[v].Trim();
                if (!string.Equals(slot.cardValue, want, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(slot.transform as RectTransform);
                break;
            }
        }

        return list;
    }
}
