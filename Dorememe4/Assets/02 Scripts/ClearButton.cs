using UnityEngine;

public class ClearButton : MonoBehaviour
{
    public GameObject clearConfirmCanvas; // Canvas_ClearConfirm ����

    public void OnClickClear()
    {
        clearConfirmCanvas.SetActive(true); // �˾� �ѱ�
    }
}