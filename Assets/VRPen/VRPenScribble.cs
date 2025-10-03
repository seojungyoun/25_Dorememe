using System.Collections.Generic;
using UnityEngine;

namespace VRPenNamespace
{
    [CreateAssetMenu(fileName = "Scribble", menuName = "VRPen/Scribble", order = 0)]
    public class VRPenScribble : ScriptableObject
    {
        public List<VRPen.Stroke> Strokes;
        public Bounds             Bounds;
    }
}