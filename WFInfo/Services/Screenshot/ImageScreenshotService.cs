using System.Drawing;
using System.Windows.Forms;
using DotNext.Collections.Generic;
using Mediator;
using Serilog;
using WFInfo.Domain;

namespace WFInfo.Services.Screenshot;

public sealed class ImageScreenshotService(IPublisher mediator) : IScreenshotService
{
    private static readonly ILogger Logger = Log.Logger.ForContext<ImageScreenshotService>();

    public async Task<IReadOnlyList<Bitmap>> CaptureScreenshot()
    {
        // Using WinForms for the openFileDialog because it's simpler and much easier
        using var openFileDialog = new OpenFileDialog();
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
                await mediator.Publish(new UpdateStatus("Failed to load image", StatusSeverity.Error));
                return [];
            }
        }

        await mediator.Publish(new UpdateStatus("Failed to select image", StatusSeverity.Error));
        return [];
    }
}
