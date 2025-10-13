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
        public string ServerUrl = "http://your-server-ip:port/api/upload_data"; // 드로잉 데이터(CSV)를 업로드할 서버 주소
        public string JobStatusUrl = "http://your-server-ip:port/api/status/"; // 음악 생성 작업 상태를 확인할 서버 주소
        public AudioSource MusicAudioSource; // 다운로드한 음악을 재생할 AudioSource 컴포넌트
        private const float PollingInterval = 5f; // 서버 상태를 확인하는 주기 (5초)
        private string currentJobId = null; // 서버에서 받은 비동기 작업(음악 생성) ID

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
            ExportCSV(); // 1. 드로잉 데이터를 CSV 파일로 내보내기
            StartCoroutine(UploadCSVAndStartPolling()); // 2. CSV 업로드 및 서버 작업 상태 폴링 시작
            Debug.Log("CSV Exported and starting upload...");
        }

        private IEnumerator UploadCSVAndStartPolling() // CSV를 서버에 업로드하고 Job ID를 받음
        {
            string name = Scribble != null ? Scribble.name : "Untitled";
            string exportDir = Path.Combine(Application.persistentDataPath, "ScribbleExports");
            string fileName = $"{name}_Data_{DateTime.Now:HH-mm-ss}.csv";
            string filePath = Path.Combine(exportDir, fileName);

            if (!File.Exists(filePath))
            {
                Debug.LogError("CSV file not found: " + filePath);
                yield break;
            }

            byte[] fileData = File.ReadAllBytes(filePath);

            UnityWebRequest request = new UnityWebRequest(ServerUrl, "POST"); // POST 요청 준비 (업로드)
            request.uploadHandler = new UploadHandlerRaw(fileData);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "text/csv");
            request.SetRequestHeader("X-File-Name", fileName);

            yield return request.SendWebRequest(); // 서버로 업로드 요청 전송

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("File Upload Failed: " + request.error);
                yield break;
            }

            string responseText = request.downloadHandler.text;

            try
            {
                // Note: Replace this with proper JsonUtility parsing in a real project
                currentJobId = ExtractJobIdFromJson(responseText); // 서버 응답에서 작업 ID 추출
                Debug.Log("Upload Success. Job ID received: " + currentJobId);

                if (!string.IsNullOrEmpty(currentJobId))
                {
                    StartCoroutine(PollForMusicStatus(currentJobId)); // Job ID로 상태 폴링 코루틴 시작
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to parse Job ID from server response: " + e.Message + " | Response: " + responseText);
            }
        }

        private IEnumerator PollForMusicStatus(string jobId) // 주기적으로 서버에 음악 생성 상태를 문의함
        {
            while (true)
            {
                yield return new WaitForSeconds(PollingInterval); // 5초 대기

                string url = JobStatusUrl + jobId;
                UnityWebRequest statusRequest = UnityWebRequest.Get(url); // 상태 확인 GET 요청

                yield return statusRequest.SendWebRequest();

                if (statusRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("Status check failed. Retrying... | Error: " + statusRequest.error);
                    continue;
                }

                string response = statusRequest.downloadHandler.text;

                if (response.Contains("completed") && response.Contains("http")) // 작업 완료 및 음악 URL이 포함되어 있는지 확인
                {
                    string musicUrl = ExtractMusicUrlFromJson(response); // 음악 다운로드 URL 추출

                    Debug.Log("Music generation completed! Starting download.");
                    StopAllCoroutines();
                    StartCoroutine(DownloadAndPlayMusic(musicUrl)); // 음악 다운로드 및 재생 시작
                    yield break;
                }
                else
                {
                    Debug.Log("Music generation in progress..."); // 아직 작업 중
                }
            }
        }

        private IEnumerator DownloadAndPlayMusic(string url) // 음악 파일을 다운로드하고 재생함
        {
            UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG); // 오디오 클립 다운로드 요청

            yield return audioRequest.SendWebRequest();

            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Music Download Failed: " + audioRequest.error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(audioRequest); // 다운로드된 데이터를 AudioClip으로 변환

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

        private string ExtractJobIdFromJson(string json)
        {
            // Placeholder: Replace with actual JsonUtility parsing in a real project
            // 서버 응답에서 job_id를 실제 파싱하는 로직이 필요함
            if (json.Contains("job_id"))
                return "job-" + DateTime.Now.Ticks.ToString();
            return null;
        }

        private string ExtractMusicUrlFromJson(string json)
        {
            // Placeholder: Replace with actual JsonUtility parsing in a real project
            // 서버 응답에서 음악 URL을 실제 파싱하는 로직이 필요함
            return "http://your-server-ip:port/music/generated_track.mp3";
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
            foreach (var stroke in _core._strokeMeshes)
            {
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
            sb.AppendLine("VRPen Export Data");
            sb.AppendLine($"Export Time,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            sb.AppendLine("Color Usage Summary (R,G,B),Count");
            var colorGroups = _core._strokeMeshes
                .GroupBy(s => s.Stroke.Color)
                .Select(g => new { Color = g.Key, Count = g.Count() });

            foreach (var g in colorGroups)
            {
                sb.AppendLine($"{g.Color.r:F3},{g.Color.g:F3},{g.Color.b:F3},{g.Count}");
            }

            sb.AppendLine();

            sb.AppendLine("Stroke Index,Color R,Color G,Color B,Alpha,Luminance,Brush Size,Start X,Start Y,Start Z,End X,End Y,End Z");

            for (int i = 0; i < _core._strokeMeshes.Count; i++)
            {
                var strokeMesh = _core._strokeMeshes[i];
                var stroke = strokeMesh.Stroke;
                if (stroke == null || stroke.Points.Count == 0) continue;

                Color c = stroke.Color;
                float luminance = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                Vector3 start = stroke.Points.First();
                Vector3 end = stroke.Points.Last();

                sb.AppendLine($"{i + 1},{c.r:F3},{c.g:F3},{c.b:F3},{stroke.BrushAlpha:F3},{luminance:F3},{stroke.BrushSize:F4},{start.x:F4},{start.y:F4},{start.z:F4},{end.x:F4},{end.y:F4},{end.z:F4}");
            }

            sb.AppendLine();

            sb.AppendLine($"Undo Count,{_core.UndoCount}");

            string exportDir = Path.Combine(Application.persistentDataPath, "ScribbleExports");
            Directory.CreateDirectory(exportDir);

            string name = Scribble != null ? Scribble.name : "Untitled";
            string filePath = Path.Combine(exportDir, $"{name}_Data_{DateTime.Now:HH-mm-ss}.csv");

            try
            {
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif

                Debug.Log($"CSV Exported: {filePath}");
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