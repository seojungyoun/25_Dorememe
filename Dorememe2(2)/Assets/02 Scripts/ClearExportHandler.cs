using UnityEngine;
using UnityEngine.InputSystem;
using VRPenNamespace; // VRPen�� �ִ� ���ӽ����̽�

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

    // XR �޼� X ��ư �� Clear
    public void OnClear(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            _pen?.Clear();
        }
    }

    // XR ������ A ��ư �� ExportCSV
    public void OnExportCSV(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
#if UNITY_EDITOR   // �����Ϳ����� CSV ��������
            _pen?.ExportCsvOnly();
#endif
        }
    }
}
