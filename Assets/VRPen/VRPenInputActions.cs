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

        // --- [Undo Action 필드 추가] ---
        [SerializeField] private InputActionReference _undoAction;

        private void OnEnable()
        {
            if (_asset != null) _asset.Enable();
        }

        public bool ChangeColor
        {
            get
            {
                if (_changeColorAction == null) return false;
                // WasPressedThisFrame은 한 프레임 동안 눌렸을 때만 true를 반환합니다.
                return _changeColorAction.action.WasPressedThisFrame();
            }
        }

        public bool IsDrawing
        {
            get
            {
                if (_drawAction == null) return false;
                // 트리거를 완전히 당겼을 때(0.1f 초과) 그리기 시작합니다.
                float value = _drawAction.action.ReadValue<float>();
                return value > 0.1f;
            }
        }

        // --- [Undo 속성 구현 추가] ---
        public bool Undo
        {
            get
            {
                if (_undoAction == null) return false;
                // Undo는 한 번의 클릭/누름에 대해서만 작동해야 하므로 WasPressedThisFrame을 사용합니다.
                // 이것이 왼쪽 컨트롤러의 트리거 입력에 연결되면 됩니다.
                return _undoAction.action.WasPressedThisFrame();
            }
        }
        // --- [Undo 속성 구현 끝] ---
    }
}

#endif