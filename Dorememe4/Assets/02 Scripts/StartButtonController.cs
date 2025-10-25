using UnityEngine;

public class StartButtonController : MonoBehaviour
{
    public GameObject startCanvas;       // �����ϱ� ��ư �ִ� ĵ����
    public GameObject seasonSelectCanvas; // ���� ���� ĵ����

    public void OnClickStart()
    {
        // ���� ĵ���� ����
        startCanvas.SetActive(false);
        // ���� ���� ĵ���� �ѱ�
        seasonSelectCanvas.SetActive(true);
    }
}