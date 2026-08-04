using System.Drawing;
using System.Windows.Forms;

namespace FanControl.Service.Tray;

/// <summary>
/// 同进程托盘：在独立 STA 线程上运行 WinForms 消息泵，
/// 与 WinUI 主线程通过回调 + DispatcherQueue 协作。
/// </summary>
public sealed class TrayHost : IDisposable
{
    private Thread? _thread;
    private TrayContext? _context;

    public void Start(Action showUi, Action exit, Action? reconnect = null)
    {
        _context = new TrayContext(showUi, exit, reconnect);
        _thread = new Thread(() => Application.Run(_context))
        {
            IsBackground = true,
            Name = "FanControl.Tray",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>更新托盘图标悬停提示（跨线程安全）。</summary>
    public void SetStatus(string text)
    {
        _context?.SetStatus(text);
    }

    /// <summary>弹出托盘气泡通知（跨线程安全）。</summary>
    public void ShowNotification(string title, string text)
    {
        _context?.ShowNotification(title, text);
    }

    public void Dispose()
    {
        _context?.Exit();
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly Action _showUi;
    private readonly Action _exit;
    private readonly Action? _reconnect;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _autostartItem = new("开机自启");
    private readonly Control _marshaller = new();

    public TrayContext(Action showUi, Action exit, Action? reconnect = null)
    {
        _showUi = showUi;
        _exit = exit;
        _reconnect = reconnect;
        _ = _marshaller.Handle; // 强制创建句柄，供跨线程 BeginInvoke
        _trayIcon = new NotifyIcon
        {
            // 优先使用主程序 exe 的图标（ApplicationIcon），失败时回退系统默认图标
            Icon = Environment.ProcessPath is { } path
                ? Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application
                : SystemIcons.Application,
            Text = "FanControl",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => _showUi();

        _ = RefreshAutostartStateAsync();
    }

    public void SetStatus(string text)
    {
        try
        {
            if (!_marshaller.IsHandleCreated)
            {
                return;
            }

            _marshaller.BeginInvoke(new Action(() =>
            {
                try
                {
                    _trayIcon.Text = text.Length > 63 ? text[..63] : text;
                }
                catch
                {
                    // 托盘图标已销毁
                }
            }));
        }
        catch
        {
            // 托盘线程未就绪时忽略
        }
    }

    public void ShowNotification(string title, string text)
    {
        try
        {
            if (!_marshaller.IsHandleCreated)
            {
                return;
            }

            _marshaller.BeginInvoke(new Action(() =>
            {
                try
                {
                    _trayIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Warning);
                }
                catch
                {
                    // 通知失败不影响主流程
                }
            }));
        }
        catch
        {
            // 托盘线程未就绪时忽略
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("打开主界面");
        openItem.Click += (_, _) => _showUi();
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        var reconnectItem = new ToolStripMenuItem("重建连接");
        reconnectItem.Click += (_, _) => _reconnect?.Invoke();
        menu.Items.Add(reconnectItem);

        menu.Items.Add(new ToolStripSeparator());

        _autostartItem.Click += async (_, _) => await ToggleAutostartAsync(_autostartItem);
        menu.Items.Add(_autostartItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => _exit();
        menu.Items.Add(exitItem);

        return menu;
    }

    private async Task RefreshAutostartStateAsync()
    {
        try
        {
            _autostartItem.Checked = await AutostartManager.IsEnabledAsync();
        }
        catch
        {
            // 忽略状态刷新失败
        }
    }

    private async Task ToggleAutostartAsync(ToolStripMenuItem item)
    {
        try
        {
            if (item.Checked)
            {
                await AutostartManager.DisableAsync();
                item.Checked = false;
            }
            else
            {
                await AutostartManager.EnableAsync();
                item.Checked = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置开机自启失败：{ex.Message}", "FanControl");
        }
    }

    public void Exit()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
