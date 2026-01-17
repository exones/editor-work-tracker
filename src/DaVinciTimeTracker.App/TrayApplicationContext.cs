using DaVinciTimeTracker.Core.Models;
using DaVinciTimeTracker.Core.Utilities;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Windows.UI.Notifications;

namespace DaVinciTimeTracker.App;

public class TrayApplicationContext : ApplicationContext
{
    private NotifyIcon _trayIcon;
    private ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _statusItem;
    private ToolStripMenuItem _openDashboardItem;
    private ToolStripMenuItem _viewLatestLogItem;
    private ToolStripMenuItem _openLogsFolderItem;
    private ToolStripMenuItem _autoStartItem;
    private ToolStripMenuItem _exitItem;
    private System.Windows.Forms.Timer _updateTimer;
    private AutoStartManager _autoStartManager;

    // Static cached icons for different states - created once
    private static readonly Icon _grayIcon = CreateColoredIcon(Color.Gray);
    private static readonly Icon _yellowIcon = CreateColoredIcon(Color.Orange);
    private static readonly Icon _greenIcon = CreateColoredIcon(Color.LimeGreen);

    public TrayApplicationContext()
    {
        _autoStartManager = new AutoStartManager(Log.Logger);
        _contextMenu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem("Status: Idle");
        _statusItem.Enabled = false;

        _openDashboardItem = new ToolStripMenuItem("Open Dashboard", null, OnOpenDashboard);

        _viewLatestLogItem = new ToolStripMenuItem("View Latest Log", null, OnViewLatestLog);
        _openLogsFolderItem = new ToolStripMenuItem("Open Logs Folder", null, OnOpenLogsFolder);

        _autoStartItem = new ToolStripMenuItem("Start with Windows", null, OnAutoStartToggle);
        _autoStartItem.CheckOnClick = true;
        _autoStartItem.Checked = _autoStartManager.IsAutoStartEnabled();

        _exitItem = new ToolStripMenuItem("Exit", null, OnExit);

        _contextMenu.Items.Add(_statusItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_openDashboardItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_viewLatestLogItem);
        _contextMenu.Items.Add(_openLogsFolderItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_autoStartItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = _grayIcon,
            ContextMenuStrip = _contextMenu,
            Visible = true,
            Text = "DaVinci Time Tracker"
        };

        _trayIcon.DoubleClick += (s, e) => OnOpenDashboard(s, e);

        // Update status periodically
        _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _updateTimer.Tick += UpdateStatus;
        _updateTimer.Start();

        // Subscribe to session events for notifications
        AppState.SessionManager.SessionStarted += OnSessionStarted;
        AppState.SessionManager.SessionEnded += OnSessionEnded;

        UpdateStatus(null, EventArgs.Empty);
    }

    private void UpdateStatus(object? sender, EventArgs e)
    {
        var state = AppState.SessionManager.CurrentState;
        var projectName = AppState.SessionManager.CurrentProjectName;

        switch (state)
        {
            case TrackingState.GraceStart:
                _statusItem.Text = $"⏱ Grace Start: {projectName} [Not tracking yet]";
                _trayIcon.Text = $"Grace Start: {projectName} [Not tracking yet]";
                _trayIcon.Icon = _yellowIcon;
                break;
            case TrackingState.Tracking:
                _statusItem.Text = $"● Tracking: {projectName}";
                _trayIcon.Text = $"Tracking: {projectName}";
                _trayIcon.Icon = _greenIcon;
                break;
            case TrackingState.GraceEnd:
                var graceTimeRemaining = AppState.SessionManager.GraceEndElapsedTime;
                var minutesRemaining = graceTimeRemaining.HasValue
                    ? (int)(10 - graceTimeRemaining.Value.TotalMinutes)
                    : 10;
                _statusItem.Text = $"⏳ Tracking: {projectName} [Grace Period - {minutesRemaining}m]";
                _trayIcon.Text = $"Tracking: {projectName} [Grace Period - {minutesRemaining}m]";
                _trayIcon.Icon = _greenIcon; // Keep green during Grace End - we're still tracking
                break;
            default:
                _statusItem.Text = "○ Not tracking";
                _trayIcon.Text = "DaVinci Time Tracker - Idle";
                _trayIcon.Icon = _grayIcon;
                break;
        }
    }

    private void OnSessionStarted(object? sender, ProjectSession session)
    {
        Log.Information("📢 Notification: Tracking started for {ProjectName}", session.ProjectName);

        try
        {
            var toast = new ToastContentBuilder()
                .AddText("🟢 Tracking Started")
                .AddText($"Now tracking: {session.ProjectName}")
                .GetToastContent();

            ToastNotificationManagerCompat.CreateToastNotifier().Show(
                new ToastNotification(toast.GetXml()));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show toast notification for tracking start");
        }
    }

    private void OnSessionEnded(object? sender, ProjectSession session)
    {
        var duration = session.EndTime.HasValue
            ? session.EndTime.Value - session.StartTime
            : TimeSpan.Zero;

        var durationText = duration.TotalHours >= 1
            ? $"{duration.Hours}h {duration.Minutes}m"
            : $"{duration.Minutes}m {duration.Seconds}s";

        Log.Information("📢 Notification: Tracking stopped for {ProjectName}, Duration: {Duration}",
            session.ProjectName, durationText);

        try
        {
            var toast = new ToastContentBuilder()
                .AddText("⚪ Tracking Stopped")
                .AddText($"{session.ProjectName}")
                .AddText($"Duration: {durationText}")
                .GetToastContent();

            ToastNotificationManagerCompat.CreateToastNotifier().Show(
                new ToastNotification(toast.GetXml()));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show toast notification for tracking stop");
        }
    }

    private static Icon CreateColoredIcon(Color color)
    {
        // Create a 16x16 bitmap for tray icon
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Draw filled circle
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, 2, 2, 12, 12);
            }

            // Draw border for better visibility
            using (var pen = new Pen(Color.FromArgb(180, color), 1.5f))
            {
                g.DrawEllipse(pen, 2, 2, 12, 12);
            }
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void OnOpenDashboard(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "http://localhost:5555",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open dashboard: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnViewLatestLog(object? sender, EventArgs e)
    {
        try
        {
            var logsDirectory = AppPaths.LogsDirectory;

            if (!Directory.Exists(logsDirectory))
            {
                MessageBox.Show("Logs directory not found.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var logFiles = Directory.GetFiles(logsDirectory, "*.log");
            if (logFiles.Length == 0)
            {
                MessageBox.Show("No log files found.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var latestLog = logFiles
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .First();

            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{latestLog.FullName}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open latest log file");
            MessageBox.Show($"Failed to open log file: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnOpenLogsFolder(object? sender, EventArgs e)
    {
        try
        {
            var logsDirectory = AppPaths.LogsDirectory;

            if (!Directory.Exists(logsDirectory))
            {
                MessageBox.Show("Logs directory not found.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{logsDirectory}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open logs folder");
            MessageBox.Show($"Failed to open logs folder: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnAutoStartToggle(object? sender, EventArgs e)
    {
        var shouldEnable = _autoStartItem.Checked;
        var success = shouldEnable
            ? _autoStartManager.EnableAutoStart()
            : _autoStartManager.DisableAutoStart();

        if (success)
        {
            var message = shouldEnable
                ? "DaVinci Time Tracker will now start automatically with Windows"
                : "Auto-start disabled";
            _trayIcon.ShowBalloonTip(2000, "Auto-start", message, ToolTipIcon.Info);
        }
        else
        {
            // Revert checkbox state on failure
            _autoStartItem.Checked = !shouldEnable;
            MessageBox.Show(
                "Failed to update auto-start setting. Please check the logs.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        _updateTimer.Stop();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
            _contextMenu?.Dispose();
            _updateTimer?.Dispose();
            // Note: Static icons (_grayIcon, _yellowIcon, _greenIcon) are not disposed here
            // as they're shared across the application lifetime
        }
        base.Dispose(disposing);
    }
}
