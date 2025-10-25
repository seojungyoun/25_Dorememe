using UnityEngine;

public class StartButtonController : MonoBehaviour
{
    public GameObject startCanvas;       // 시작하기 버튼 있는 캔버스
    public GameObject seasonSelectCanvas; // 계절 선택 캔버스

    public void OnClickStart()
    {
        // 시작 캔버스 끄기
        startCanvas.SetActive(false);
        // 계절 선택 캔버스 켜기
        seasonSelectCanvas.SetActive(true);
    }
}