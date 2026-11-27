using System;
using System.Windows.Forms;

namespace BlackOut
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            var launcher = new LauncherForm();
            if (args.Length > 1 && args[0] == LauncherForm.ElevatedBlackoutArgument)
                launcher.Shown += (_, _) => launcher.ResumeElevatedBlackout(args[1..]);
            Application.Run(launcher);
        }
    }
}
