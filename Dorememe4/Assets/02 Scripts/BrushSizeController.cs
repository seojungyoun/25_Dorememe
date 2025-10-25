using UnityEngine;
using UnityEngine.UI;

public class BrushSizeController : MonoBehaviour
{
    public Slider brushSizeSlider;
    public RectTransform fillArea;       // �����̴� ��

    public float minBarHeight = 10f;     // ���� �ּ� �β�
    public float maxBarHeight = 20f;     // ���� �ִ� �β�

    void Start()
    {
        brushSizeSlider.onValueChanged.AddListener(UpdateSliderBar);
        UpdateSliderBar(brushSizeSlider.value);
    }

    void UpdateSliderBar(float value)
    {
        // value^2 �� ������ ���� ��ȭ ����, ������ �������� ����
        float t = Mathf.Pow(value, 2f);
        float barHeight = Mathf.Lerp(minBarHeight, maxBarHeight, t);

        Vector2 fillSize = fillArea.sizeDelta;
        fillArea.sizeDelta = new Vector2(fillSize.x, barHeight);
    }
}