using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class CSVUploader : MonoBehaviour
{
    public string serverURL = "http://127.0.0.1:5000/api/upload_data";

    public IEnumerator UploadCSV(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"CSV file not found: {filePath}");
            yield break;
        }

        Debug.Log($"CSV Exported and starting upload: {filePath}");

        byte[] fileBytes = File.ReadAllBytes(filePath);
        string fileName = Path.GetFileName(filePath);

        //수동으로 multipart/form-data 전송
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", fileBytes, fileName, "text/csv");

        using (UnityWebRequest www = UnityWebRequest.Post(serverURL, form))
        {
            www.SetRequestHeader("Accept", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($" File Upload Failed: {www.responseCode} {www.error}");
                Debug.LogError($"Server says: {www.downloadHandler.text}");
            }
            else
            {
                Debug.Log($"Upload Success: {www.downloadHandler.text}");
            }
        }
    }
}
