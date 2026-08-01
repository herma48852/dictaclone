namespace DictaClone.App.Presentation;

public enum OverlayStatus
{
    Recording,
    Processing,
    Success,
    Failure,
}

public interface IStatusOverlay
{
    void ShowStatus(OverlayStatus status, string? message = null);

    void HideStatus();
}
