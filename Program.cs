using System;
using System.Windows.Forms;

namespace WindowsSmartTaskbar
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool autostart = args.Length > 0 && args[0] == "-autostart";
            Application.Run(new MainForm(autostart));
        }
    }
}
