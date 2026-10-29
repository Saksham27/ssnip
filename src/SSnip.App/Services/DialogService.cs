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
}
