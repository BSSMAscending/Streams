using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class randomoutnum : MonoBehaviour
{
    public Button button;
    public Text text;
    public num_path numPath; // ← Inspector에서 연결

    List<string> deck = new List<string>()
    {
        "1","2","3","4","5","6","7","8","9","10",
        "11","12","13","14","15","16","17","18","19",
        "11","12","13","14","15","16","17","18","19",
        "20","21","22","23","24","25","26","27","28","29","30","J"
    };

    void Start()
    {
        button.onClick.AddListener(DrawCard);
        
        if (numPath == null)
        {
            Debug.LogError("randomoutnum: numPath가 인스펙터에서 할당되지 않았습니다! num_path 오브젝트를 연결해 주세요.");
        }
        text.text = "카드를 뽑으세요";
    }

    void DrawCard()
    {
        if (StreamsCardDrawCinematic.IsBlockingPlacement)
            return;
        if (deck.Count == 0)
        {
            text.text = "";
            return;
        }

        int index = Random.Range(0, deck.Count);
        string drawn = deck[index];
        deck.RemoveAt(index);

        text.text = drawn;
        Debug.Log(drawn);

        if (numPath != null)
        {
            // ✅ cardPlacementGame → numPath 로 수정
            numPath.ReceiveCard(drawn);
        }
        else
        {
            Debug.LogError("numPath가 할당되지 않아 ReceiveCard를 호출할 수 없습니다.");
        }
    }
}