using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MiviaDesktop;

class ImageDirectoryWatcher
{
    private readonly string directory;
    private readonly Func<string, Task> callback;
    private FileSystemWatcher watcher;

    public ImageDirectoryWatcher(string directory, Func<string, Task> callback)
    {
        this.directory = directory;
        this.callback = callback;
    }

    public void Start()
    {
        watcher = new FileSystemWatcher(directory);
        watcher.Filter = "*.*";
        watcher.IncludeSubdirectories = false;

        watcher.Created += OnFileCreated;

        watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        watcher?.Dispose();
    }
    
    private async Task<bool> WaitForFileReady(string filePath)
    {
        DateTime startTime = DateTime.Now;
        bool isFileReady = false;
        while (!isFileReady && DateTime.Now - startTime < TimeSpan.FromSeconds(10))
        {
            try
            {
                using FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                // File is not locked, it's available for operations
                isFileReady = true;
            }
            catch (IOException)
            {
                // File is locked or in use, wait for a short duration before trying again
                await Task.Delay(500);
            }
        }

        return isFileReady;
    }


    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (IsImageFile(e.FullPath))
        {
            await WaitForFileReady(e.FullPath);

            // Perform your operations on the file here
            await callback(e.FullPath);
        }
    }


    private bool IsImageFile(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

        return Array.Exists(imageExtensions, ext => ext.Equals(extension));
    }
}