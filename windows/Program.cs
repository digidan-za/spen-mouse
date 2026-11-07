// spen-mouse receiver: a tray app. Receiver.cs is the mouse part, MainForm.cs the window.
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var single = new Mutex(true, "spen-mouse-receiver", out var first);
        if (!first) { MessageBox.Show("spen-mouse is already running. Look for it in the tray.", "spen-mouse"); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startMinimized: args.Contains("--minimized")));
    }
}
