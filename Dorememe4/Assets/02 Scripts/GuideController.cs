using UnityEngine;

public class GuideController : MonoBehaviour
{
    public GameObject guidePanel; // Guide Panel을 여기로 드래그

    public void ToggleGuidePanel()
    {
        guidePanel.SetActive(!guidePanel.activeSelf);
    }
}