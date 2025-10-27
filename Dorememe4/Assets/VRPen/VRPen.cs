using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using System.Collections;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VRPenNamespace
{
    // ==========================================================
    // 1. JSON 파싱을 위한 클래스 정의 (새로 추가됨)
    // ==========================================================
    [Serializable]
    public class JobIdResponse
    {
        public string job_id;
        public string status;
    }

    [Serializable]
    public class StatusResponse
    {
        public string status;
        public string music_url;
        public string error;
    }

    [ExecuteAlways]
    public partial class VRPen : MonoBehaviour
    {
        [SerializeField] private Transform _brushPoint;
        [SerializeField] private float _elementDistance = 0.1f;
        [SerializeField] private float _paintDelay = 0.5f;
        [SerializeField] public Material _material;
        [SerializeField] public float _brushSize = 0.02f;
        [SerializeField] public float _brushAlpha = 1.0f;

        private bool _drawing;

        [SerializeField]
        private Color[] _colors = new[]
        {
            Color.blue, Color.cyan, Color.green, Color.red, Color.yellow,
        };

        private int _colorIndex;
        private Vector3 _lastPosition;
        private float _lastTime;

        private global::VRPenNamespace.IVrPenInput _input;

        // 서버 통신 및 오디오 재생 변수
        public string ServerUrl = "http://127.0.0.1:5000/api/upload_data";
        public string JobStatusUrl = "http://127.0.0.1:5000/api/status"; // ❗ 수정: 끝 슬래시 제거 (URL 조합 안정화)
        public AudioSource MusicAudioSource;
        private const float PollingInterval = 5f;
        private string currentJobId = null;

        void Start()
        {
            InitCore();

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += PlayModeStateChanged;

            void PlayModeStateChanged(PlayModeStateChange obj)
            {
                if (obj == PlayModeStateChange.ExitingPlayMode)
                {
                    if (Scribble != null) Save();
                    else SaveNew();
                }
            }

            if (Scribble != null) Load();
#endif
        }

        private void ChangeColor()
        {
            _colorIndex++;
            _colorIndex %= _colors.Length;

            Color newColor = _colors[_colorIndex];
            newColor.a = _brushAlpha;
            _core.SetColor(newColor);
        }

        public void SetBrushSize(float newSize)
        {
            _brushSize = newSize;
        }

        public void SetBrushAlpha(float newAlpha)
        {
            _brushAlpha = newAlpha;

            Color currentColor = _colors[_colorIndex];
            currentColor.a = _brushAlpha;
            _core.SetColor(currentColor);
        }

        private void InitCore()
        {
            if (_core == null)
            {
                _core = new VRPenCore();
                Color initialColor = _colors[0];
                initialColor.a = _brushAlpha;
                _core.BrushColor = initialColor;
                _core.BrushSize = _brushSize;
                _core.BrushMaterial = _material;
                _core.Start();
            }
        }

        void Update()
        {
            InitCore();
            _core.Draw();

            if (Mathf.Abs(_core.BrushSize - _brushSize) > 0.0001f)
            {
                _core.BrushSize = _brushSize;
                _core.InitPenMesh();
            }

            if (!Application.isPlaying) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                _drawing = false;
                return;
            }

            _core.SetBrushPoint(_brushPoint.position, _brushPoint.rotation);

            if (_input == null || (_input as Component) == null)
            {
                _input = GetComponent<global::VRPenNamespace.IVrPenInput>();
            }

            if (_input.UndoAction)
            {
                Undo();
            }

            if (_input.ChangeColor)
            {
                ChangeColor();
            }

            var drawing = _input.IsDrawing;

            if (_drawing != drawing)
            {
                _drawing = drawing;
                if (drawing)
                {
                    Color strokeColor = _colors[_colorIndex];
                    strokeColor.a = _brushAlpha;
                    _core.NewStroke(strokeColor, false);
                    _lastPosition = _brushPoint.position;
                    _lastTime = Time.time;
                    OnNewStroke?.Invoke();
                    AddPoint();
                    AddPoint();
                }
            }

            if (drawing)
            {
                _core.SetLast(_brushPoint.position);

                if (Time.time - _lastTime > _paintDelay
                    && (_lastPosition - _brushPoint.position).magnitude > 0.001f)
                {
                    AddPoint();
                    return;
                }

                if ((_lastPosition - _brushPoint.position).magnitude > _elementDistance)
                {
                    AddPoint();
                    return;
                }
            }
        }

        private void AddPoint()
        {
            AddPointImpl(_brushPoint.position, false);
            OnPointAdded?.Invoke(_brushPoint.position);
        }

        private void AddPointImpl(Vector3 position, bool load)
        {
            _core.AddPoint(position, load);
            _lastPosition = position;
            _lastTime = Time.time;
        }

        public VRPenScribble Scribble;
        private Transform _container;

        internal Action OnNewStroke;
        internal Action<Vector3> OnPointAdded;
        internal VRPenCore _core;

        [Button]
        public void Clear()
        {
            _core?.Clear();
        }

        [Button]
        public void Undo()
        {
            _core?.RemoveLastStroke();
        }

        [Button]
        public void Load()
        {
            if (Scribble == null) return;
            Clear();
            Add(Scribble);
        }

        public void Add(VRPenScribble scribble)
        {
            if (scribble.Strokes == null) return;

            foreach (var stroke in scribble.Strokes)
            {
                _core.NewStroke(stroke.Color, true);
                foreach (var point in stroke.Points)
                {
                    AddPointImpl(point, true);
                }
            }
        }

        public void TrySave()
        {
            if (Scribble != null)
            {
#if UNITY_EDITOR
                Save();
#endif
            }
        }

        [Button]
        public void Done()
        {
            ExportCSV(); // CSV Export
            StartCoroutine(UploadCSVAndStartPolling()); // CSV 업로드 및 폴링 시작
            Debug.Log("CSV Exported and starting upload...");
        }

        private IEnumerator UploadCSVAndStartPolling() // CSV 업로드 및 서버 작업 폴링 시작
        {
            // ... (파일 저장 및 업로드 로직은 동일) ...
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            const string exportFolderName = "MusicGenerator_Server";
            const string subFolderName = "ExportCSV";
            string exportDir = Path.Combine(projectRoot, exportFolderName, subFolderName);
            const string baseName = "SketchCSV";
            string filePath = Path.Combine(exportDir, $"{baseName}.csv");
            string latestFilePath = filePath;
            if (File.Exists(filePath))
            {
                int maxIndex = 0;
                int tempIndex = 1;
                while (true)
                {
                    string tempPath = Path.Combine(exportDir, $"{baseName}({tempIndex}).csv");
                    if (!File.Exists(tempPath))
                        break;
                    maxIndex = tempIndex;
                    tempIndex++;
                }
                if (maxIndex > 0)
                {
                    latestFilePath = Path.Combine(exportDir, $"{baseName}({maxIndex}).csv");
                }
                else
                {
                    latestFilePath = filePath;
                }
            }
            string finalFilePath = latestFilePath;
            if (!File.Exists(finalFilePath))
            {
                Debug.LogError("CSV file not found: " + finalFilePath);
                yield break;
            }
            byte[] fileData = File.ReadAllBytes(finalFilePath);
            UnityWebRequest request = new UnityWebRequest(ServerUrl, "POST");
            request.uploadHandler = new UploadHandlerRaw(fileData);
            request.downloadHandler = new DownloadHandlerBuffer();
            string fileName = Path.GetFileName(finalFilePath);
            request.SetRequestHeader("Content-Type", "text/csv");
            request.SetRequestHeader("X-File-Name", fileName);

            yield return request.SendWebRequest(); // 업로드 요청

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("File Upload Failed: " + request.error + " | Server Response: " + request.downloadHandler.text);
                yield break;
            }

            string responseText = request.downloadHandler.text;

            try
            {
                currentJobId = ExtractJobIdFromJson(responseText); // Job ID 추출
                Debug.Log("Upload Success. Job ID received: " + currentJobId);

                if (!string.IsNullOrEmpty(currentJobId))
                {
                    StartCoroutine(PollForMusicStatus(currentJobId)); // 상태 폴링 시작
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to parse Job ID from server response: " + e.Message + " | Response: " + responseText);
            }
        }

        private IEnumerator PollForMusicStatus(string jobId) // 주기적으로 서버에 상태 문의
        {
            while (true)
            {
                yield return new WaitForSeconds(PollingInterval); // 5초 대기

                string url = JobStatusUrl + "/" + jobId;

                UnityWebRequest statusRequest = UnityWebRequest.Get(url); // 상태 확인 GET 요청

                yield return statusRequest.SendWebRequest();

                if (statusRequest.result != UnityWebRequest.Result.Success)
                {
                    // 오류 시 응답 텍스트와 함께 출력
                    Debug.LogWarning("Status check failed. Retrying... | Error: " + statusRequest.error
                                         + " | Response: " + statusRequest.downloadHandler.text);
                    continue;
                }

                string response = statusRequest.downloadHandler.text;

                // ❗ 디버깅: 서버 응답 JSON 전문 출력
                Debug.Log($"[Server Response] Job ID: {jobId}, Status JSON: {response}");

                // ❗ 최종 수정: "status":"completed" 대신 "music_url" 필드의 존재 여부만 확인합니다.
                // 이 필드가 존재한다는 것은 서버가 음악 생성을 성공적으로 완료했음을 의미합니다.
                if (response.Contains("music_url") && !response.Contains("\"error\":"))
                {
                    string musicUrl = ExtractMusicUrlFromJson(response);

                    if (!string.IsNullOrEmpty(musicUrl))
                    {
                        // 이 로그가 출력되면 다운로드 단계로 진입 성공!
                        Debug.Log("Music generation completed! Starting download.");
                        StopAllCoroutines();

                        // AudioType.WAV를 사용하도록 이미 수정되었음을 가정합니다.
                        StartCoroutine(DownloadAndPlayMusic(musicUrl));
                        yield break;
                    }
                }

                // 서버에서 명시적으로 실패 상태를 받은 경우 (무한 폴링 종료)
                else if (response.Contains("\"status\":\"failed\""))
                {
                    Debug.LogError("Music generation failed on server: " + response);
                    StopAllCoroutines();
                    yield break;
                }

                // 완료나 실패가 아니면 계속 진행 중
                Debug.Log("Music generation in progress...");
            }
        }

        private IEnumerator DownloadAndPlayMusic(string url) // 음악 파일을 다운로드하고 재생
        {
            UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV); // 오디오 클립 다운로드 요청

            yield return audioRequest.SendWebRequest();

            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Music Download Failed: " + audioRequest.error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(audioRequest); // AudioClip으로 변환

            if (MusicAudioSource != null && clip != null)
            {
                MusicAudioSource.clip = clip;
                MusicAudioSource.Play(); // 음악 재생
                Debug.Log("Music played successfully!");
            }
            else
            {
                Debug.LogError("AudioSource or AudioClip is null.");
            }
        }

        // ❗ 핵심 수정: Job ID를 실제 파싱하는 로직으로 교체
        private string ExtractJobIdFromJson(string json)
        {
            try
            {
                JobIdResponse response = JsonUtility.FromJson<JobIdResponse>(json);

                if (!string.IsNullOrEmpty(response.job_id))
                {
                    return response.job_id; // 서버가 발급한 실제 UUID를 반환
                }

                Debug.LogError("Failed to extract 'job_id'. Status: " + response.status + " | JSON: " + json);
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"JSON Parsing Error (JobId): {e.Message}. Response: {json}");
                return null;
            }
        }

        // ❗ 핵심 수정: Music URL을 실제 파싱하는 로직으로 교체
        private string ExtractMusicUrlFromJson(string json)
        {
            try
            {
                StatusResponse response = JsonUtility.FromJson<StatusResponse>(json);

                if (!string.IsNullOrEmpty(response.music_url))
                {
                    return response.music_url; // 음악 URL 반환
                }

                Debug.LogWarning("Music URL not found in status response. Status: " + response.status + " Error: " + response.error);
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"JSON Parsing Error (MusicUrl): {e.Message}. Response: {json}");
                return null;
            }
        }

#if UNITY_EDITOR
        [Button]
        private void Save()
        {
            SaveCurrentScribble();
            EditorUtility.SetDirty(Scribble);
            AssetDatabase.SaveAssets();
        }

        [Button]
        private void SaveNew()
        {
            Scribble = ScriptableObject.CreateInstance<VRPenScribble>();
            Scribble.name = DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss");
            var dir = "Assets/VRPenScribbles";
            Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(Scribble, $"{dir}/{Scribble.name}.asset");
            AssetDatabase.SaveAssets();
            Save();
        }

        [Button]
        private void ExportObj()
        {
            if (Scribble != null) Save();
            else SaveNew();

            var sb = new StringBuilder();
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            foreach (var stroke in Scribble.Strokes)
            {
                for (int i = 0; i < stroke.Points.Count; i++)
                {
                    var p = stroke.Points[i] - Scribble.Bounds.center;
                    sb.AppendLine($"v {p.x} {p.y} {-p.z} {stroke.Color.r} {stroke.Color.g} {stroke.Color.b}");
                }

                sb.Append("l ");
                for (int i = stroke.Points.Count; i > 0; i--)
                {
                    sb.Append(-i + " ");
                }

                sb.AppendLine();
            }

            Directory.CreateDirectory("Scribbles");
            File.WriteAllText($"Scribbles/{Scribble.name}.obj", sb.ToString());
        }

        [Button]
        private void ExportPLY()
        {
            if (Scribble != null) Save();
            else SaveNew();

            var sb = new StringBuilder();
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            sb.AppendLine(
                @"ply
format ascii 1.0
comment author: VRPen
comment object: " + Scribble.name);

            var vertices = new List<(Color32, Vector3)>();
            var faces = new List<(int, int, int, int)>();

            int indexShift = 0;
            // _core._strokeMeshes의 구조체가 VRPen.cs에 없으므로 임시 타입캐스팅 가정
            foreach (var stroke in _core._strokeMeshes)
            {
                // StrokeMesh 구조체의 구체적인 멤버는 VRPen.cs에 없으므로 원래 코드를 유지
                for (int i = 0; i < stroke.vertex.Count; i++)
                {
                    vertices.Add((stroke.colors[i], stroke.vertex[i]));
                }

                for (int i = 0; i < stroke.quads.Count; i++)
                {
                    var face = stroke.quads[i];
                    faces.Add((
                        indexShift + face.Item1,
                        indexShift + face.Item2,
                        indexShift + face.Item3,
                        indexShift + face.Item4
                    ));
                }

                indexShift += stroke.vertex.Count;
            }

            sb.AppendLine(
                $@"element vertex {vertices.Count}
property float x
property float y
property float z
property uchar red
property uchar green
property uchar blue
element face {faces.Count}
property list uchar int vertex_index
end_header");

            foreach (var v in vertices)
            {
                var p = v.Item2;
                var c = v.Item1;
                sb.AppendLine($"{p.x} {p.z} {p.y}  {c.r} {c.g} {c.b}");
            }

            foreach (var f in faces)
            {
                sb.AppendLine($"4 {f.Item1} {f.Item2} {f.Item4} {f.Item3}");
            }

            Directory.CreateDirectory("Scribbles");
            File.WriteAllText($"Scribbles/{Scribble.name}.ply", sb.ToString());
        }
#endif

        [Button]
        public void ExportCSV() // 드로잉 데이터를 CSV 형식으로 파일에 저장
        {
            if (_core == null || _core._strokeMeshes == null || _core._strokeMeshes.Count == 0)
            {
                Debug.LogWarning("No strokes to export.");
                return;
            }

            var sb = new StringBuilder();

            // 1. 헤더 라인 추가
            sb.AppendLine("StrokeIndex,ColorR,ColorG,ColorB,Count,ColorA,BrushSize,Start_X,Start_Y,Start_Z,End_X,End_Y,End_Z,TotalUndoCount");

            // 2. 색상별 누적 카운트를 위한 딕셔너리
            var colorCounts = new Dictionary<Color, int>();
            int totalUndoCount = _core.UndoCount;

            // 3. 스트로크 데이터 기록
            for (int i = 0; i < _core._strokeMeshes.Count; i++)
            {
                var strokeMesh = _core._strokeMeshes[i];
                var stroke = strokeMesh.Stroke;
                if (stroke == null || stroke.Points.Count == 0) continue;

                Color c = stroke.Color;

                // 동일 색상 누적 카운트
                if (!colorCounts.ContainsKey(c))
                {
                    colorCounts[c] = 0;
                }
                colorCounts[c]++;
                int currentCount = colorCounts[c];

                Vector3 start = stroke.Points.First();
                Vector3 end = stroke.Points.Last();

                sb.AppendLine(
                    $"{i}," + // StrokeIndex
                    $"{c.r:F4},{c.g:F4},{c.b:F4}," + // Color R, G, B
                    $"{currentCount}," + // Count
                    $"{c.a:F4}," + // ColorA
                    $"{stroke.BrushSize:F4}," + // BrushSize
                    $"{start.x:F4},{start.y:F4},{start.z:F4}," + // Start_X, Y, Z
                    $"{end.x:F4},{end.y:F4},{end.z:F4}," + // End_X, Y, Z
                    $"{totalUndoCount}" // TotalUndoCount
                );
            }

            // 4. 파일 저장 로직 (프로젝트 루트의 MusicGenerator_Server/ExportCSV 경로에 저장)

            // Assets 폴더 경로에서 한 단계 상위로 이동하여 프로젝트 루트 폴더 얻기
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            // 프로젝트 루트 폴더에서 MusicGenerator_Server/ExportCSV 폴더를 지정
            const string exportFolderName = "MusicGenerator_Server";
            const string subFolderName = "ExportCSV";

            // 최종 저장 경로: .../프로젝트이름/MusicGenerator_Server/ExportCSV
            string exportDir = Path.Combine(projectRoot, exportFolderName, subFolderName);

            Directory.CreateDirectory(exportDir);

            const string baseName = "SketchCSV";

            string filePath = Path.Combine(exportDir, $"{baseName}.csv");
            int index = 1;

            // 이름 충돌 시 순번 부여 (SketchCSV, SketchCSV(1), ...)
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(exportDir, $"{baseName}({index}).csv");
                index++;
            }

            try
            {
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                Debug.Log($"CSV Exported to custom path: {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"CSV Export Failed at {filePath}: {e.Message}");
            }
        }

        private void SaveCurrentScribble()
        {
            if (Scribble == null) return;
            if (_core == null) return;

            Scribble.Strokes = new List<Stroke>();
            var bounds = new Bounds();
            bool hasBounds = false;

            foreach (var strokeMesh in _core._strokeMeshes)
            {
                if (strokeMesh == null) continue;

                var astroke = new Stroke();
                astroke.Color = strokeMesh.Stroke.Color;
                astroke.Points = new List<Vector3>();
                astroke.BrushSize = strokeMesh.Stroke.BrushSize;
                astroke.BrushAlpha = strokeMesh.Stroke.BrushAlpha;

                for (int i = 0; i < strokeMesh.Stroke.Points.Count; i++)
                {
                    var p = strokeMesh.Stroke.Points[i];
                    astroke.Points.Add(p);
                    if (!hasBounds)
                    {
                        hasBounds = true;
                        bounds = new Bounds(p, Vector3.zero);
                    }
                    bounds.Encapsulate(p);
                }

                Scribble.Strokes.Add(strokeMesh.Stroke);
            }

            Scribble.Bounds = bounds;
        }

        private void OnDrawGizmos()
        {
            if (_brushPoint != null && _colors != null && _colorIndex < _colors.Length)
            {
                Color gizmoColor = _colors[_colorIndex];
                gizmoColor.a = _brushAlpha;
                Gizmos.color = gizmoColor;
                Gizmos.DrawSphere(_brushPoint.position, 0.01f);
            }
        }

        [Serializable]
        public class Stroke
        {
            public Color Color;
            public List<Vector3> Points;
            public float BrushSize;
            public float BrushAlpha;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ButtonAttribute : Attribute
    {
    }
}