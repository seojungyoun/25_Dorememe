using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneChange : MonoBehaviour
{
    [System.Serializable]
    public class ButtonData
    {
        public Button button;      // ��ư ������Ʈ
        public string sceneName;   // �̵��� �� �̸�
    }

    public ButtonData[] buttons;           // ��ư + �� �迭
    public Color targetColor = new Color(0.3f, 0.3f, 0.3f); // ��ο� ȸ��
    public float scaleMultiplier = 1.05f;  // ��¦ Ŀ����
    public float effectDuration = 0.3f;    // �ִϸ��̼� �ð�

    void Start()
    {
        // �� ��ư Ŭ�� �� �ڷ�ƾ ����
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

            // ũ�� ��ȭ
            btn.transform.localScale = Vector3.Lerp(originalScale, originalScale * scaleMultiplier, t);

            // ���� ��ȭ
            btnImage.color = Color.Lerp(originalColor, targetColor, t);

            yield return null;
        }

        // �� �̵�
        SceneManager.LoadScene(sceneName);
    }
}