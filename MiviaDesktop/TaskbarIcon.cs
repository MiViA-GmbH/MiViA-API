using System;
using System.Drawing;
using System.Windows;

namespace MiviaDesktop;
using Forms = System.Windows.Forms;

public class TaskbarIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;
    
    public TaskbarIcon(MainWindow reference)
    {
        _mainWindow = reference;
    
        _notifyIcon = new Forms.NotifyIcon();
        _notifyIcon.Text = "MiViA";
        _notifyIcon.Icon = new System.Drawing.Icon("Resources/icon_gray.ico");
        _notifyIcon.Click += NotifyIconOnClick;
        _notifyIcon.ContextMenuStrip = new Forms.ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("Configure", Image.FromFile("Resources/icon.ico"), OnConfigClick);
        _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, OnExitClick);
        
        _notifyIcon.Visible = true;
        
    }


    public void SetActiveIcon()
    {
        _notifyIcon.Icon = new System.Drawing.Icon("Resources/icon.ico");
    }
        
    public void SetInactiveIcon()
    {
        _notifyIcon.Icon = new System.Drawing.Icon("Resources/icon_gray.ico");
    }
    
    private void OnExitClick(object? sender, EventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void OnConfigClick(object? sender, EventArgs e)
    {
        MaximizeWindow();
    }
    
    private void NotifyIconOnClick(object? sender, EventArgs e)
    {
        // MaximizeWindow();
    }
    
    private void MaximizeWindow()
    {
        if (!_mainWindow.IsVisible) _mainWindow.Show();

        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ShowError(string message)
    {
        _notifyIcon.ShowBalloonTip(2000, "MiViA", message, Forms.ToolTipIcon.Error);
    }


    public void ShowMessage(string message)
    {
        _notifyIcon.ShowBalloonTip(2000, "Mivia", message, Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}