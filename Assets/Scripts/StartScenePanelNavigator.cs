using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시작 씬에서 여러 패널 중 하나만 보이게 하고, 상단의 이전/다음 버튼으로 전환합니다.
/// 첫 패널에서는 이전, 마지막 패널에서는 다음 버튼을 숨깁니다.
/// </summary>
public class StartScenePanelNavigator : MonoBehaviour
{
    [Header("상단 고정 버튼")]
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;

    [Header("패널 (순서대로 전환)")]
    [SerializeField] private GameObject[] panels;

    private int _currentIndex;

    private void Awake()
    {
        if (panels == null || panels.Length == 0)
        {
            Debug.LogError("StartScenePanelNavigator: panels가 비어 있습니다.");
            return;
        }

        if (previousButton != null)
            previousButton.onClick.AddListener(GoToPrevious);
        if (nextButton != null)
            nextButton.onClick.AddListener(GoToNext);

        ShowPage(0);
    }

    public void GoToNext()
    {
        if (_currentIndex < panels.Length - 1)
            ShowPage(_currentIndex + 1);
    }

    public void GoToPrevious()
    {
        if (_currentIndex > 0)
            ShowPage(_currentIndex - 1);
    }

    public void ShowPage(int index)
    {
        index = Mathf.Clamp(index, 0, panels.Length - 1);
        _currentIndex = index;

        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] != null)
                panels[i].SetActive(i == index);
        }

        if (previousButton != null)
            previousButton.gameObject.SetActive(index > 0);
        if (nextButton != null)
            nextButton.gameObject.SetActive(index < panels.Length - 1);
    }
}
