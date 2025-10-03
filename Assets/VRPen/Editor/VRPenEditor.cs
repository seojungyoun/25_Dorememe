using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRPenNamespace.Editors
{
    [CustomEditor(typeof(VRPenNamespace.VRPen))]
    public class VRPenEditor : Editor
    {
        private static Dictionary<Type, (MethodInfo, string)[]> _methodsByType = new();

        private (MethodInfo method, string name)[] _methods;
        private string[]                           _files;

        private void OnEnable()
        {
            var allMethods = TypeCache.GetMethodsWithAttribute<ButtonAttribute>();

            if (!_methodsByType.TryGetValue(target.GetType(), out var methods))
            {
                methods = allMethods
                          .Where(p => p.DeclaringType == target.GetType() && p.GetParameters().Length == 0)
                          .Select(p => (p, ObjectNames.NicifyVariableName(p.Name)))
                          .ToArray();

                _methodsByType[target.GetType()] = methods;
            }

            _methods = methods;

            var vrpen = target as VRPen;
            if (!vrpen.TryGetComponent<global::VRPenNamespace.IVrPenInput>(out var input))
            {
#if ENABLE_INPUT_SYSTEM
                vrpen.gameObject.AddComponent<VRPenNamespace.VRPenInputActions>();
#elif ENABLE_LEGACY_INPUT_MANAGER
                vrpen.gameObject.AddComponent<VRPenLegacyInput>();
#endif
            }

            EditorApplication.playModeStateChanged += PlayModeStateChanged;
        }

        private void PlayModeStateChanged(PlayModeStateChange obj)
        {
            if (target is VRPen vrPen && obj == PlayModeStateChange.ExitingPlayMode)
            {
                vrPen.TrySave();
            }
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            var vrPen = (VRPen)target;

            foreach (var pair in _methods)
            {
                if (GUILayout.Button(pair.name))
                {
                    pair.method.Invoke(target, null);
                }
            }
        }
    }
}