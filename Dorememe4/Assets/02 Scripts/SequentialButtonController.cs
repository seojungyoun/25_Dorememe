using UnityEngine;
using UnityEngine.UI;

public class SequentialButtonController : MonoBehaviour
{
    public GameObject[] buttons; // 순차 등장 버튼 배열
    public Button nextButton; // >
    public Button prevButton; // <

    private int currentIndex = 0; // 처음부터 첫 버튼이 켜져 있으므로 0

    void Start()
    {
        // 첫 버튼은 켜져있고 나머지는 꺼져있는 상태라면, 배열 순서대로 SetActive 체크
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].SetActive(i == 0); // 첫 버튼만 켜기
        }

        // 버튼 클릭 이벤트 연결
        nextButton.onClick.AddListener(OnNextButton);
        prevButton.onClick.AddListener(OnPrevButton);
    }

    void OnNextButton()
    {
        if (currentIndex < buttons.Length - 1)
        {
            currentIndex++;
            buttons[currentIndex].SetActive(true);
        }
    }

    void OnPrevButton()
    {
        if (currentIndex > 0)
        {
            buttons[currentIndex].SetActive(false);
            currentIndex--;
        }
    }
}