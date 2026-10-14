using System.Windows.Interop;

namespace Lumina.Services;

/// <summary>
/// Görünmez, üst düzey (message-only DEĞİL) pencere. Tepsi geri çağrıları, WM_HOTKEY ve
/// yayın mesajları (TaskbarCreated, WM_DISPLAYCHANGE, WM_SETTINGCHANGE...) buraya gelir.
/// Tam bir WPF Window yerine hafif <see cref="HwndSource"/> kullanılır.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("Lumina.MessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0, // WS_VISIBLE yok → hiç gösterilmez
        };
        _source = new HwndSource(parameters);
    }

    public IntPtr Handle => _source.Handle;

    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);

    public void Dispose() => _source.Dispose();
}
