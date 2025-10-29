using UnityEngine;
using UnityEngine.UI;

public class BrushHandleTransparency : MonoBehaviour
{
    public Slider brushSlider;       // �����̴� ������Ʈ
    public Image handleImage;        // �ڵ� �̹���

    [Range(0.1f, 1f)]
    public float minAlpha = 0.3f;   // ���� �� ����
    public float maxAlpha = 1f;     // ������ �� ����

    void Start()
    {
        if (handleImage == null)
        {
            Debug.LogError("Handle Image�� ������� �ʾҽ��ϴ�!");
            return;
        }

        brushSlider.onValueChanged.AddListener(UpdateHandleTransparency);
        UpdateHandleTransparency(brushSlider.value);
    }

    void UpdateHandleTransparency(float value)
    {
        // value^2�� ��ä�� ����
        float t = Mathf.Pow(value, 2f);
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);

        Color c = handleImage.color;
        c.a = alpha;
        handleImage.color = c;
    }
}
