using LibVLCSharp.Shared;

namespace MagerCam.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception error)
        {
            MessageBox.Show("Не удалось запустить MagerCam. Распакуйте весь ZIP, включая папку libvlc.\n\n" + error.Message,
                "MagerCam", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
