# Lumina

<div align="center">

**Windows 11 için Ultra Hafif, Modern DDC/CI Monitör Parlaklık Yöneticisi & OSD**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=flat&logo=windows&logoColor=white)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Lisans: MIT](https://img.shields.io/badge/Lisans-MIT-F5A623?style=flat&logo=open-source-initiative&logoColor=white)](LICENSE)
[![Boşta RAM](https://img.shields.io/badge/Bo%C5%9Fta%20RAM-~8%20MB-2ea44f?style=flat)](https://github.com/dogueu/Lumina)

[ **English Documentation** ](README.md)

</div>

---

## Özellikler

- **Donanımsal Monitör Desteği (DDC/CI):** Harici monitörlerinizin gerçek panel parlaklığını doğrudan donanım düzeyinde (DDC/CI protokolü ile) kontrol eder; ekrana sahte siyah katman (overlay) koymaz.
- **Windows 11 Fluent Tasarım:** Windows 11'in yerel ses/parlaklık paneline birebir sadık, akıcı animasyonlu ve yuvarlatılmış köşelere sahip modern OSD (ekran göstergesi).
- **Sıfır Arka Plan Yükü (Ultra-Lightweight):** Arka planda CPU tüketimi %0'dır; sürekli döngüde çalışan timer veya gereksiz tarama mekanizması bulunmaz. Boşta yalnızca **~8 MB RAM** tüketir.
- **Özelleştirilebilir Global Kısayollar:** Oyun oynarken veya tam ekrandayken bile parlaklığı anında artırıp azaltabilmeniz için global kısayol tuşları.
- **Tepsi Tekerleği Kontrolü:** Sağ alttaki sistem tepsisi simgesi üzerine gelip fare tekerleğini yukarı/aşağı kaydırarak parlaklığı zahmetsizce ayarlayabilme.
- **Monitör İsimlendirme:** Algılanan monitörlerinize kolay ayırt edebilmeniz için özel isimler atayabilme (örn. *Ana Ekran*, *Oyun Monitörü*).
- **Hafıza ve Otomatik Başlatma:** Her monitörün son parlaklık seviyesini hatırlar; Windows başladığında otomatik olarak arka planda sessizce başlatılabilir.

---

## Kurulum ve Çalıştırma

### 1. Hazır Taşınabilir Sürüm (Portable Single-File - Önerilen)
Hiçbir kurulum veya ek paket gerektirmez:
1. **Releases** bölümünden en güncel `Lumina.exe` dosyasını indirin.
2. Dosyayı dilediğiniz bir klasöre (örn. *Belgelerim* veya *C:\Programlar\Lumina*) koyun.
3. Çift tıklayarak çalıştırın; Lumina sağ alt sistem tepsisine yerleşecektir!

### 2. Kaynak Koddan Derleme (Geliştiriciler İçin)
Gereksinimler:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 (Sürüm 1903+) veya Windows 11

```bash
# Depoyu klonlayın
git clone https://github.com/dogueu/Lumina.git
cd Lumina

# Geliştirici modunda çalıştırın
dotnet run

# Tek dosya bağımsız (portable) exe üretin
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o Publish
```

---

## Kontroller ve Varsayılan Kısayollar

| İşlem | Varsayılan | Açıklama |
|---|---|---|
| **Parlaklığı Artır** | `Ctrl + Alt + ↑` | Parlaklığı belirlenen adım oranında artırır ve OSD'yi gösterir. |
| **Parlaklığı Azalt** | `Ctrl + Alt + ↓` | Parlaklığı belirlenen adım oranında azaltır ve OSD'yi gösterir. |
| **Hızlı Ayar Menüsü** | Tepsi Sol Tık | Windows 11 tarzı monitör kaydırıcı panelini açar. |
| **Tekerlekle Ayar** | Tepsi Üzerinde Tekerlek | Tepsi ikonu üzerindeyken parlaklığı hızlıca değiştirir. |
| **Seçenekler** | Tepsi Sağ Tık | Ayarlar, monitörleri yenile ve çıkış menüsü. |

> *Not: Kısayol tuşları Ayarlar penceresinden istenen herhangi bir tuşa (örneğin `F9`/`F10` veya `Page Up`/`Page Down`) kolayca değiştirilebilir.*

---

## Mimari ve Kullanılan Teknolojiler

- **Framework:** [.NET 8 Windows Presentation Foundation (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- **UI & Tasarım:** [WPF-UI (Fluent 2 Design)](https://github.com/lepoco/wpfui)
- **Monitör API:** Windows Physical Monitor API (`dxva2.dll` / DDC/CI)
- **Native Entegrasyon:** Win32 P/Invoke (`dwmapi.dll`, `user32.dll`, `shell32.dll`)

---

## Lisans

Bu proje [MIT Lisansı](LICENSE) kapsamında açık kaynak olarak yayınlanmıştır.
