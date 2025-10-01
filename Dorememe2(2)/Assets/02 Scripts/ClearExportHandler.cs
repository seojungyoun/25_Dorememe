using UnityEngine;
using UnityEngine.InputSystem;
using VRPenNamespace; // VRPen이 있는 네임스페이스

public class ClearExportHandler : MonoBehaviour, ClearExport.IButtonActionActions
{
    private ClearExport _actions;
    private VRPen _pen;

    private void Awake()
    {
        _actions = new ClearExport();
        _pen = GetComponent<VRPen>();
    }

    private void OnEnable()
    {
        _actions.ButtonAction.SetCallbacks(this);
        _actions.ButtonAction.Enable();
    }

    private void OnDisable()
    {
        _actions.ButtonAction.Disable();
    }

    // XR 왼손 X 버튼 → Clear
    public void OnClear(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            _pen?.Clear();
        }
    }

    // XR 오른손 A 버튼 → ExportCSV
    public void OnExportCSV(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
#if UNITY_EDITOR   // 에디터에서만 CSV 내보내기
            _pen?.ExportCsvOnly();
#endif
        }
    }
}
