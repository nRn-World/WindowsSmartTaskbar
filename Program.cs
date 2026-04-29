using System;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace WindowsSmartTaskbar
{
    internal static class Program
    {
        static Mutex? mutex = null;
        private static string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsSmartTaskbar");
        private static string LastUpdateCheckFile => Path.Combine(AppDataFolder, "last_update_check.txt");
        
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        public const int HWND_BROADCAST = 0xffff;
        public static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOWME_SMARTTASKBAR");
        [DllImport("user32.dll")] public static extern int RegisterWindowMessage(string message);

        [STAThread]
        static void Main(string[] args)
        {
            VelopackApp.Build().Run();

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

            // Check for updates once per week
            CheckForUpdatesAsync().ConfigureAwait(false);

            Application.Run(new MainForm(autostart));
        }

        static async Task CheckForUpdatesAsync()
        {
            try {
                if (!Directory.Exists(AppDataFolder)) Directory.CreateDirectory(AppDataFolder);
                
                if (File.Exists(LastUpdateCheckFile)) {
                    if (DateTime.TryParse(File.ReadAllText(LastUpdateCheckFile), out DateTime lastCheck)) {
                        if ((DateTime.Now - lastCheck).TotalDays < 7) return;
                    }
                }

                File.WriteAllText(LastUpdateCheckFile, DateTime.Now.ToString());

                var source = new GithubSource("https://github.com/nRn-World/WindowsSmartTaskbar", string.Empty, false);
                var manager = new UpdateManager(source);
                
                if (manager.IsInstalled) {
                    var newVersion = await manager.CheckForUpdatesAsync();
                    if (newVersion != null) {
                        await manager.DownloadUpdatesAsync(newVersion);
                    }
                }
            } catch { }
        }
    }
}
