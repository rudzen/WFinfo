using System.Drawing;
using System.Windows.Forms;
using Serilog;

namespace WFInfo.Services.Screenshot;

public class ImageScreenshotService : IScreenshotService
{
    private static readonly ILogger Logger = Log.Logger.ForContext<ImageScreenshotService>();

    public async Task<List<Bitmap>> CaptureScreenshot()
    {
        // Using WinForms for the openFileDialog because it's simpler and much easier
        using OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        openFileDialog.Filter = "image files (*.png)|*.png|All files (*.*)|*.*";
        openFileDialog.FilterIndex = 2;
        openFileDialog.RestoreDirectory = true;
        openFileDialog.Multiselect = true;

        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var tasks = openFileDialog.FileNames.Select(file => Task.Run(() => new Bitmap(file)));
                var images = await Task.WhenAll(tasks);
                return images.ToList();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load image");
                Main.StatusUpdate("Failed to load image", 1);

                return [];
            }
        }

        Main.StatusUpdate("Failed to select image", 1);
        return [];
    }
}