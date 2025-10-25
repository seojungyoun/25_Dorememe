using UnityEngine;
using UnityEngine.UI;

public class BrushHandleTransparency : MonoBehaviour
{
    public Slider brushSlider;       // 슬라이더 오브젝트
    public Image handleImage;        // 핸들 이미지

    [Range(0.1f, 1f)]
    public float minAlpha = 0.3f;   // 왼쪽 끝 투명도
    public float maxAlpha = 1f;     // 오른쪽 끝 투명도

    void Start()
    {
        if (handleImage == null)
        {
            Debug.LogError("Handle Image가 연결되지 않았습니다!");
            return;
        }

        brushSlider.onValueChanged.AddListener(UpdateHandleTransparency);
        UpdateHandleTransparency(brushSlider.value);
    }

    void UpdateHandleTransparency(float value)
    {
        // value^2로 부채꼴 느낌
        float t = Mathf.Pow(value, 2f);
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);

        Color c = handleImage.color;
        c.a = alpha;
        handleImage.color = c;
    }
}
