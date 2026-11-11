using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Fujify.Common
{
    public static class NativeWindowHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;
        private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
        private const int DWMWCP_ROUND = 2;

        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_CAPTION = 0x00C00000;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        private static extern int DwmSetWindowAttributeUint(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public static void ConfigureDialogWindow(Window window)
        {
            window.Classes.Add("no-caption");

            void HideVisualDecorations()
            {
                try
                {
                    foreach (var visual in window.GetVisualDescendants())
                    {
                        if (visual is Control ctrl)
                        {
                            var role = Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(ctrl);
                            if (role != Avalonia.Input.WindowDecorationsElementRole.None ||
                                ctrl.Name == "PART_CloseButton" ||
                                ctrl.Name == "PART_MinimizeButton" ||
                                ctrl.Name == "PART_MaximizeButton" ||
                                ctrl.Name == "PART_FullScreenButton" ||
                                ctrl.Name == "PART_TitleBar")
                            {
                                ctrl.IsVisible = false;
                                ctrl.Width = 0;
                                ctrl.MaxWidth = 0;
                                ctrl.Height = 0;
                                ctrl.MaxHeight = 0;
                                ctrl.Opacity = 0;
                            }
                        }
                    }
                }
                catch
                {
                    // Visual tree access safe fallback
                }
            }

            void Apply()
            {
                HideVisualDecorations();

                if (!OperatingSystem.IsWindows()) return;

                var handle = window.TryGetPlatformHandle()?.Handle;
                if (!handle.HasValue || handle.Value == IntPtr.Zero) return;

                try
                {
                    // Strip WS_SYSMENU / WS_CAPTION so native Windows controls never appear
                    long style = GetWindowLongPtr(handle.Value, GWL_STYLE).ToInt64();
                    long newStyle = style & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_CAPTION);
                    if (newStyle != style)
                    {
                        SetWindowLongPtr(handle.Value, GWL_STYLE, new IntPtr(newStyle));
                        SetWindowPos(handle.Value, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                    }

                    // Native rounded corners (Windows 11)
                    int cornerPreference = DWMWCP_ROUND;
                    DwmSetWindowAttribute(handle.Value, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                    // Native DWM drop shadow
                    var margins = new MARGINS { cxLeftWidth = 1, cxRightWidth = 1, cyTopHeight = 1, cyBottomHeight = 1 };
                    DwmExtendFrameIntoClientArea(handle.Value, ref margins);

                    // Native dark/light titlebar & border coloring
                    int darkMode = (window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light) ? 0 : 1;
                    DwmSetWindowAttribute(handle.Value, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                    uint captionColor = DWMWA_COLOR_NONE;
                    DwmSetWindowAttributeUint(handle.Value, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(uint));

                    uint borderColor = DWMWA_COLOR_DEFAULT;
                    DwmSetWindowAttributeUint(handle.Value, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
                }
                catch
                {
                    // Ignored on unsupported Windows configurations
                }
            }

            window.Opened += (s, e) => Apply();
            window.ActualThemeVariantChanged += (s, e) => Apply();
        }
    }
}
