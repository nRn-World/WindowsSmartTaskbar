using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WindowsSmartTaskbar
{
    public class ProgramItem
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? Arguments { get; set; }
        public string Category { get; set; } = "All programs";
        public Icon? Icon { get; set; }
        public DateTime AddedDate { get; set; } = DateTime.Now;

        public ProgramItem() { }

        public ProgramItem(string name, string filePath, string? arguments = null)
        {
            Name = name;
            FilePath = filePath;
            Arguments = arguments;
            LoadIcon();
        }

        private void LoadIcon()
        {
            try
            {
                string path = FilePath;
                if (Path.GetExtension(FilePath).ToLower() == ".lnk")
                {
                    string target = GetShortcutTarget(FilePath);
                    if (File.Exists(target)) path = target;
                }

                if (File.Exists(path))
                {
                    SHFILEINFO shfi = new SHFILEINFO();
                    IntPtr hIcon = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_LARGEICON);
                    if (hIcon != IntPtr.Zero)
                    {
                        Icon = (Icon)Icon.FromHandle(shfi.hIcon).Clone();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(ProgramItem)}::{nameof(LoadIcon)}] {ex}");
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_LARGEICON = 0x0;

        private string GetShortcutTarget(string shortcutPath)
        {
            try
            {
                string? target = ReadShortcutTarget(shortcutPath);
                return string.IsNullOrWhiteSpace(target) ? shortcutPath : target;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(ProgramItem)}::{nameof(GetShortcutTarget)}] {ex}");
                return shortcutPath;
            }
        }

        private string? ReadShortcutTarget(string shortcutPath)
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            object? shell = null;
            object? shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return null;

                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                if (shortcut == null) return null;

                string? targetPath = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                return string.IsNullOrWhiteSpace(targetPath) ? null : targetPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(ProgramItem)}::{nameof(ReadShortcutTarget)}] {ex}");
                return null;
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        public void Start()
        {
            try
            {
                string targetPath = FilePath;
                string targetArgs = Arguments ?? string.Empty;
                
                if (Path.GetExtension(FilePath).ToLower() == ".lnk")
                {
                    try
                    {
                        var startInfo = new ProcessStartInfo { FileName = FilePath, UseShellExecute = true };
                        Process.Start(startInfo);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[{nameof(ProgramItem)}::{nameof(Start)}] Failed launching shortcut directly: {ex}");
                        targetPath = GetShortcutTarget(FilePath);
                    }
                }

                if (!string.IsNullOrEmpty(targetPath))
                {
                    var startInfo = new ProcessStartInfo { FileName = targetPath, Arguments = targetArgs, UseShellExecute = true };
                    Process.Start(startInfo);
                }
                else
                {
                    MessageBox.Show($"Kunde inte hitta målfilen: {targetPath}", "Fel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kunde inte starta programmet: {ex.Message}\nSökväg: {FilePath}", "Fel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
