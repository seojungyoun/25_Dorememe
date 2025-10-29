using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System;

public class MusicUploader : MonoBehaviour
{
    [Header("Server Config")]
    public string UploadUrl = "http://127.0.0.1:5000/api/upload_data";
    public string JobStatusUrl = "http://127.0.0.1:5000/api/status";
    public float PollingInterval = 5f;

    private string currentJobId = null;
    private AudioSource audioSource;

    [Serializable]
    public class JobIdResponse
    {
        public string job_id;
        public string status;
        public string fast_url;
    }

    [Serializable]
    public class StatusResponse
    {
        public string status;
        public string fast_url;
        public string final_url;
        public string error;
    }

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    // CSV 업로드 시작
    public void UploadCSV(string filePath)
    {
        StartCoroutine(UploadCSVCoroutine(filePath));
    }

    private IEnumerator UploadCSVCoroutine(string filePath)
    {
        byte[] fileData = File.ReadAllBytes(filePath);
        string fileName = Path.GetFileName(filePath);

        UnityWebRequest request = new UnityWebRequest(UploadUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(fileData);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/octet-stream");
        request.SetRequestHeader("X-File-Name", fileName);

        Debug.Log("Uploading CSV to server...");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Upload failed: " + request.error);
            yield break;
        }

        string responseText = request.downloadHandler.text;
        Debug.Log("Upload response: " + responseText);

        var response = JsonUtility.FromJson<JobIdResponse>(responseText);
        currentJobId = response.job_id;

        if (!string.IsNullOrEmpty(response.fast_url))
        {
            Debug.Log("Fast preview ready. Playing...");
            StartCoroutine(DownloadAndPlayMusic(response.fast_url));
        }

        if (!string.IsNullOrEmpty(currentJobId))
        {
            StartCoroutine(PollForFinalMusic(currentJobId));
        }
    }

    // 음악 다운로드 후 재생
    private IEnumerator DownloadAndPlayMusic(string url)
    {
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Music download failed: " + request.error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                Debug.LogError("Invalid audio clip");
                yield break;
            }

            audioSource.clip = clip;
            audioSource.loop = true;
            audioSource.Play();
        }
    }

    // 최종 음악 생성 완료될 때까지 주기적으로 확인
    private IEnumerator PollForFinalMusic(string jobId)
    {
        while (true)
        {
            yield return new WaitForSeconds(PollingInterval);

            UnityWebRequest req = UnityWebRequest.Get(JobStatusUrl + "/" + jobId);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                continue;

            var status = JsonUtility.FromJson<StatusResponse>(req.downloadHandler.text);
            Debug.Log($"[Polling] status={status.status}");

            if (!string.IsNullOrEmpty(status.final_url))
            {
                Debug.Log("Final music ready! Replacing preview...");
                StartCoroutine(DownloadAndPlayMusic(status.final_url));
                yield break;
            }
        }
    }
}
