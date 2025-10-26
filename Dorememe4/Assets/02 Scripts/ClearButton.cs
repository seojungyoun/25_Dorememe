using UnityEngine;

public class ClearButton : MonoBehaviour
{
    public GameObject clearConfirmCanvas; // Canvas_ClearConfirm 연결

    public void OnClickClear()
    {
        clearConfirmCanvas.SetActive(true); // 팝업 켜기
    }
}