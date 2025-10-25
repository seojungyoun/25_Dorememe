using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class PanelFadeAutoHide_Canvas : MonoBehaviour
{
    public GameObject panel;        // �ȳ��� Panel ����
    public float delay = 3f;        // ��ٸ��� �ð�
    public float fadeDuration = 1f; // ������� �ð�

    private Graphic[] graphics;     // Panel�� �ڽ� UI ��� ��� ����

    void Start()
    {
        if (panel != null)
        {
            // Panel�� ��� �ڽ� UI ���(Image, Text ��) ��������
            graphics = panel.GetComponentsInChildren<Graphic>();

            if (graphics.Length == 0)
            {
                Debug.LogWarning("Panel�̳� �ڽ� UI�� Graphic ������Ʈ�� �����ϴ�!");
                return;
            }

            StartCoroutine(FadeOutPanel());
        }
    }

    IEnumerator FadeOutPanel()
    {
        yield return new WaitForSeconds(delay); // ���� delay ��ٸ�

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

        panel.SetActive(false); // ������ ������� Panel ��Ȱ��ȭ
    }
}