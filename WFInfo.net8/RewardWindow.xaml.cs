using System.Windows;
using System.Windows.Input;
using Mediator;
using Serilog;
using WFInfo.Domain;
using WFInfo.Extensions;

namespace WFInfo;

public partial class RewardWindow : Window, INotificationHandler<LoadRewardTextData>
{
    private static readonly ILogger Logger = Log.Logger.ForContext<RewardWindow>();
    private static readonly int[] Widths = [251, 501, 751, 1000];

    private readonly IMediator _mediator;

    public RewardWindow(IMediator mediator)
    {
        InitializeComponent();
        _mediator = mediator;
    }

    private void LoadTextData(LoadRewardTextData notification, int width)
    {
        Show();
        Topmost = true;

        switch (notification.PartNumber)
        {
            case 0:
                firstPartText.Text = notification.Name;
                if (notification.HideReward)
                {
                    platImage.Visibility = Visibility.Hidden;
                    setPlatImage.Visibility = Visibility.Hidden;
                    firstDucatImage.Visibility = Visibility.Hidden;
                    firstPlatText.Text = string.Empty;
                    firstSetPlatText.Text = string.Empty;
                    firstDucatText.Text = string.Empty;
                    firstVolumeText.Text = string.Empty;
                    firstVaultedMargin.Visibility = Visibility.Hidden;
                    firstOwnedText.Text = string.Empty;
                    if (notification.Resize)
                        Width = width;
                    break;
                }

                platImage.Visibility = Visibility.Visible;
                setPlatImage.Visibility = Visibility.Visible;
                firstDucatImage.Visibility = Visibility.Visible;
                firstPlatText.Text = notification.Plat;
                firstSetPlatText.Text = $"Full set price: {notification.PrimeSetPlat}";
                firstDucatText.Text = notification.Ducats;
                firstVolumeText.Text = $"{notification.Volume} sold last 48hrs";
                firstVaultedMargin.Visibility = notification.Vaulted ? Visibility.Visible : Visibility.Hidden;
                firstOwnedText.Text = notification.Owned.Length > 0 ? $"{(notification.Mastered ? "✓ " : string.Empty)}{notification.Owned} OWNED" : string.Empty;
                if (notification.Resize)
                    Width = width;
                break;

            case 1:
                secondPartText.Text = notification.Name;
                if (notification.HideReward)
                {
                    platImage1.Visibility = Visibility.Hidden;
                    setPlatImage1.Visibility = Visibility.Hidden;
                    firstDucatImage1.Visibility = Visibility.Hidden;
                    secondPlatText.Text = string.Empty;
                    secondSetPlatText.Text = string.Empty;
                    secondDucatText.Text = string.Empty;
                    secondVolumeText.Text = string.Empty;
                    secondVaultedMargin.Visibility = Visibility.Hidden;
                    secondOwnedText.Text = string.Empty;
                    if (notification.Resize)
                        Width = width;
                }

                platImage1.Visibility = Visibility.Visible;
                setPlatImage1.Visibility = Visibility.Visible;
                firstDucatImage1.Visibility = Visibility.Visible;
                secondPlatText.Text = notification.Plat;
                secondSetPlatText.Text = $"Full set price: {notification.PrimeSetPlat}";
                secondDucatText.Text = notification.Ducats;
                secondVolumeText.Text = $"{notification.Volume} sold last 48hrs";
                secondVaultedMargin.Visibility = notification.Vaulted ? Visibility.Visible : Visibility.Hidden;
                secondOwnedText.Text = notification.Owned.Length > 0 ? $"{(notification.Mastered ? "✓ " : string.Empty)}{notification.Owned} OWNED" : string.Empty;
                if (notification.Resize)
                    Width = width;
                break;

            case 2:
                thirdPartText.Text = notification.Name;
                if (notification.HideReward)
                {
                    platImage2.Visibility = Visibility.Hidden;
                    setPlatImage2.Visibility = Visibility.Hidden;
                    firstDucatImage2.Visibility = Visibility.Hidden;
                    thirdPlatText.Text = string.Empty;
                    thirdSetPlatText.Text = string.Empty;
                    thirdDucatText.Text = string.Empty;
                    thirdVolumeText.Text = string.Empty;
                    thirdVaultedMargin.Visibility = Visibility.Hidden;
                    thirdOwnedText.Text = string.Empty;
                    if (notification.Resize)
                        Width = width;
                }

                platImage2.Visibility = Visibility.Visible;
                setPlatImage2.Visibility = Visibility.Visible;
                firstDucatImage2.Visibility = Visibility.Visible;
                thirdPlatText.Text = notification.Plat;
                thirdSetPlatText.Text = $"Full set price: {notification.PrimeSetPlat}";
                thirdDucatText.Text = notification.Ducats;
                thirdVolumeText.Text = $"{notification.Volume} sold last 48hrs";
                thirdVaultedMargin.Visibility = notification.Vaulted ? Visibility.Visible : Visibility.Hidden;
                thirdOwnedText.Text = notification.Owned.Length > 0 ? $"{(notification.Mastered ? "✓ " : string.Empty)}{notification.Owned} OWNED" : string.Empty;
                if (notification.Resize)
                    Width = width;
                break;

            case 3:
                fourthPartText.Text = notification.Name;
                if (notification.HideReward)
                {
                    platImage3.Visibility = Visibility.Hidden;
                    setPlatImage3.Visibility = Visibility.Hidden;
                    firstDucatImage3.Visibility = Visibility.Hidden;
                    fourthPlatText.Text = string.Empty;
                    fourthSetPlatText.Text = string.Empty;
                    fourthDucatText.Text = string.Empty;
                    fourthVolumeText.Text = string.Empty;
                    fourthVaultedMargin.Visibility = Visibility.Hidden;
                    fourthOwnedText.Text = string.Empty;
                    if (notification.Resize)
                        Width = width;
                }

                platImage3.Visibility = Visibility.Visible;
                setPlatImage3.Visibility = Visibility.Visible;
                firstDucatImage3.Visibility = Visibility.Visible;
                fourthPlatText.Text = notification.Plat;
                fourthSetPlatText.Text = $"Full set price: {notification.PrimeSetPlat}";
                fourthDucatText.Text = notification.Ducats;
                fourthVolumeText.Text = $"{notification.Volume} sold last 48hrs";
                fourthVaultedMargin.Visibility = notification.Vaulted ? Visibility.Visible : Visibility.Hidden;
                fourthOwnedText.Text = notification.Owned.Length > 0 ? (notification.Mastered ? "✓ " : string.Empty) + notification.Owned + " OWNED" : string.Empty;
                if (notification.Resize)
                    Width = width;
                break;
        }
    }

    private void Exit(object sender, RoutedEventArgs e)
    {
        Topmost = false;
        Hide();
    }

    // Allows the draging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    public async ValueTask Handle(LoadRewardTextData notification, CancellationToken cancellationToken)
    {
        if (notification.PartNumber is < 0 or > 3)
        {
            Logger.Error("Invalid part number: {PartNumber}", notification.PartNumber);
            await _mediator.Publish(new UpdateStatus($"Invalid part number: {notification.PartNumber}", StatusSeverity.Error), cancellationToken);
        }
        else
        {
            Dispatcher.InvokeIfRequired(() =>
            {
                LoadTextData(notification, Widths[notification.PartNumber]);
            });
        }
    }
}
