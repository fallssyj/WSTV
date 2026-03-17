using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WSTV.ViewModels;

namespace WSTV
{
    public partial class MainWindow : Window
    {
        // 缩放热区宽度（像素）
        private const int ResizeBorderThickness = 5;

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            StateChanged += OnWindowStateChanged;
            Closing += OnWindowClosing;
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 主窗口关闭时，若画中画处于浮动状态，先收回再 Dispose
            // 否则 DetachedNoOwner 的浮动窗口会作为独立进程残留
            if (DataContext is MainViewModel vm)
                vm.DisposePlayVm();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            // 最大化时去掉圆角避免黑角，还原时恢复圆角
            var radius = WindowState == WindowState.Maximized
                ? new CornerRadius(0)
                : new CornerRadius(10);
            RootBorder.CornerRadius = radius;
            OverlayBorder.CornerRadius = radius;

            // 同步 ViewModel 状态，让按钮图标跟随切换
            if (DataContext is ViewModels.MainViewModel vm)
                vm.WindowState = WindowState;
        }

        // 注册 WndProc 钩子，实现边缘缩放
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                int hit = GetResizeHitTest(lParam);
                if (hit != HTCLIENT)
                {
                    handled = true;
                    return new IntPtr(hit);
                }
            }
            else if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("user32")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, 0x00000002 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(monitor, ref mi);
                var wa = mi.rcWork; // 工作区（排除任务栏）
                // 以窗口客户端坐标表示：位置相对于左上角，尺寸为工作区大小
                mmi.ptMaxPosition.x = wa.left;
                mmi.ptMaxPosition.y = wa.top;
                mmi.ptMaxSize.x = wa.right - wa.left;
                mmi.ptMaxSize.y = wa.bottom - wa.top;
            }
            // 设置最小跟踪尺寸（考虑 DPI 缩放）
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                int minW = (int)Math.Round(MinWidth * dpi.DpiScaleX);
                int minH = (int)Math.Round(MinHeight * dpi.DpiScaleY);
                if (minW > 0) mmi.ptMinTrackSize.x = minW;
                if (minH > 0) mmi.ptMinTrackSize.y = minH;
            }
            catch { }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private int GetResizeHitTest(IntPtr lParam)
        {
            // lParam 低16位=屏幕X，高16位=屏幕Y
            int screenX = unchecked((short)(lParam.ToInt32() & 0xFFFF));
            int screenY = unchecked((short)((lParam.ToInt32() >> 16) & 0xFFFF));
            Point pt = PointFromScreen(new Point(screenX, screenY));

            int x = (int)pt.X;
            int y = (int)pt.Y;
            int w = (int)ActualWidth;
            int h = (int)ActualHeight;
            int b = ResizeBorderThickness;

            bool onLeft = x < b;
            bool onRight = x > w - b;
            bool onTop = y < b;
            bool onBottom = y > h - b;

            if (onTop && onLeft) return HTTOPLEFT;
            if (onTop && onRight) return HTTOPRIGHT;
            if (onBottom && onLeft) return HTBOTTOMLEFT;
            if (onBottom && onRight) return HTBOTTOMRIGHT;
            if (onTop) return HTTOP;
            if (onBottom) return HTBOTTOM;
            if (onLeft) return HTLEFT;
            if (onRight) return HTRIGHT;

            return HTCLIENT;
        }

        // 拖拽移动：挂在外层 Border 的 MouseLeftButtonDown
        // Button 会标记事件为 Handled，因此点击按钮不会触发此方法
        private void OnWindowDragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}