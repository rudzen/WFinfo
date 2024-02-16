using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using WFInfo.Domain;
using WFInfo.Extensions;
using WFInfo.Services.OpticalCharacterRecognition;
using WFInfo.Services.WindowInfo;

namespace WFInfo;

/// <summary>
/// Interaction logic for SnapItOverlay.xaml
/// Marching ant logic by: https://www.codeproject.com/Articles/27816/Marching-Ants-Selection
/// </summary>
public partial class SnapItOverlay : Window
{
    private static readonly ILogger Logger = Log.Logger.ForContext<SnapItOverlay>();

    private System.Windows.Point _startDrag;
    private System.Drawing.Point _topLeft;
    private readonly IWindowInfoService _windowInfoService;

    public SnapItOverlay(IWindowInfoService windowInfoService)
    {
        _windowInfoService = windowInfoService;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Left = 0;
        Top = 0;
        InitializeComponent();
        MouseDown += canvas_MouseDown;
        MouseUp += canvas_MouseUp;
        MouseMove += canvas_MouseMove;
    }

    public bool isEnabled { get; set; }

    public Bitmap? tempImage { get; set; }

    public void Populate(Bitmap screenshot)
    {
        tempImage = screenshot;
        isEnabled = true;
    }

    private void canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        //Set the start point
        _startDrag = e.GetPosition(canvas);

        //Move the selection marquee on top of all other objects in canvas
        Panel.SetZIndex(rectangle, canvas.Children.Count);

        //Capture the mouse
        if (!canvas.IsMouseCaptured)
            canvas.CaptureMouse();
        canvas.Cursor = Cursors.Cross;
    }

    public void CloseOverlay()
    {
        rectangle.Width = 0;
        rectangle.Height = 0;
        rectangle.RenderTransform = new TranslateTransform(0, 0);
        Topmost = false;
        isEnabled = false;

        // THIS FUCKING RECTANGLE WOULDN'T GO AWAY
        //    AND IT WOULD STAY FOR 1 FRAME WHEN RE-OPENNING THIS WINDOW
        //    SO I FORCED THAT FRAME TO HAPPEN BEFORE CLOSING
        //       AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAHHHHHHHHHHH
        //
        //  fucking hate rectangles
        Task.Run(async () =>
        {
            await Task.Delay(100);
            Dispatcher.InvokeIfRequired(Hide);
        });
    }

    private async void canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Release the mouse
        if (canvas.IsMouseCaptured)
            canvas.ReleaseMouseCapture();

        canvas.Cursor = Cursors.Arrow;

        Logger.Debug("User drew rectangle. point={Point},width={W},height={H}", _startDrag, rectangle.Width, rectangle.Height);

        if (rectangle.Width < 10 || rectangle.Height < 10)
        {
            // Box is smaller than 10x10 and thus will never be able to have any text.
            // Also used as a fail save to prevent the program from crashing if the user makes a 0x0 selection
            Logger.Debug("User selected an area too small");
            Main.StatusUpdate("Please select a larger area to scan", StatusSeverity.Warning);
            return;
        }

        var scaling = _windowInfoService.DpiScaling;
        var cutout = tempImage.Clone(
            rect: new Rectangle(
                x: (int)(_topLeft.X * scaling),
                y: (int)(_topLeft.Y * scaling),
                width: (int)(rectangle.Width * scaling),
                height: (int)(rectangle.Height * scaling)
            ),
            format: System.Drawing.Imaging.PixelFormat.DontCare
        );

        // try to hide the evidence as fast as possible
        rectangle.Visibility = Visibility.Hidden;

        Task.Run(async () => await OCR.ProcessSnapIt(cutout, tempImage, _topLeft));

        CloseOverlay();
    }

    private void canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!canvas.IsMouseCaptured)
            return;

        var currentPoint = e.GetPosition(canvas);

        // Calculate the top left corner of the rectangle regardless of drag direction
        var x = _startDrag.X < currentPoint.X ? _startDrag.X : currentPoint.X;
        var y = _startDrag.Y < currentPoint.Y ? _startDrag.Y : currentPoint.Y;

        if (rectangle.Visibility == Visibility.Hidden)
            rectangle.Visibility = Visibility.Visible;

        // Move the rectangle to proper place
        _topLeft = new System.Drawing.Point((int)x, (int)y);
        rectangle.RenderTransform = new TranslateTransform(x, y);

        // Set its size
        rectangle.Width = Math.Abs(e.GetPosition(canvas).X - _startDrag.X);
        rectangle.Height = Math.Abs(e.GetPosition(canvas).Y - _startDrag.Y);
    }
}
