# Lumina

<div align="center">

**Windows 10/11 için Sıfır Bağımlılıklı, Ultra Hafif DDC/CI Monitör Parlaklık Yöneticisi & OSD**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20(2004%2B)%20%7C%2011-0078D6?style=flat&logo=windows&logoColor=white)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Lisans: MIT](https://img.shields.io/badge/Lisans-MIT-F5A623?style=flat&logo=open-source-initiative&logoColor=white)](LICENSE)
[![Boşta CPU](https://img.shields.io/badge/Bo%C5%9Fta%20CPU-%250-2ea44f?style=flat)](https://github.com/dogueu/Lumina)

[ **English Documentation** ](README.md)

</div>

---

## Özellikler

- **Donanımsal Monitör Kontrolü (DDC/CI):** Harici monitörlerinizin gerçek arka aydınlatmasını doğrudan donanım düzeyinde (`dxva2.dll`) yönetir; ekrana sahte karartma katmanı koymaz.
- **3 Görünüm Modu:**
  - **Renkli cam:** İsteğe bağlı `Windows.UI.Composition` (`HostBackdropBrush`) bulanıklaştırma ve ayarlanabilir cam opaklığı.
  - **Sistem:** Windows vurgu rengini takip eden klasik Windows 11 Hızlı Ayarlar estetiği.
  - **Katı:** Windows açık/koyu temasıyla uyumlu sade ve opak yüzey.
- **Windows 11 Ölçülerinde OSD:** Odak çalmayan, tıklanamayan (`192 × 46` DIP) kompakt ekran göstergesi ve bulanıklığı koruyan pürüzsüz giriş/çıkış animasyonu.
- **Sıfır Arka Plan Yükü:** Olay güdümlü Win32 mimarisi (`RegisterHotKey`, `Shell_NotifyIcon`, `WM_DISPLAYCHANGE`); boşta %0 CPU ve arka planda otomatik çalışma kümesi sıkıştırma.
- **Monitör Bazlı Kontrol ve Sıralama:** Özel monitör adları, her monitör için kısayol/tepsi tekerleği dahil etme anahtarı ve Ayarlar içinden sürükle-bırak ile monitör sıralama.
- **Tepsi Tekerleği ve Global Kısayollar:** Sağ alttaki simge üzerinde tekerleği çevirerek veya tam ekran uygulamalarda global kısayollarla (`Varsayılan: Ctrl + Alt + ↑ / ↓`) anında parlaklık ayarı.
- **Hafıza ve Otomatik Başlatma:** Monitör başına son parlaklık değerlerini ve sırasını oturumlar arasında hatırlar; isteğe bağlı olarak `HKCU\...\Run` üzerinden sessiz başlatılır.

---

## Kurulum ve Çalıştırma

### 1. Hazır Taşınabilir Sürüm (Önerilen)
Kurulum veya ek .NET çalışma zamanı gerektirmez:
1. [**Releases (Sürümler)**](https://github.com/dogueu/Lumina/releases) bölümünden en güncel `Lumina.exe` dosyasını indirin.
2. Dosyayı kalıcı bir klasöre (örn. *Belgelerim* veya *C:\Tools\Lumina*) koyun.
3. Çift tıklayarak çalıştırın; Lumina doğrudan sistem tepsisine yerleşecektir.

### 2. Kaynak Koddan Derleme (Geliştiriciler İçin)
Gereksinimler:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 (Sürüm 2004 / Derleme 19041+) veya Windows 11

```bash
# Depoyu klonlayın
git clone https://github.com/dogueu/Lumina.git
cd Lumina

# Geliştirici modunda çalıştırın
dotnet run

# Sıkıştırılmış tek dosya bağımsız (portable) exe üretin
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o Publish
```

---

## Kontroller ve Varsayılan Kısayollar

| İşlem | Varsayılan | Açıklama |
|---|---|---|
| **Parlaklığı Artır** | `Ctrl + Alt + ↑` | Kısayolu aktif monitörlerin parlaklığını artırır ve OSD gösterir. |
| **Parlaklığı Azalt** | `Ctrl + Alt + ↓` | Kısayolu aktif monitörlerin parlaklığını azaltır ve OSD gösterir. |
| **Hızlı Ayar Paneli** | Tepsi Sol Tık | Monitör kaydırıcılarını ve gömülü Ayarlar görünümünü açar. |
| **Tekerlekle Ayar** | Tepsi Üzerinde Tekerlek | İkon üzerindeyken parlaklığı hızlıca değiştirir. |
| **Seçenekler** | Tepsi Sağ Tık | Ayarlar, monitörleri yenile ve çıkış menüsü. |

---

## Mimari ve Teknolojiler

- **Framework:** Saf [.NET 8 Windows Presentation Foundation (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/) — sıfır harici NuGet paketi.
- **Bulanıklaştırma Katmanı:** `Windows.UI.Composition` (`HostBackdropBrush` WinRT interop).
- **Monitör API:** Windows Physical Monitor Configuration API (`dxva2.dll` / DDC/CI).
- **Native Entegrasyon:** Win32 P/Invoke (`user32.dll`, `shell32.dll`, `dwmapi.dll`, `shcore.dll`, `gdi32.dll`).

---

## Lisans

- Uygulama kaynak kodu [MIT Lisansı](LICENSE) kapsamındadır.
- Gömülü `Geist-SemiBold.ttf` yazı tipi [SIL Open Font License 1.1](Fonts/OFL.txt) (Vercel) lisansıyla dağıtılmaktadır.
