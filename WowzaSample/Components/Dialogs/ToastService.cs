namespace WowzaSample.Components.Dialogs;

/// <summary>
/// Application-wide toast notification service.
/// Registered as scoped in DI. Components subscribe to OnToastAdded to display toasts.
/// </summary>
public class ToastService
{
    public event Action<ToastMessage>? OnToastAdded;

    public void ShowInfo(string message)
        => OnToastAdded?.Invoke(new ToastMessage(ToastType.Info, message));

    public void ShowError(string message)
        => OnToastAdded?.Invoke(new ToastMessage(ToastType.Error, message));

    public void ShowSuccess(string message)
        => OnToastAdded?.Invoke(new ToastMessage(ToastType.Success, message));

    public void ShowWarning(string message)
        => OnToastAdded?.Invoke(new ToastMessage(ToastType.Warning, message));
}

public enum ToastType { Info, Success, Warning, Error }

public record ToastMessage(ToastType Type, string Text, DateTime Timestamp)
{
    public ToastMessage(ToastType type, string text) : this(type, text, DateTime.Now) { }
}
