using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneChange : MonoBehaviour
{
    [System.Serializable]
    public class ButtonData
    {
        public Button button;      // 버튼 오브젝트
        public string sceneName;   // 이동할 씬 이름
    }

    public ButtonData[] buttons;           // 버튼 + 씬 배열
    public Color targetColor = new Color(0.3f, 0.3f, 0.3f); // 어두운 회색
    public float scaleMultiplier = 1.05f;  // 살짝 커지기
    public float effectDuration = 0.3f;    // 애니메이션 시간

    void Start()
    {
        // 각 버튼 클릭 시 코루틴 실행
        foreach (var bd in buttons)
        {
            bd.button.onClick.AddListener(() => StartCoroutine(PlayEffectAndLoadScene(bd.button, bd.sceneName)));
        }
    }

    IEnumerator PlayEffectAndLoadScene(Button btn, string sceneName)
    {
        Vector3 originalScale = btn.transform.localScale;
        Image btnImage = btn.GetComponent<Image>();
        Color originalColor = btnImage.color;

        float timer = 0f;
        while (timer < effectDuration)
        {
            timer += Time.deltaTime;
            float t = timer / effectDuration;

            // 크기 변화
            btn.transform.localScale = Vector3.Lerp(originalScale, originalScale * scaleMultiplier, t);

            // 색상 변화
            btnImage.color = Color.Lerp(originalColor, targetColor, t);

            yield return null;
        }

        // 씬 이동
        SceneManager.LoadScene(sceneName);
    }
}