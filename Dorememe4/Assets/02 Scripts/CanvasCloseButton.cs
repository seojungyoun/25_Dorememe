using UnityEngine;
using UnityEngine.UI;

public class CanvasCloseButton : MonoBehaviour
{
    public GameObject canvasToClose; // 숨기고 싶은 Canvas

    void Start()
    {
        // 버튼 클릭 이벤트 연결
        GetComponent<Button>().onClick.AddListener(CloseCanvas);
    }

    void CloseCanvas()
    {
        canvasToClose.SetActive(false); // Canvas 숨기기
    }
}