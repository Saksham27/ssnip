using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace SSnip.App.Services;

/// <summary>Windows toasts for finished jobs and a finished queue. No modal pop-ups for routine events.</summary>
public interface INotificationService
{
    void Show(string title, string message);
}

public sealed partial class NotificationService(ILogger<NotificationService> logger) : INotificationService
{
    public void Show(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title, hintMaxLines: 1)
                .AddText(message)
                .Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Notifications can be turned off or unavailable; the queue page still shows everything.
            LogToastFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not show a Windows notification")]
    private partial void LogToastFailed(Exception exception);
}
