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

            // --- [Undo 로직 추가] ---
            if (_input.Undo)
            {
                _core.Undo();
                TrySave(); // Undo 후, 저장된 스크리블도 업데이트
            }
            // --- [Undo 로직 끝] ---

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
        public void ExportCsv()
        {
            // 1. 저장할 디렉터리 설정
            var dir = "ScribbleExports";
            Directory.CreateDirectory(dir);

            // 2. 파일 이름 생성 (누를 때마다 새로운 파일)
            var fileName = DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss") + "_Strokes.csv";
            var filePath = Path.Combine(dir, fileName);

            var sb = new StringBuilder();
            // 소수점 포맷을 일관되게 유지하기 위해 InvariantCulture 사용
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // 색상별 사용 횟수를 기록할 딕셔너리. RGB 값만 키로 사용하기 위해 string을 사용합니다.
            var colorCount = new Dictionary<string, int>();

            // 3. CSV 헤더 설정 (Brightness 제거)
            sb.AppendLine("StrokeIndex,ColorR,ColorG,ColorB,ColorA,BrushSize,Start_X,Start_Y,Start_Z,End_X,End_Y,End_Z");

            if (_core != null && _core._strokeMeshes != null)
            {
                for (int i = 0; i < _core._strokeMeshes.Count; i++)
                {
                    var strokeMesh = _core._strokeMeshes[i];
                    var stroke = strokeMesh.Stroke;

                    if (stroke == null || stroke.Points == null || stroke.Points.Count < 2) continue;

                    // 4. 데이터 추출
                    var color = stroke.Color;
                    var startPos = stroke.Points[0];
                    var endPos = stroke.Points[^1];

                    // RGB 문자열 키 생성 (통계용)
                    // F4 포맷으로 통일하여 부동 소수점 오차를 줄입니다.
                    string rgbKey = $"{color.r:F4},{color.g:F4},{color.b:F4}";

                    // 5. CSV 라인 작성
                    sb.Append($"{i},"); // Stroke Index

                    // 색상 (R, G, B, A)
                    // Brightness 필드는 제거되었습니다.
                    sb.Append($"{color.r:F4},{color.g:F4},{color.b:F4},{color.a:F4},");

                    // 브러쉬 굵기
                    sb.Append($"{_brushSize:F4},");

                    // 시작 위치 (X, Y, Z)
                    sb.Append($"{startPos.x:F4},{startPos.y:F4},{startPos.z:F4},");

                    // 끝 위치 (X, Y, Z)
                    sb.AppendLine($"{endPos.x:F4},{endPos.y:F4},{endPos.z:F4}");

                    // 6. 색상 사용 횟수 업데이트 (RGB만 사용)
                    if (colorCount.ContainsKey(rgbKey))
                    {
                        colorCount[rgbKey]++;
                    }
                    else
                    {
                        colorCount.Add(rgbKey, 1);
                    }
                }
            }

            // 7. 스트로크 데이터와 통계 섹션을 구분합니다.
            sb.AppendLine();
            sb.AppendLine("--- Color Usage Statistics (RGB Only) ---");
            sb.AppendLine("UsedColor(RGB),Count");

            // 8. 색상 사용 횟수 통계 추가 (RGB만 표기)
            foreach (var kvp in colorCount)
            {
                // Key는 이미 "r,g,b" 형식의 문자열입니다.
                sb.AppendLine($"({kvp.Key}),{kvp.Value}");
            }


            // 9. 파일 저장
            File.WriteAllText(filePath, sb.ToString());
            Debug.Log($"CSV Exported: {filePath}");

            // 에디터에서 파일이 생성되었음을 알립니다.
            AssetDatabase.Refresh();
        }

#endif // UNITY_EDITOR

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