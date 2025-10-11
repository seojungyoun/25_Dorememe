using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Linq;


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
        public void ExportCSV()
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

            sb.AppendLine("Stroke Index,Color R,Color G,Color B,Luminance,Brush Size,Start X,Start Y,Start Z,End X,End Y,End Z");

            for (int i = 0; i < _core._strokeMeshes.Count; i++)
            {
                var strokeMesh = _core._strokeMeshes[i];
                var stroke = strokeMesh.Stroke;
                if (stroke == null || stroke.Points.Count == 0) continue;

                Color c = stroke.Color;
                float luminance = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                Vector3 start = stroke.Points.First();
                Vector3 end = stroke.Points.Last();

                sb.AppendLine($"{i + 1},{c.r:F3},{c.g:F3},{c.b:F3},{luminance:F3},{_core.BrushSize:F4},{start.x:F4},{start.y:F4},{start.z:F4},{end.x:F4},{end.y:F4},{end.z:F4}");
            }

            sb.AppendLine();

            sb.AppendLine($"Undo Count,{_core.UndoCount}");

            // --- 저장 경로 수정 ---
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
