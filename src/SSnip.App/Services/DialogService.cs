using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SSnip.App.Services;

/// <summary>File and folder pickers, behind an interface so view models stay free of UI types.</summary>
public interface IDialogService
{
    string? PickFolder(string title, string? initialDirectory = null);

    string? PickExecutable(string title, string fileName);

    void OpenInExplorer(string path);

    /// <summary>Opens Explorer with the file selected.</summary>
    void RevealInExplorer(string filePath);

    string? GetClipboardText();

    void SetClipboardText(string text);

    /// <summary>Opens a file with its default app, for example a job log in the text editor.</summary>
    void OpenFile(string path);

    /// <summary>Asks before something that cannot be undone, such as clearing the queue.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);
}

public sealed class DialogService : IDialogService
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickExecutable(string title, string fileName)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            FileName = fileName,
            Filter = $"{fileName}|{fileName}|Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void OpenInExplorer(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
    }

    public void RevealInExplorer(string filePath)
    {
        if (!File.Exists(filePath))
        {
            OpenInExplorer(Path.GetDirectoryName(filePath) ?? filePath);
            return;
        }

        // explorer.exe parses /select itself, so the path is quoted into one argument string.
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = false });
    }

    public string? GetClipboardText()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another app holds the clipboard open.
            return null;
        }
    }

    public void SetClipboardText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
        };

        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }
}
