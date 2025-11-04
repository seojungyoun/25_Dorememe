// SceneChange.cs 파일 수정
using UnityEngine;
using UnityEngine.SceneManagement;
using VRPenNamespace;

namespace VRPenNamespace
{
    public class SceneChange : MonoBehaviour
    {
        public static int SelectedSceneID_Global = -1;
        public void OnSceneSelectButton(int sceneID)
        {
            SelectedSceneID_Global = sceneID;
            Debug.Log($"Scene selected: {sceneID}. Loading default scene.");
        }
        public void LoadSceneByName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("Scene name is empty. Cannot load scene.");
                return;
            }
            Debug.Log($"Loading scene by name: {sceneName}");
            SceneManager.LoadScene(sceneName);
        }
    }
}