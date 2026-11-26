using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lumina.Native;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Lumina.Services;

/// <summary>
/// Şeffaf pencerenin hemen arkasında Windows.UI.Composition HostBackdropBrush ile
/// pürüzsüz, köşeleri kırpmalı cam bulanıklığı çizen yardımcı katman penceresi.
/// </summary>
internal sealed class BlurUnderlay : IDisposable
{
    private const int DispatcherQueueThreadCurrent = 2;
    private const int DispatcherQueueApartmentSta = 2;

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

    private static IntPtr _queue;
    private static Compositor? _compositor;
    private static ICompositorDesktopInterop? _interop;

    private readonly IntPtr _ownerHwnd;
    private readonly HwndSource _host;
    private readonly HwndSource? _ownerSource;
    private readonly HwndSourceHook _ownerHook;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly CompositionBrush _brush;
    private readonly CompositionRoundedRectangleGeometry _clip;
    private readonly CompositionGeometricClip _geoClip;
    private readonly SpriteVisual _sprite;
    private float _radius;
    private bool _disposed;

    public static BlurUnderlay? Attach(Window owner)
    {
        try { return new BlurUnderlay(owner); }
        catch (Exception ex)
        {
            AppDiagnostics.Trace($"BlurUnderlay başlatılamadı: {ex.Message}");
            return null;
        }
    }

    private BlurUnderlay(Window owner)
    {
        _ownerHwnd = new WindowInteropHelper(owner).EnsureHandle();

        if (_compositor is null)
        {
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(
                new DispatcherQueueOptions
                {
                    Size = Marshal.SizeOf<DispatcherQueueOptions>(),
                    ThreadType = DispatcherQueueThreadCurrent,
                    ApartmentType = DispatcherQueueApartmentSta,
                },
                out _queue));
            _compositor = new Compositor();
            IntPtr unknown = ((WinRT.IWinRTObject)_compositor).NativeObject.ThisPtr;
            _interop = (ICompositorDesktopInterop)Marshal.GetObjectForIUnknown(unknown);
        }

        _host = new HwndSource(new HwndSourceParameters("LuminaBlur")
        {
            WindowStyle = User32.WS_POPUP,
            ExtendedWindowStyle = User32.WS_EX_NOREDIRECTIONBITMAP
                                | User32.WS_EX_NOACTIVATE
                                | User32.WS_EX_TOOLWINDOW
                                | User32.WS_EX_TRANSPARENT
                                | User32.WS_EX_TOPMOST,
            Width = 1,
            Height = 1,
        });
        _host.AddHook(HostWndProc);

        int on = 1;
        DwmApi.DwmSetWindowAttribute(_host.Handle, DwmApi.DWMWA_USE_HOSTBACKDROPBRUSH, ref on, sizeof(int));

        _interop!.CreateDesktopWindowTarget(_host.Handle, true, out IntPtr raw);
        try { _target = DesktopWindowTarget.FromAbi(raw); }
        finally { Marshal.Release(raw); }

        _root = _compositor.CreateContainerVisual();
        _brush = _compositor.CreateHostBackdropBrush();
        _clip = _compositor.CreateRoundedRectangleGeometry();
        _geoClip = _compositor.CreateGeometricClip(_clip);
        _sprite = _compositor.CreateSpriteVisual();
        _sprite.Brush = _brush;
        _sprite.Clip = _geoClip;
        _root.Children.InsertAtTop(_sprite);
        _target.Root = _root;

        _ownerHook = OwnerWndProc;
        _ownerSource = HwndSource.FromHwnd(_ownerHwnd);
        _ownerSource?.AddHook(_ownerHook);
        owner.Closed += (_, _) => Dispose();
    }

    private static IntPtr HostWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_NCHITTEST)
        {
            handled = true;
            return new IntPtr(User32.HTTRANSPARENT);
        }
        if (msg == User32.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(User32.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private IntPtr OwnerWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_WINDOWPOSCHANGED) Sync();
        return IntPtr.Zero;
    }

    public void Update(double radiusPx, double opacity)
    {
        if (_disposed) return;
        _radius = (float)Math.Max(0, radiusPx);
        _root.Opacity = (float)Math.Clamp(opacity, 0, 1);
        Sync();
    }

    private void Sync()
    {
        if (_disposed || !User32.GetWindowRect(_ownerHwnd, out var r)) return;
        int w = r.Width;
        int h = r.Height;
        bool show = w > 0 && h > 0 && _root.Opacity > 0 && User32.IsWindowVisible(_ownerHwnd) && !User32.IsIconic(_ownerHwnd);

        if (show)
        {
            _clip.Size = new Vector2(w, h);
            _clip.CornerRadius = new Vector2(_radius, _radius);
            _sprite.Size = new Vector2(w, h);
        }

        User32.SetWindowPos(_host.Handle, _ownerHwnd, r.Left, r.Top, w, h,
            User32.SWP_NOACTIVATE | User32.SWP_NOOWNERZORDER | (show ? User32.SWP_SHOWWINDOW : User32.SWP_HIDEWINDOW));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownerSource?.RemoveHook(_ownerHook);
        _host.RemoveHook(HostWndProc);
        _target.Root = null;
        _root.Children.RemoveAll();
        _sprite.Dispose();
        _geoClip.Dispose();
        _clip.Dispose();
        _root.Dispose();
        _brush.Dispose();
        _target.Dispose();
        _host.Dispose();
    }
}
