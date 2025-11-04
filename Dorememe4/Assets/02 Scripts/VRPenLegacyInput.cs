#if ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine;

namespace VRPenNamespace
{
    public partial class VRPenLegacyInput : MonoBehaviour, global::VRPenNamespace.IVrPenInput
    {
        [SerializeField] private string _changeColorButton;
        [SerializeField] private string _isDrawingButton;
        
        public bool ChangeColor => Input.GetButtonDown(_changeColorButton);
        public bool IsDrawing   => Input.GetButton(_isDrawingButton);
    }
}
#endif