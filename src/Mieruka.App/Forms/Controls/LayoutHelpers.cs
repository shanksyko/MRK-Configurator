using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms.Controls;

internal static class LayoutHelpers
{
    public static void ApplyStandardLayout(Control control)
    {
        if (control is null)
        {
            return;
        }

        if (control is ContainerControl container)
        {
            container.AutoScaleMode = AutoScaleMode.Dpi;
            container.AutoScaleDimensions = new SizeF(96F, 96F);
        }

        if (control is not Form)
        {
            control.Dock = DockStyle.Fill;
        }

        control.Margin = new Padding(8);
    }

    public static TableLayoutPanel CreateStandardTableLayout(int columnCount = 1)
    {
        if (columnCount <= 0)
        {
            columnCount = 1;
        }

        var table = new TableLayoutPanel
        {
            ColumnCount = columnCount,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = true,
            Padding = new Padding(8),
            Margin = new Padding(0),
        };

        if (columnCount == 1)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        }
        else
        {
            var percent = 100F / columnCount;
            for (var i = 0; i < columnCount; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent));
            }
        }

        return table;
    }

    public static Panel CreateMonitorCard(
        MonitorInfo monitor,
        EventHandler selecionarHandler,
        EventHandler pararHandler,
        out PictureBox previewBox)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(selecionarHandler);
        ArgumentNullException.ThrowIfNull(pararHandler);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8),
            Padding = new Padding(8),
            BorderStyle = BorderStyle.FixedSingle,
        };

        var baseFont = SystemFonts.CaptionFont ?? Control.DefaultFont;
        if (baseFont is null)
        {
            baseFont = new Font(FontFamily.GenericSansSerif, 9F, FontStyle.Regular);
        }

        var desiredStyle = baseFont.Style | FontStyle.Bold;
        Font titleFont;
        if (desiredStyle == baseFont.Style)
        {
            titleFont = new Font(baseFont, baseFont.Style);
        }
        else
        {
            titleFont = new Font(baseFont, desiredStyle);
        }

        var title = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = titleFont,
            Height = 24,
            Margin = new Padding(0, 0, 0, 8),
            Text = GetMonitorDisplayName(monitor),
        };

        previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = SystemColors.ControlDark,
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };

        var btnSelecionar = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Selecionar",
            Margin = new Padding(0, 0, 8, 0),
        };

        var btnParar = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Parar",
            Margin = new Padding(0),
        };

        btnSelecionar.Click += selecionarHandler;
        btnParar.Click += pararHandler;

        footer.Controls.Add(btnSelecionar);
        footer.Controls.Add(btnParar);

        card.Controls.Add(previewBox);
        card.Controls.Add(footer);
        card.Controls.Add(title);

        ApplyClickForwarding(card, previewBox, title, selecionarHandler);

        return card;
    }

    public static string GetMonitorDisplayName(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var ordinal = monitor.Key?.DisplayIndex >= 0
            ? monitor.Key.DisplayIndex + 1
            : 0;

        var friendly = !string.IsNullOrWhiteSpace(monitor.Name)
            ? monitor.Name
            : (!string.IsNullOrWhiteSpace(monitor.DeviceName) ? monitor.DeviceName : "Monitor");

        var resolution = monitor.Width > 0 && monitor.Height > 0
            ? $"{monitor.Width}x{monitor.Height}"
            : "?x?";

        var refreshRate = TryGetRefreshRate(monitor.DeviceName);
        var hzText = refreshRate > 0 ? $"{refreshRate}Hz" : "?Hz";

        var ordinalText = ordinal > 0 ? $"#{ordinal}" : "#?";

        return $"{ordinalText}  {friendly}  {resolution} @ {hzText}";
    }

    private static void ApplyClickForwarding(Control card, PictureBox previewBox, Label title, EventHandler selecionarHandler)
    {
        card.Click += selecionarHandler;
        previewBox.Click += selecionarHandler;
        title.Click += selecionarHandler;
    }

    private static int TryGetRefreshRate(string? deviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return 0;
        }

        try
        {
            var mode = new DEVMODE
            {
                dmSize = (short)Marshal.SizeOf<DEVMODE>(),
            };

            return EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode)
                ? (int)mode.dmDisplayFrequency
                : 0;
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
