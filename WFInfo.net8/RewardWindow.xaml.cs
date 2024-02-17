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
                FirstPartText.Text = notification.Name;
                if (notification.HideReward)
                {
                    PlatImage.Visibility = Visibility.Hidden;
                    SetPlatImage.Visibility = Visibility.Hidden;
                    FirstDucatImage.Visibility = Visibility.Hidden;
                    FirstPlatText.Text = string.Empty;
                    FirstSetPlatText.Text = string.Empty;
                    FirstDucatText.Text = string.Empty;
                    FirstVolumeText.Text = string.Empty;
                    FirstVaultedMargin.Visibility = Visibility.Hidden;
                    FirstOwnedText.Text = string.Empty;
                    if (notification.Resize)
                        Width = width;
                    break;
                }

                PlatImage.Visibility = Visibility.Visible;
                SetPlatImage.Visibility = Visibility.Visible;
                FirstDucatImage.Visibility = Visibility.Visible;
                FirstPlatText.Text = notification.Plat;
                FirstSetPlatText.Text = $"Full set price: {notification.PrimeSetPlat}";
                FirstDucatText.Text = notification.Ducats;
                FirstVolumeText.Text = $"{notification.Volume} sold last 48hrs";
                FirstVaultedMargin.Visibility = notification.Vaulted ? Visibility.Visible : Visibility.Hidden;
                FirstOwnedText.Text = notification.Owned.Length > 0 ? $"{(notification.Mastered ? "✓ " : string.Empty)}{notification.Owned} OWNED" : string.Empty;
                if (notification.Resize)
                    Width = width;
                break;

            case 1:
                SecondPartText.Text = notification.Name;
                if (notification.HideReward)
                {
                    PlatImage1.Visibility = Visibility.Hidden;
                    SetPlatImage1.Visibility = Visibility.Hidden;
                    FirstDucatImage1.Visibility = Visibility.Hidden;
                    SecondPlatText.Text = string.Empty;
                    SecondSetPlatText.Text = string.Empty;
                    SecondDucatText.Text = string.Empty;
                    SecondVolumeText.Text = string.Empty;
                    SecondVaultedMargin.Visibility = Visibility.Hidden;
                    SecondOwnedText.Text = string.Empty;
                    if (notification.Resize)
                        Width = width;
                }

                PlatImage1.Visibility = Visibility.Visible;
                SetPlatImage1.Visibility = Visibility.Visible;
                FirstDucatImage1.Visibility = Visibility.Visible;
                SecondPlatText.Text = notification.Plat;
                SecondSetPlatText.Text = $"Full set price: {notification.PrimeSetPlat}";
                SecondDucatText.Text = notification.Ducats;
                SecondVolumeText.Text = $"{notification.Volume} sold last 48hrs";
                SecondVaultedMargin.Visibility = notification.Vaulted ? Visibility.Visible : Visibility.Hidden;
                SecondOwnedText.Text = notification.Owned.Length > 0 ? $"{(notification.Mastered ? "✓ " : string.Empty)}{notification.Owned} OWNED" : string.Empty;
                if (notification.Resize)
                    Width = width;
                break;

            case 2:
                ThirdPartText.Text = notification.Name;
                if (notification.HideReward)
                {
                    PlatImage2.Visibility = Visibility.Hidden;
                    SetPlatImage2.Visibility = Visibility.Hidden;
                    FirstDucatImage2.Visibility = Visibility.Hidden;
                    ThirdPlatText.Text = string.Empty;
                    ThirdSetPlatText.Text = string.Empty;
                    ThirdDucatText.Text = string.Empty;
                    ThirdVolumeText.Text = string.Empty;
                    ThirdVaultedMargin.Visibility = Visibility.Hidden;
                    ThirdOwnedText.Text = string.Empty;
                    if (notification.Resize)
                        Width = width;
                }

                PlatImage2.Visibility = Visibility.Visible;
                SetPlatImage2.Visibility = Visibility.Visible;
                FirstDucatImage2.Visibility = Visibility.Visible;
                ThirdPlatText.Text = notification.Plat;
                ThirdSetPlatText.Text = $"Full set price: {notification.PrimeSetPlat}";
                ThirdDucatText.Text = notification.Ducats;
                ThirdVolumeText.Text = $"{notification.Volume} sold last 48hrs";
                ThirdVaultedMargin.Visibility = notification.Vaulted ? Visibility.Visible : Visibility.Hidden;
                ThirdOwnedText.Text = notification.Owned.Length > 0 ? $"{(notification.Mastered ? "✓ " : string.Empty)}{notification.Owned} OWNED" : string.Empty;
                if (notification.Resize)
                    Width = width;
                break;

            case 3:
                FourthPartText.Text = notification.Name;
                if (notification.HideReward)
                {
                    PlatImage3.Visibility = Visibility.Hidden;
                    SetPlatImage3.Visibility = Visibility.Hidden;
                    FirstDucatImage3.Visibility = Visibility.Hidden;
                    FourthPlatText.Text = string.Empty;
                    FourthSetPlatText.Text = string.Empty;
                    FourthDucatText.Text = string.Empty;
                    FourthVolumeText.Text = string.Empty;
                    FourthVaultedMargin.Visibility = Visibility.Hidden;
                    FourthOwnedText.Text = string.Empty;
                    if (notification.Resize)
                        Width = width;
                }

                PlatImage3.Visibility = Visibility.Visible;
                SetPlatImage3.Visibility = Visibility.Visible;
                FirstDucatImage3.Visibility = Visibility.Visible;
                FourthPlatText.Text = notification.Plat;
                FourthSetPlatText.Text = $"Full set price: {notification.PrimeSetPlat}";
                FourthDucatText.Text = notification.Ducats;
                FourthVolumeText.Text = $"{notification.Volume} sold last 48hrs";
                FourthVaultedMargin.Visibility = notification.Vaulted ? Visibility.Visible : Visibility.Hidden;
                FourthOwnedText.Text = notification.Owned.Length > 0 ? (notification.Mastered ? "✓ " : string.Empty) + notification.Owned + " OWNED" : string.Empty;
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
