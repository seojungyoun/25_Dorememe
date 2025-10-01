using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

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

        // --- Clear 횟수 기록용 필드 추가 ---
        private int _clearCount = 0;

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
        }

        private void InitCore()
        {
            if (_core == null)
            {
                _core = new VRPenCore();
                _core.BrushColor = _colors[0];
                _core.BrushSize = _brushSize;
                _core.BrushMaterial = _material;

                _core.Start();
            }
        }

        void Update()
        {
            InitCore();

            _core.Draw();

            if (!Application.isPlaying) return;

            _core.SetBrushPoint(_brushPoint.position, _brushPoint.rotation);

            if (_input == null || (_input as Component) == null)
            {
                _input = GetComponent<global::VRPenNamespace.IVrPenInput>();
            }

            if (_input.ChangeColor)
            {
                ChangeColor();
                _core.SetColor(_colors[_colorIndex]);
            }

            var drawing = _input.IsDrawing;

            if (_drawing != drawing)
            {
                _drawing = drawing;
                if (drawing)
                {
                    _core.NewStroke(_colors[_colorIndex], false);

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
            _clearCount++;   // --- Clear 호출 횟수 증가 ---
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
        public void ExportCsvOnly()
        {
            // 스케치 데이터가 없으면 함수 종료
            if (_core == null || _core._strokeMeshes.Count == 0)
            {
                Debug.LogWarning("저장할 스케치 데이터가 없습니다.");
                return;
            }

            // 현재 스케치 데이터를 임시 VRPenScribble 객체에 복사
            var tempScribble = ScriptableObject.CreateInstance<VRPenScribble>();
            tempScribble.Strokes = new List<Stroke>();
            var bounds = new Bounds();
            bool hasBounds = false;

            foreach (var strokeMesh in _core._strokeMeshes)
            {
                if (strokeMesh == null) continue;

                var tempStroke = new Stroke();
                tempStroke.Color = strokeMesh.Stroke.Color;
                tempStroke.Points = new List<Vector3>(strokeMesh.Stroke.Points);

                for (int i = 0; i < tempStroke.Points.Count; i++)
                {
                    var p = tempStroke.Points[i];
                    if (!hasBounds)
                    {
                        hasBounds = true;
                        bounds = new Bounds(p, Vector3.zero);
                    }
                    bounds.Encapsulate(p);
                }
                tempScribble.Strokes.Add(tempStroke);
            }
            tempScribble.Bounds = bounds;

            // 현재 시간을 기반으로 유일한 파일명 생성
            var fileName = DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss");

            // CSV 파일 내용 생성
            var sb = new StringBuilder();

            // --- ClearCount 기록 ---
            sb.AppendLine($"ClearCount,{_clearCount}");
            sb.AppendLine();

            // 색상 요약
            var colorCounts = new Dictionary<Color, int>();
            foreach (var stroke in tempScribble.Strokes)
            {
                if (colorCounts.ContainsKey(stroke.Color))
                {
                    colorCounts[stroke.Color]++;
                }
                else
                {
                    colorCounts[stroke.Color] = 1;
                }
            }

            sb.AppendLine("ColorUsageSummary,TotalCount");
            foreach (var entry in colorCounts)
            {
                string colorString = $"{entry.Key.r:F2}_{entry.Key.g:F2}_{entry.Key.b:F2}";
                sb.AppendLine($"{colorString},{entry.Value}");
            }
            sb.AppendLine();
            sb.AppendLine("StrokeIndex,Color,StartPoint.x,StartPoint.y,StartPoint.z,EndPoint.x,EndPoint.y,EndPoint.z");

            for (int i = 0; i < tempScribble.Strokes.Count; i++)
            {
                var stroke = tempScribble.Strokes[i];
                var startPoint = stroke.Points[0];
                var endPoint = stroke.Points[^1];
                string colorString = $"{stroke.Color.r:F2}_{stroke.Color.g:F2}_{stroke.Color.b:F2}";
                string line = $"{i},{colorString},{startPoint.x},{startPoint.y},{startPoint.z},{endPoint.x},{endPoint.y},{endPoint.z}";
                sb.AppendLine(line);
            }

            // 파일 경로 설정 및 저장
            var dir = "Scribbles";
            Directory.CreateDirectory(dir);
            var filePath = $"{dir}/{fileName}_summary.csv";
            File.WriteAllText(filePath, sb.ToString());

            Debug.Log($"CSV 파일이 성공적으로 저장되었습니다: {filePath}");
#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
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
                sb.AppendLine($"{p.x} {p.z} {p.y} {c.r} {c.g} {c.b}");
            }

            foreach (var f in faces)
            {
                sb.AppendLine($"4 {f.Item1} {f.Item2} {f.Item4} {f.Item3}");
            }

            Directory.CreateDirectory("Scribbles");
            File.WriteAllText($"Scribbles/{Scribble.name}.ply", sb.ToString());
        }

#endif

        private void SaveCurrentScribble()
        {
            if (Scribble == null) return;
            if (_core == null) return;

            Scribble.Strokes = new List<Stroke>();
            var bounds = new Bounds();
            bool hasBounds = false;

            foreach (var stroke in _core._strokeMeshes)
            {
                if (stroke == null) continue;

                var astroke = new Stroke();
                astroke.Color = stroke.Stroke.Color;
                astroke.Points = new List<Vector3>();

                for (int i = 0; i < stroke.Stroke.Points.Count; i++)
                {
                    var p = stroke.Stroke.Points[i];
                    astroke.Points.Add(p);
                    if (!hasBounds)
                    {
                        hasBounds = true;
                        bounds = new Bounds(p, Vector3.zero);
                    }

                    bounds.Encapsulate(p);
                }

                Scribble.Strokes.Add(stroke.Stroke);
            }

            Scribble.Bounds = bounds;
        }

        private void OnDrawGizmos()
        {
            if (_brushPoint != null && _colors != null && _colorIndex < _colors.Length)
            {
                Gizmos.color = _colors[_colorIndex];
                Gizmos.DrawSphere(_brushPoint.position, 0.01f);
            }
        }

        [Serializable]
        public class Stroke
        {
            public Color Color;
            public List<Vector3> Points;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ButtonAttribute : Attribute
    {
    }
}
