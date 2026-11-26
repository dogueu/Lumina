using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Lumina.Models;
using Lumina.Services;

namespace Lumina;

/// <summary>
/// <see cref="MainWindow"/> ayar görünümü, monitör listesi yönetimi ve sürükle-bırak sıralama bölümü.
/// </summary>
public partial class MainWindow
{
    private const int BlurDefaultMaxOpacity = 35;
    private const double DragSwapHysteresisPx = 4.0;
    private const double DragEdgeClampPx = 4.0;

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            var s = _settings.Current;

            var requested = ThemeService.Parse(s.Appearance);
            AppearanceGlass.IsChecked = requested == AppAppearance.Glass;
            AppearanceSystem.IsChecked = requested == AppAppearance.System;
            AppearanceSolid.IsChecked = requested == AppAppearance.Solid;

            OpacityBlock.Visibility = requested == AppAppearance.Glass ? Visibility.Visible : Visibility.Collapsed;
            BlurBlock.Visibility = OpacityBlock.Visibility;

            BlurSwitch.IsChecked = s.GlassBlur && ThemeService.TransparencyEffects;
            BlurSwitch.IsEnabled = ThemeService.TransparencyEffects;
            BlurTitle.Opacity = ThemeService.TransparencyEffects ? 1 : 0.5;
            BlurCaption.Text = ThemeService.TransparencyEffects
                ? "Panelin arkasındaki içerik yumuşakça bulanıklaşır"
                : "Windows ayarlarında “Saydamlık efektleri” açık olmalıdır";
            ColorsButton.Visibility = ThemeService.TransparencyEffects ? Visibility.Collapsed : Visibility.Visible;

            OpacitySlider.Value = s.GlassOpacity;
            OpacityValue.Text = s.GlassOpacity.ToString();

            AppearanceNote.Text = requested switch
            {
                AppAppearance.System => "Klasik Windows 11 Hızlı Ayarlar görünümü ve kaydırıcı stili kullanılır.",
                AppAppearance.Solid => "Arka plan şeffaflığı olmadan sade ve opak görünüm kullanılır.",
                _ when ThemeService.Blur => "Bulanıklaştırma açıkken opaklığı düşürerek arka planı daha belirgin görebilirsiniz.",
                _ => "Bulanıklaştırma olmadan cam katmanı arka planı karartır/aydınlatır; okunabilirlik için opaklığı artırabilirsiniz.",
            };

            StepSlider.Value = s.StepPercent;
            StepValue.Text = s.StepPercent.ToString();
            IncreaseBox.Text = s.IncreaseHotkey.ToString();
            DecreaseBox.Text = s.DecreaseHotkey.ToString();

            string? hotkeyWarn = _getHotkeyWarning();
            HotkeyErrorText.Text = hotkeyWarn ?? "";
            HotkeyErrorText.Visibility = string.IsNullOrEmpty(hotkeyWarn) ? Visibility.Collapsed : Visibility.Visible;

            StartupSwitch.IsChecked = s.RunAtStartup;
            OsdSwitch.IsChecked = s.ShowOsd;

            PopulateMonitorSettings();
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void PopulateMonitorSettings()
    {
        SettingsMonitorsPanel.Children.Clear();
        var monitors = _controller.Monitors;

        if (monitors.Count == 0)
        {
            SettingsNoMonitorsText.Visibility = Visibility.Visible;
            return;
        }

        SettingsNoMonitorsText.Visibility = Visibility.Collapsed;

        var s = _settings.Current;
        var allIds = monitors.Select(m => m.Id).ToList();
        bool canReorder = monitors.Count > 1;

        for (int i = 0; i < monitors.Count; i++)
        {
            if (i > 0)
            {
                SettingsMonitorsPanel.Children.Add(new Border
                {
                    Style = (Style)FindResource("Rule")
                });
            }

            var m = monitors[i];
            var rowTransform = new TranslateTransform();
            var rowBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                RenderTransform = rowTransform,
                Tag = rowTransform,
            };

            var rowDock = new DockPanel();

            Border? gripBorder = null;
            System.Windows.Shapes.Path? gripPath = null;
            if (canReorder)
            {
                gripPath = new System.Windows.Shapes.Path
                {
                    Data = Icons.Grip,
                    Width = 20,
                    Height = 20,
                };
                gripPath.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Text.Tertiary");

                gripBorder = new Border
                {
                    Width = 22,
                    CornerRadius = new CornerRadius(4),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.SizeNS,
                    Margin = new Thickness(6, 8, 6, 8),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    ToolTip = "Sıralamak için sürükleyin",
                    Child = new Viewbox
                    {
                        Width = 14,
                        Height = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = gripPath,
                    },
                };
                Motion.SetHover(gripBorder, (Brush)FindResource("Tile.Fill"));
                DockPanel.SetDock(gripBorder, Dock.Left);
                rowDock.Children.Add(gripBorder);
            }

            var container = new StackPanel
            {
                Margin = canReorder ? new Thickness(0, 12, 14, 12) : new Thickness(14, 12, 14, 12),
            };

            var header = new DockPanel();
            bool isExcluded = BrightnessController.IsMonitorExcluded(m.Id, s.ExcludedHotkeyMonitors, allIds);
            var statusText = new TextBlock
            {
                Style = (Style)FindResource("Caption"),
                Text = isExcluded ? "Kısayol devre dışı" : "Kısayol aktif",
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var toggle = new ToggleButton
            {
                Style = (Style)FindResource("Switch"),
                IsChecked = !isExcluded,
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Klavye kısayolları ve tepsi tekerleğine dahil et",
            };
            DockPanel.SetDock(toggle, Dock.Right);
            header.Children.Add(toggle);

            toggle.Click += (_, _) =>
            {
                if (_loadingSettings) return;
                bool enabled = toggle.IsChecked == true;
                var currentIds = _controller.Monitors.Select(x => x.Id).ToList();
                if (enabled)
                {
                    s.ExcludedHotkeyMonitors.Remove(m.Id);
                    if (BrightnessController.HasUniquePnp(m.Id, currentIds, out string pnp))
                    {
                        s.ExcludedHotkeyMonitors.RemoveWhere(existing =>
                            string.Equals(BrightnessController.ExtractPnpCode(existing), pnp, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    s.ExcludedHotkeyMonitors.Add(m.Id);
                }

                statusText.Text = enabled ? "Kısayol aktif" : "Kısayol devre dışı";
                _controller.SetExcludedHotkeyMonitors(s.ExcludedHotkeyMonitors);
                _settings.Save(s);
                RenderMainState();
            };

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(m.RawName) ? m.Name : m.RawName,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textStack.Children.Add(statusText);
            header.Children.Add(textStack);
            container.Children.Add(header);

            string existingCustom = s.MonitorCustomNames.TryGetValue(m.Id, out var custom) ? custom : string.Empty;
            var nameBox = new TextBox
            {
                Style = (Style)FindResource("LuminaTextBox"),
                Margin = new Thickness(0, 8, 0, 0),
                Text = existingCustom,
            };
            Motion.SetPlaceholder(nameBox, "Özel isim (boşsa donanım adı)");
            nameBox.TextChanged += (_, _) =>
            {
                if (_loadingSettings) return;
                string trimmed = nameBox.Text.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    s.MonitorCustomNames.Remove(m.Id);
                else
                    s.MonitorCustomNames[m.Id] = trimmed;

                _controller.SetCustomNames(s.MonitorCustomNames);
                _settings.ScheduleSave();
            };

            container.Children.Add(nameBox);
            rowDock.Children.Add(container);
            rowBorder.Child = rowDock;

            if (gripBorder is not null && gripPath is not null)
                AttachMonitorDragReorder(rowBorder, gripBorder, gripPath, rowTransform, s);

            SettingsMonitorsPanel.Children.Add(rowBorder);
        }
    }

    private void AttachMonitorDragReorder(
        Border rowBorder,
        Border gripBorder,
        System.Windows.Shapes.Path gripPath,
        TranslateTransform rowTransform,
        AppSettings settings)
    {
        bool dragging = false;
        double startMouseY = 0;
        double slotOffsetY = 0;
        int dragIdx = -1;
        int settleGen = 0;

        gripBorder.MouseEnter += (_, _) =>
        {
            if (!dragging)
                gripPath.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Text.Secondary");
        };

        gripBorder.MouseLeave += (_, _) =>
        {
            if (!dragging)
                gripPath.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Text.Tertiary");
        };

        gripBorder.MouseLeftButtonDown += (_, e) =>
        {
            if (_loadingSettings || _controller.Monitors.Count <= 1) return;

            int childIdx = SettingsMonitorsPanel.Children.IndexOf(rowBorder);
            if (childIdx < 0) return;

            e.Handled = true;
            settleGen++;
            rowTransform.BeginAnimation(TranslateTransform.YProperty, null);
            rowTransform.Y = 0;

            if (!gripBorder.CaptureMouse()) return;

            dragging = true;
            _reorderingMonitors = true;
            dragIdx = childIdx / 2;
            startMouseY = e.GetPosition(SettingsMonitorsPanel).Y;
            slotOffsetY = 0;

            Panel.SetZIndex(rowBorder, 20);
            rowBorder.Opacity = 0.96;
            rowBorder.SetResourceReference(Border.BackgroundProperty, "Card.FillHover");
            rowBorder.SetResourceReference(Border.BorderBrushProperty, "Level.Track");
            gripPath.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Text.Primary");
        };

        gripBorder.MouseMove += (_, e) =>
        {
            if (!dragging) return;

            double pointerY = e.GetPosition(SettingsMonitorsPanel).Y;
            double desiredY = (pointerY - startMouseY) - slotOffsetY;
            int maxIdx = _controller.Monitors.Count - 1;

            while (dragIdx < maxIdx)
            {
                int baseChild = 2 * dragIdx;
                var rule = (FrameworkElement)SettingsMonitorsPanel.Children[baseChild + 1];
                var neighbor = (Border)SettingsMonitorsPanel.Children[baseChild + 2];
                double dragStep = neighbor.ActualHeight + rule.ActualHeight;
                if (dragStep <= 1 || desiredY <= dragStep * 0.5 + DragSwapHysteresisPx) break;

                double neighborShift = rowBorder.ActualHeight + rule.ActualHeight;
                _controller.MoveMonitor(dragIdx, dragIdx + 1);

                // Sürüklenen öğeyi görsel ağaçtan çıkarmadan komşu ve ayırıcıyı taşı (MouseCapture korunur).
                SettingsMonitorsPanel.Children.RemoveAt(baseChild + 2);
                SettingsMonitorsPanel.Children.RemoveAt(baseChild + 1);
                SettingsMonitorsPanel.Children.Insert(baseChild, rule);
                SettingsMonitorsPanel.Children.Insert(baseChild, neighbor);

                AnimateNeighborOffset(neighbor, +neighborShift);

                dragIdx++;
                slotOffsetY += dragStep;
                desiredY = (pointerY - startMouseY) - slotOffsetY;
            }

            while (dragIdx > 0)
            {
                int baseChild = 2 * dragIdx;
                var neighbor = (Border)SettingsMonitorsPanel.Children[baseChild - 2];
                var rule = (FrameworkElement)SettingsMonitorsPanel.Children[baseChild - 1];
                double dragStep = neighbor.ActualHeight + rule.ActualHeight;
                if (dragStep <= 1 || desiredY >= -(dragStep * 0.5 + DragSwapHysteresisPx)) break;

                double neighborShift = rowBorder.ActualHeight + rule.ActualHeight;
                _controller.MoveMonitor(dragIdx, dragIdx - 1);

                SettingsMonitorsPanel.Children.RemoveAt(baseChild - 1);
                SettingsMonitorsPanel.Children.RemoveAt(baseChild - 2);
                SettingsMonitorsPanel.Children.Insert(baseChild - 1, rule);
                SettingsMonitorsPanel.Children.Insert(baseChild, neighbor);

                AnimateNeighborOffset(neighbor, -neighborShift);

                dragIdx--;
                slotOffsetY -= dragStep;
                desiredY = (pointerY - startMouseY) - slotOffsetY;
            }

            if (dragIdx == 0 && desiredY < -DragEdgeClampPx) desiredY = -DragEdgeClampPx;
            if (dragIdx == maxIdx && desiredY > DragEdgeClampPx) desiredY = DragEdgeClampPx;
            rowTransform.Y = desiredY;
        };

        void FinishDrag()
        {
            if (!dragging) return;
            dragging = false;
            _reorderingMonitors = false;

            if (gripBorder.IsMouseCaptured)
                gripBorder.ReleaseMouseCapture();

            gripPath.SetResourceReference(
                System.Windows.Shapes.Shape.FillProperty,
                gripBorder.IsMouseOver ? "Text.Secondary" : "Text.Tertiary");

            settings.MonitorOrder = _controller.Monitors.Select(x => x.Id).ToList();
            _settings.Save(settings);

            int gen = ++settleGen;
            Motion.Run(rowTransform, TranslateTransform.YProperty, null, 0, 160, Motion.Out, () =>
            {
                if (gen != settleGen) return;
                Panel.SetZIndex(rowBorder, 0);
                rowBorder.Opacity = 1;
                rowBorder.Background = Brushes.Transparent;
                rowBorder.BorderBrush = Brushes.Transparent;
            });
        }

        gripBorder.MouseLeftButtonUp += (_, _) => FinishDrag();
        gripBorder.LostMouseCapture += (_, _) => FinishDrag();
    }

    private static void AnimateNeighborOffset(Border neighbor, double deltaY)
    {
        if (neighbor.Tag is not TranslateTransform transform) return;

        double currentY = transform.Y;
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        Motion.Run(transform, TranslateTransform.YProperty, currentY + deltaY, 0, 180, Motion.Out);
    }

    private void WireSettings()
    {
        void ApplyAndSave(Action mutate, bool immediate = true)
        {
            if (_loadingSettings) return;
            mutate();
            if (immediate) _settings.Save(_settings.Current);
            else _settings.ScheduleSave();
            LoadSettings();
        }

        void SetAppearance(AppAppearance a) => ApplyAndSave(() =>
        {
            _settings.Current.Appearance = ThemeService.ToSetting(a);
            ThemeService.Apply(a, _settings.Current.GlassOpacity, _settings.Current.GlassBlur);
        });

        AppearanceGlass.Checked += (_, _) => SetAppearance(AppAppearance.Glass);
        AppearanceSystem.Checked += (_, _) => SetAppearance(AppAppearance.System);
        AppearanceSolid.Checked += (_, _) => SetAppearance(AppAppearance.Solid);

        BlurSwitch.Click += (_, _) => ApplyAndSave(() =>
        {
            _settings.Current.GlassBlur = BlurSwitch.IsChecked == true;
            if (_settings.Current.GlassBlur)
                _settings.Current.GlassOpacity = Math.Min(_settings.Current.GlassOpacity, BlurDefaultMaxOpacity);
            ThemeService.Apply(ThemeService.Parse(_settings.Current.Appearance), _settings.Current.GlassOpacity, _settings.Current.GlassBlur);
        });

        OpacitySlider.ValueChanged += (_, e) => ApplyAndSave(() =>
        {
            _settings.Current.GlassOpacity = (int)Math.Round(e.NewValue);
            ThemeService.Apply(ThemeService.Parse(_settings.Current.Appearance), _settings.Current.GlassOpacity, _settings.Current.GlassBlur);
        }, immediate: false);

        ColorsButton.Click += (_, _) =>
        {
            try
            {
                using var _ = Process.Start(new ProcessStartInfo("ms-settings:colors") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppDiagnostics.Trace($"Ayarlar açılamadı: {ex.Message}");
            }
        };

        StepSlider.ValueChanged += (_, e) => ApplyAndSave(() =>
        {
            _settings.Current.StepPercent = (int)Math.Round(e.NewValue);
        }, immediate: false);

        StartupSwitch.Click += (_, _) =>
        {
            if (_loadingSettings) return;
            bool desired = StartupSwitch.IsChecked == true;
            string? err = _updateStartup(desired);
            if (err is not null)
            {
                HotkeyErrorText.Text = err;
                HotkeyErrorText.Visibility = Visibility.Visible;
            }
            LoadSettings();
        };

        OsdSwitch.Click += (_, _) => ApplyAndSave(() =>
        {
            _settings.Current.ShowOsd = OsdSwitch.IsChecked == true;
        });

        ResetButton.Click += (_, _) =>
        {
            var cur = _settings.Current;
            var defaults = new AppSettings();

            string? err = _updateHotkeys(defaults.IncreaseHotkey, defaults.DecreaseHotkey);
            if (err is null)
            {
                cur.StepPercent = defaults.StepPercent;
                cur.ShowOsd = defaults.ShowOsd;
                cur.IncreaseHotkey = defaults.IncreaseHotkey;
                cur.DecreaseHotkey = defaults.DecreaseHotkey;
                cur.ExcludedHotkeyMonitors.Clear();
                cur.Appearance = defaults.Appearance;
                cur.GlassOpacity = defaults.GlassOpacity;
                cur.GlassBlur = defaults.GlassBlur;

                _settings.Save(cur);
                _controller.SetExcludedHotkeyMonitors(cur.ExcludedHotkeyMonitors);
                ThemeService.Apply(ThemeService.Parse(cur.Appearance), cur.GlassOpacity, cur.GlassBlur);
                RenderMainState();
            }

            LoadSettings();
            if (err is not null)
            {
                HotkeyErrorText.Text = err;
                HotkeyErrorText.Visibility = Visibility.Visible;
            }
        };
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        var actual = e.Key == Key.System ? e.SystemKey : e.Key;
        if (actual == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;

        e.Handled = true;

        var gesture = HotkeyGesture.FromKeyEvent(e);
        if (gesture is null) return;

        var s = _settings.Current;
        bool isIncrease = (string?)box.Tag == "Increase";
        var nextInc = isIncrease ? gesture : s.IncreaseHotkey;
        var nextDec = isIncrease ? s.DecreaseHotkey : gesture;

        if (nextInc == nextDec)
        {
            HotkeyErrorText.Text = "Artırma ve azaltma kısayolları farklı olmalıdır.";
            HotkeyErrorText.Visibility = Visibility.Visible;
            return;
        }

        string? err = _updateHotkeys(nextInc, nextDec);
        if (err is not null)
        {
            HotkeyErrorText.Text = err;
            HotkeyErrorText.Visibility = Visibility.Visible;
            return;
        }

        s.IncreaseHotkey = nextInc;
        s.DecreaseHotkey = nextDec;
        _settings.Save(s);

        box.Text = gesture.ToString();
        HotkeyErrorText.Visibility = Visibility.Collapsed;
    }
}
