using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MMPET;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x80000;
    private const uint WS_EX_TRANSPARENT = 0x20;
    private const uint WS_EX_TOPMOST = 0x8;
    
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private BattleEntitiesAPI? _api;
    private readonly Dictionary<int, TextBlock> _petLabels = new();
    private MainWindow? _mainWindow;
    




    public OverlayWindow()
    {
        InitializeComponent();
        
        // 设置窗口状态，尺寸和位置将由GetGameWindowSize动态设置
        this.WindowState = WindowState.Normal;
        
        // 窗口加载完成后设置透明穿透
        this.Opened += OverlayWindow_Opened;
        this.SizeChanged += OverlayWindow_SizeChanged;
        
        Console.WriteLine("覆盖窗口已创建");
    }

    private void OverlayWindow_Opened(object? sender, EventArgs e)
    {
        Console.WriteLine("覆盖窗口已打开");
        
        // 首先设置窗口位置和尺寸
        GetGameWindowSize();
        
        // 设置窗口为透明穿透（鼠标事件穿透）
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var hwnd = this.TryGetPlatformHandle()?.Handle;
            if (hwnd.HasValue)
            {
                // 设置窗口样式
                uint exStyle = GetWindowLong(hwnd.Value, GWL_EXSTYLE);
                SetWindowLong(hwnd.Value, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST);
                
                // 强制设置为最顶层
                SetWindowPos(hwnd.Value, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                

            }
        }
        

    }

    private void OverlayWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {

    }

    public void SetAPI(BattleEntitiesAPI api)
    {
        _api = api;
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }



    /// <summary>
    /// 强制设置窗口为最顶层
    /// </summary>
    public void ForceTopMost()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var hwnd = this.TryGetPlatformHandle()?.Handle;
            if (hwnd.HasValue)
            {
                SetWindowPos(hwnd.Value, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }

    public void UpdatePetOverlay(List<PetDisplayInfo> pets)
    {
        if (_api == null) return;

        try
        {
            // 获取摄像头信息
            var cameraLocation = _api.GetCameraLocation();
            var cameraRotation = _api.GetCameraRotation();

            // 检测游戏窗口尺寸
            var gameWindowSize = GetGameWindowSize();
            

            


            // 清除旧的宠物标签（保留调试元素）
            ClearPetLabels();
            _petLabels.Clear();

            int labelCount = 0;
            foreach (var pet in pets)
            {
                if (pet.EntityInfo == null) continue;

                var petPosition = pet.EntityInfo.Position;
                
                // 使用覆盖层的实际尺寸进行坐标计算
                var screenWidth = this.Bounds.Width;
                var screenHeight = this.Bounds.Height;
                
                // 使用宽松的FOV算法进行坐标转换
                var screenPos = MathUtils.RelaxedFOVWorldToScreen(
                    petPosition, 
                    cameraLocation, 
                    cameraRotation,
                    screenWidth, 
                    screenHeight,
                    90.0 // 固定FOV角度
                );
                
                if (screenPos.HasValue)
                {
                    // 限制到屏幕范围内
                    var overlayWidth = this.Bounds.Width;
                    var overlayHeight = this.Bounds.Height;
                    var clampedX = Math.Max(0, Math.Min(overlayWidth - 100, screenPos.Value.X)); // 留100像素边距
                    var clampedY = Math.Max(0, Math.Min(overlayHeight - 30, screenPos.Value.Y)); // 留30像素边距
                    

                    
                    // 创建宠物名称标签
                    var label = CreatePetLabel(pet, new Point(clampedX, clampedY));
                    OverlayCanvas.Children.Add(label);
                    _petLabels[pet.NpcId] = label;
                    labelCount++;
                }
                // 如果坐标转换失败，跳过此宠物
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新覆盖层失败: {ex.Message}");
        }
    }

    private TextBlock CreatePetLabel(PetDisplayInfo pet, Point screenPos)
    {
        var label = new TextBlock
        {
            Text = pet.DisplayName,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), // 更不透明的黑色背景
            Padding = new Thickness(6, 3),
            FontSize = 14,
            FontWeight = FontWeight.Bold
        };

        // 设置位置
        Canvas.SetLeft(label, screenPos.X);
        Canvas.SetTop(label, screenPos.Y);

        return label;
    }

    private Point? WorldToScreen(FVector worldPos, FVector cameraPos, FRotator cameraRot)
    {
        // 使用更精确的数学工具类进行转换
        var screenPos = MathUtils.WorldToScreen(
            worldPos, 
            cameraPos, 
            cameraRot, 
            this.Bounds.Width, 
            this.Bounds.Height, 
            90.0 // FOV
        );

        return screenPos.HasValue ? new Point(screenPos.Value.X, screenPos.Value.Y) : null;
    }

    private bool IsOnScreen(Point screenPos)
    {
        return screenPos.X >= 0 && screenPos.X <= this.Bounds.Width &&
               screenPos.Y >= 0 && screenPos.Y <= this.Bounds.Height;
    }

    public void ClearOverlay()
    {
        ClearPetLabels();
        _petLabels.Clear();
    }

    private void ClearPetLabels()
    {
        // 清除所有宠物标签
        OverlayCanvas.Children.Clear();
    }

    private double GetDPIScale()
    {
        try
        {
            var hdc = GetDC(IntPtr.Zero);
            var dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            ReleaseDC(IntPtr.Zero, hdc);
            
            var scale = dpiX / 96.0; // 96 DPI是标准DPI
            return scale;
        }
        catch
        {
            return 1.0; // 默认无缩放
        }
    }

    private (int Width, int Height)? GetGameWindowSize()
    {
        try
        {
            // 通过进程名查找游戏窗口
            var gameWindow = IntPtr.Zero;
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName("EM-Win64-Shipping");
                if (processes.Length > 0 && processes[0].MainWindowHandle != IntPtr.Zero)
                {
                    gameWindow = processes[0].MainWindowHandle;
                }
            }
            catch { }
            
            if (gameWindow == IntPtr.Zero)
            {
                Console.WriteLine("未找到游戏窗口");
            }
            
            if (gameWindow != IntPtr.Zero)
            {
                // 获取游戏窗口的位置和尺寸
                if (GetWindowRect(gameWindow, out RECT windowRect) && GetClientRect(gameWindow, out RECT clientRect))
                {
                    // 使用ClientToScreen获取客户区的准确屏幕位置
                    POINT clientOrigin = new POINT { X = 0, Y = 0 };
                    ClientToScreen(gameWindow, ref clientOrigin);
                    
                    var clientLeft = clientOrigin.X;
                    var clientTop = clientOrigin.Y;
                    
                    // 获取DPI缩放比例
                    var dpiScale = GetDPIScale();
                    
                    // 对尺寸进行DPI缩放，位置使用原始坐标
                    var targetWidth = clientRect.Width / dpiScale;
                    var targetHeight = clientRect.Height / dpiScale;
                    var targetLeft = clientLeft;
                    var targetTop = clientTop;
                    
                    // 调整覆盖窗口尺寸和位置
                    this.Width = targetWidth;
                    this.Height = targetHeight;
                    this.Position = new PixelPoint((int)targetLeft, (int)targetTop);
                    
                    return (clientRect.Width, clientRect.Height);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取游戏窗口信息失败: {ex.Message}");
        }
        
        // 如果检测失败，使用默认设置
        this.Width = 1280;
        this.Height = 720;
        this.Position = new PixelPoint(0, 0);
        
        return (1280, 720);
    }




}