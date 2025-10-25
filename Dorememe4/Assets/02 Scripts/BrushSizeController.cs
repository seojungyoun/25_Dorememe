using UnityEngine;
using UnityEngine.UI;

public class BrushSizeController : MonoBehaviour
{
    public Slider brushSizeSlider;
    public RectTransform fillArea;       // 슬라이더 바

    public float minBarHeight = 10f;     // 앞쪽 최소 두께
    public float maxBarHeight = 20f;     // 뒤쪽 최대 두께

    void Start()
    {
        brushSizeSlider.onValueChanged.AddListener(UpdateSliderBar);
        UpdateSliderBar(brushSizeSlider.value);
    }

    void UpdateSliderBar(float value)
    {
        // value^2 → 왼쪽은 거의 변화 없음, 오른쪽 끝에서만 증가
        float t = Mathf.Pow(value, 2f);
        float barHeight = Mathf.Lerp(minBarHeight, maxBarHeight, t);

        Vector2 fillSize = fillArea.sizeDelta;
        fillArea.sizeDelta = new Vector2(fillSize.x, barHeight);
    }
}