using UnityEngine;

public class StartButtonController : MonoBehaviour
{
    [Header("Canvas")]
    public GameObject introCanvas;   // Canvas_Start
    public GameObject seasonCanvas;  // Canvas_Season (계절 선택 버튼이 있는 캔버스)

    [Header("VR Pen Root")]
    public GameObject vrPenRoot;     // VR Pen 전체 오브젝트

    public void OnStartButtonClick()
    {
        // 1Intro 화면 끄기
        if (introCanvas != null)
            introCanvas.SetActive(false);

        // 2계절 선택 화면 켜기
        if (seasonCanvas != null)
            seasonCanvas.SetActive(true);

        // 3️ VR Pen 비활성화 (드로잉 불가)
        if (vrPenRoot != null)
            vrPenRoot.SetActive(false);
    }
}