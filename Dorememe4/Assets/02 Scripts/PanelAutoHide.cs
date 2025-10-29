using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class PanelFadeAutoHide_Canvas : MonoBehaviour
{
    public GameObject panel;        // 안내문 Panel 연결
    public float delay = 3f;        // 기다리는 시간
    public float fadeDuration = 1f; // 사라지는 시간

    private Graphic[] graphics;     // Panel과 자식 UI 요소 모두 담음

    void Start()
    {
        if (panel != null)
        {
            // Panel과 모든 자식 UI 요소(Image, Text 등) 가져오기
            graphics = panel.GetComponentsInChildren<Graphic>();

            if (graphics.Length == 0)
            {
                Debug.LogWarning("Panel이나 자식 UI에 Graphic 컴포넌트가 없습니다!");
                return;
            }

            StartCoroutine(FadeOutPanel());
        }
    }

    IEnumerator FadeOutPanel()
    {
        yield return new WaitForSeconds(delay); // 먼저 delay 기다림

        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float t = timer / fadeDuration;

            foreach (var g in graphics)
            {
                Color original = g.color;
                g.color = new Color(original.r, original.g, original.b, Mathf.Lerp(1f, 0f, t));
            }

            yield return null;
        }

        panel.SetActive(false); // 완전히 사라지면 Panel 비활성화
    }
}