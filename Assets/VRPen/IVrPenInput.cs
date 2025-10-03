namespace VRPenNamespace
{
    public partial interface IVrPenInput
    {
        bool ChangeColor { get; }
        bool IsDrawing   { get; }
        bool Undo        { get; }
    }
}