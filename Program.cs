using System;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;

namespace WindowsSmartTaskbar
{
    internal static class Program
    {
        static Mutex? mutex = null;
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        public const int HWND_BROADCAST = 0xffff;
        public static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOWME_SMARTTASKBAR");
        [DllImport("user32.dll")] public static extern int RegisterWindowMessage(string message);

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            mutex = new Mutex(true, "WindowsSmartTaskbar_SingleInstance_App", out createdNew);
            if (!createdNew)
            {
                SendMessage((IntPtr)HWND_BROADCAST, WM_SHOWME, 0, 0);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool autostart = false;
            foreach (var arg in args)
            {
                if (arg.Trim('"', '\'').Equals("-autostart", StringComparison.OrdinalIgnoreCase)) autostart = true;
            }
            Application.Run(new MainForm(autostart));
        }
    }
}
