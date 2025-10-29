#if ENABLE_INPUT_SYSTEM

using UnityEngine;
using UnityEngine.InputSystem;

namespace VRPenNamespace
{
    public partial class VRPenInputActions : MonoBehaviour, global::VRPenNamespace.IVrPenInput
    {
        [SerializeField] private InputActionAsset _asset;
        [SerializeField] private InputActionReference _drawAction;
        [SerializeField] private InputActionReference _changeColorAction;

        [SerializeField] private InputActionReference _undoAction;

        private void OnEnable()
        {
            if (_asset != null) _asset.Enable();
            if (_undoAction != null && _undoAction.action != null)
            {
                _undoAction.action.Enable();
            }
        }

        public bool UndoAction
        {
            get
            {
                if (_undoAction == null) return false;
                return _undoAction.action.WasPressedThisFrame();
            }
        }

        public bool ChangeColor
        {
            get
            {
                if (_changeColorAction == null) return false;
                return _changeColorAction.action.WasPressedThisFrame();
            }
        }

        public bool IsDrawing
        {
            get
            {
                if (_drawAction == null) return false;
                float value = _drawAction.action.ReadValue<float>();
                return value > 0.1f;
            }
        }
    }
}

#endif