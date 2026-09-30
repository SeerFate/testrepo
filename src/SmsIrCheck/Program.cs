using System.Runtime.InteropServices;

namespace SmsIrCheck;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (ConsoleApp.WantsConsole(args))
            return ConsoleApp.Run(args);

        ReleaseLaunchConsole();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "SMS.ir connectivity check", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
        return 0;
    }

    private static void ReleaseLaunchConsole()
    {
        var ids = new uint[16];
        var count = GetConsoleProcessList(ids, (uint)ids.Length);
        if (count <= 1)
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
                ShowWindow(handle, 0);
            return;
        }

        // Started from an existing terminal. Detach so that terminal is not held open.
        FreeConsole();
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processIds, uint processCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
