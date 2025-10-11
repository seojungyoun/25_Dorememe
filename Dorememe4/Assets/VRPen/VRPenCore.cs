using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRPenNamespace
{
    internal class VRPenCore
    {
        public float BrushSize;
        public Color BrushColor;
        public Vector3 BrushPosition;
        public Quaternion BrushRotation = Quaternion.identity;
        public Material BrushMaterial;

        // ⭐ 1. Undo 횟수 카운터 추가
        public int UndoCount { get; private set; } = 0;

        private Matrix4x4 _matrix;
        private Mesh _penMesh;

        internal List<StrokeMesh> _strokeMeshes = new();
        internal StrokeMesh _currentMesh;
        private MaterialPropertyBlock _brushPB;

        private static readonly int _Color = Shader.PropertyToID("_Color");

        public void Start()
        {
            _matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);

            _penMesh = new Mesh();

            var shift = BrushSize;

            _penMesh.vertices = new[]
            {
                Vector3.forward * shift,
                Vector3.left * shift,
                Vector3.up * shift,
                Vector3.right * shift,
                Vector3.down * shift,
                Vector3.back * shift
            };

            _penMesh.triangles = new[]
            {
                1, 0, 2,
                2, 0, 3,
                3, 0, 4,
                4, 0, 1,

                1, 5, 2,
                2, 5, 3,
                3, 5, 4,
                4, 5, 1,
            };

            _penMesh.colors = Enumerable.Repeat(Color.white, _penMesh.vertexCount).ToArray();

            _brushPB ??= new MaterialPropertyBlock();
            _brushPB.SetColor(_Color, BrushColor);
        }

        internal void NewStroke(Color color, bool load)
        {
            _currentMesh = new StrokeMesh(BrushSize);
            _currentMesh.Stroke = new VRPen.Stroke()
            {
                Color = color,
                Points = new List<Vector3>()
            };

            if (!load)
            {
                _currentMesh.SetLast(BrushPosition);
            }

            _strokeMeshes.Add(_currentMesh);
        }

        internal void Draw()
        {
            foreach (var strokeMesh in _strokeMeshes)
            {
                if (strokeMesh != null && strokeMesh.Mesh != null)
                {
                    Graphics.DrawMesh(strokeMesh.Mesh, _matrix, BrushMaterial, 0);
                }
            }

            Graphics.DrawMesh(_penMesh,
                             Matrix4x4.TRS(BrushPosition, BrushRotation, Vector3.one),
                             BrushMaterial, 0, null, 0, _brushPB);
        }

        public void Clear()
        {
            foreach (var mesh in _strokeMeshes)
            {
                if (mesh.Mesh != null) UnityEngine.Object.Destroy(mesh.Mesh);
            }
            _strokeMeshes.Clear();
            _currentMesh = null;
        }

        public void RemoveLastStroke()
        {
            if (_strokeMeshes.Count > 0)
            {
                int lastIndex = _strokeMeshes.Count - 1;
                StrokeMesh lastMesh = _strokeMeshes[lastIndex];

                if (lastMesh.Mesh != null)
                {
                    UnityEngine.Object.Destroy(lastMesh.Mesh);
                }

                _strokeMeshes.RemoveAt(lastIndex);

                if (_currentMesh == lastMesh)
                {
                    _currentMesh = null;
                }

                // ⭐ 2. Undo 횟수 증가
                UndoCount++;
            }
        }

        internal class StrokeMesh
        {
            public VRPen.Stroke Stroke;

            public Mesh Mesh;

            private List<Node> dirs = new();
            internal readonly List<Vector3> vertex = new();
            internal readonly List<Color> colors = new();
            internal readonly List<(int, int, int, int)> quads = new();
            private List<int> tris = new();

            private readonly float _shift;

            public StrokeMesh(float brushSize)
            {
                _shift = brushSize / 2;
            }

            struct Node
            {
                public Vector3 forward;
                public Vector3 left;
                public Vector3 up;
                public Vector3 pos;
            }

            public void BuildMesh()
            {
                var points = Stroke.Points;

                if (points.Count < 2) return;

                if (Mesh == null)
                {
                    Mesh = new Mesh();
                    Mesh.MarkDynamic();
                }

                dirs.Clear();

                for (int i = 1; i < points.Count - 1; i++)
                {
                    var from = points[i - 1];
                    var to = points[i];
                    var next = points[i + 1];

                    var forward = to - from;
                    var forwardNext = next - to;

                    forward = Vector3.Slerp(forward.normalized, forwardNext.normalized, 0.5f);

                    var left = Vector3.zero;
                    var up = Vector3.zero;

                    if (dirs.Count > 0)
                    {
                        left = dirs[dirs.Count - 1].left;
                        up = dirs[dirs.Count - 1].up;
                    }

                    Vector3.OrthoNormalize(ref forward, ref left, ref up);

                    dirs.Add(new Node()
                    {
                        forward = forward,
                        left = left,
                        up = up,
                        pos = to
                    });
                }

                {
                    var forward = (points[1] - points[0]).normalized;
                    var left = dirs.Count > 0 ? dirs[0].left : Vector3.left;
                    var up = dirs.Count > 0 ? dirs[0].up : Vector3.up;

                    Vector3.OrthoNormalize(ref forward, ref left, ref up);

                    dirs.Insert(0, new Node()
                    {
                        forward = forward,
                        up = up,
                        left = left,
                        pos = points[0]
                    });
                }

                {
                    var forward = (points[points.Count - 1] - points[points.Count - 2]).normalized;
                    var left = dirs.Count > 0 ? dirs[dirs.Count - 1].left : Vector3.left;
                    var up = dirs.Count > 0 ? dirs[dirs.Count - 1].up : Vector3.up;

                    Vector3.OrthoNormalize(ref forward, ref left, ref up);

                    dirs.Add(new Node()
                    {
                        forward = forward,
                        up = up,
                        left = left,
                        pos = points[points.Count - 1]
                    });
                }

                vertex.Clear();
                quads.Clear();
                tris.Clear();
                colors.Clear();

                for (int i = 0; i < dirs.Count; i++)
                {
                    vertex.Add(dirs[i].pos - dirs[i].left * _shift);
                    vertex.Add(dirs[i].pos + dirs[i].up * _shift);
                    vertex.Add(dirs[i].pos + dirs[i].left * _shift);
                    vertex.Add(dirs[i].pos - dirs[i].up * _shift);

                    colors.Add(Stroke.Color);
                    colors.Add(Stroke.Color);
                    colors.Add(Stroke.Color);
                    colors.Add(Stroke.Color);
                }

                for (int i = 0; i < vertex.Count - 4; i += 4)
                {
                    quads.Add((i + 0, i + 1, i + 0 + 4, i + 1 + 4));
                    quads.Add((i + 1, i + 2, i + 1 + 4, i + 2 + 4));
                    quads.Add((i + 2, i + 3, i + 2 + 4, i + 3 + 4));
                    quads.Add((i + 3, i + 0, i + 3 + 4, i + 0 + 4));
                }

                for (int i = 0; i < quads.Count; i++)
                {
                    tris.AddRange(new[]
                    {
                        quads[i].Item1,
                        quads[i].Item3,
                        quads[i].Item2,
                    });

                    tris.AddRange(new[]
                    {
                        quads[i].Item2,
                        quads[i].Item3,
                        quads[i].Item4,
                    });
                }

                Mesh.Clear();
                Mesh.SetVertices(vertex);
                Mesh.SetColors(colors);
                Mesh.SetTriangles(tris, 0, true);
            }

            public void AddPoint(Vector3 position, bool load)
            {
                var points = Stroke.Points;
                if (!load)
                {
                    points.Insert(points.Count - 1, position);
                }
                else
                {
                    points.Add(position);
                }


                if (points.Count < 2) return;

                BuildMesh();
            }

            public void SetLast(Vector3 position)
            {
                if (Stroke.Points.Count == 0)
                {
                    Stroke.Points.Add(position);
                }
                else
                {
                    Stroke.Points[^1] = position;
                }
            }
        }

        public void AddPoint(Vector3 position, bool load)
        {
            _currentMesh.AddPoint(position, load);
        }

        public void SetColor(Color color)
        {
            if (color == BrushColor) return;
            BrushColor = color;

            _brushPB ??= new MaterialPropertyBlock();
            _brushPB.SetColor(_Color, BrushColor);
        }

        public void SetBrushPoint(Vector3 pos, Quaternion rot)
        {
            BrushPosition = pos;
            BrushRotation = rot;
        }

        public void SetLast(Vector3 brushPosition)
        {
            BrushPosition = brushPosition;
            if (_currentMesh != null)
            {
                _currentMesh.SetLast(BrushPosition);
                _currentMesh.BuildMesh();
            }
        }
    }
}