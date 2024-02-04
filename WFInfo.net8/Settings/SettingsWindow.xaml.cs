using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WFInfo.Services.OpticalCharacterRecognition;

namespace WFInfo.Settings;

/// <summary>
/// Interaction logic for Settings.xaml
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsViewModel SettingsViewModel { get; }

    private bool IsActivationFocused => Activation_key_box.IsFocused;

    public SettingsWindow(SettingsViewModel settingsViewModel)
    {
        InitializeComponent();
        DataContext = this;
        SettingsViewModel = settingsViewModel;
    }

    public void Populate()
    {
        Overlay_sliders.Visibility = Visibility.Collapsed; // default hidden for the majority of states

        if (SettingsViewModel.Display == Display.Overlay)
        {
            OverlayRadio.IsChecked = true;
            Overlay_sliders.Visibility = Visibility.Visible;
        }
        else if (SettingsViewModel.Display == Display.Light)
        {
            LightRadio.IsChecked = true;
        }
        else
        {
            WindowRadio.IsChecked = true;
        }

        if (SettingsViewModel.Auto)
        {
            autoCheckbox.IsChecked = true;
            Autolist.IsEnabled = true;
            Autocsv.IsEnabled = true;
            Autoadd.IsEnabled = true;
        }
        else
        {
            Autolist.IsEnabled = false;
            Autocsv.IsEnabled = false;
            Autoadd.IsEnabled = false;
        }

        foreach (ComboBoxItem localeItem in localeCombobox.Items)
        {
            if (SettingsViewModel.Locale.Equals(localeItem.Tag.ToString()))
            {
                localeItem.IsSelected = true;
            }
        }

        Focus();
    }

    public void Save()
    {
        SettingsViewModel.Save();
    }

    private void Hide(object sender, RoutedEventArgs e)
    {
        Save();
        Hide();
    }

    // Allows the draging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.Focus(hidden);
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void WindowChecked(object sender, RoutedEventArgs e)
    {
        SettingsViewModel.Display = Display.Window;
        Overlay_sliders.Visibility = Visibility.Collapsed;
        clipboardCheckbox.IsEnabled = true;
        Save();
    }

    private void OverlayChecked(object sender, RoutedEventArgs e)
    {
        SettingsViewModel.Display = Display.Overlay;
        Overlay_sliders.Visibility = Visibility.Visible;
        clipboardCheckbox.IsEnabled = true;
        Save();
    }

    private void AutoClicked(object sender, RoutedEventArgs e)
    {
        SettingsViewModel.Auto = autoCheckbox.IsChecked.Value;
        if (SettingsViewModel.Auto)
        {
            var message = "Do you want to enable the new auto mode?" + Environment.NewLine +
                          "This connects to the warframe debug logger to detect the reward window." +
                          Environment.NewLine +
                          "The logger contains info about your pc specs, your public IP, and your email." +
                          Environment.NewLine +
                          "We will be ignoring all of that and only looking for the Fissure Reward Screen." +
                          Environment.NewLine +
                          "We will begin listening after your approval, and it is completely inactive currently." +
                          Environment.NewLine +
                          "If you opt-in, we will be using a windows method to receive this info quicker, but it is the same info being written to EE.log, which you can check before agreeing." +
                          Environment.NewLine +
                          "If you want more information or have questions, please contact us on Discord.";
            MessageBoxResult messageBoxResult =
                MessageBox.Show(message, "Automation Mode Opt-In", MessageBoxButton.YesNo);
            if (messageBoxResult == MessageBoxResult.Yes)
            {
                Main.DataBase.EnableLogCapture();
                Autolist.IsEnabled = true;
                Autocsv.IsEnabled = true;
                Autoadd.IsEnabled = true;
            }
            else
            {
                SettingsViewModel.Auto = false;
                autoCheckbox.IsChecked = false;
                Main.DataBase.DisableLogCapture();
                Autolist.IsEnabled = false;
                Autocsv.IsEnabled = false;
                Autoadd.IsEnabled = false;
            }
        }
        else
        {
            SettingsViewModel.Auto = false;
            Autolist.IsEnabled = false;
            Autocsv.IsEnabled = false;
            Autoadd.IsEnabled = false;
            Main.DataBase.DisableLogCapture();
        }

        Save();
    }

    private void ActivationMouseDown(object sender, MouseEventArgs e)
    {
        if (!IsActivationFocused)
            return;
        
        MouseButton key = MouseButton.Left;

        if (e.MiddleButton == MouseButtonState.Pressed)
            key = MouseButton.Middle;
        else if (e.XButton1 == MouseButtonState.Pressed)
            key = MouseButton.XButton1;
        else if (e.XButton2 == MouseButtonState.Pressed)
            key = MouseButton.XButton2;

        if (key == MouseButton.Left)
            return;
        
        e.Handled = true;
        SettingsViewModel.ActivationKey = key.ToString();
        hidden.Focus();
    }

    private void ActivationUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == SettingsViewModel.SearchItModifierKey || e.Key == SettingsViewModel.SnapitModifierKey ||
            e.Key == SettingsViewModel.MasterItModifierKey)
        {
            hidden.Focus();
            return;
        }

        Key key = e.Key != Key.System ? e.Key : e.SystemKey;
        SettingsViewModel.ActivationKey = key.ToString();
        hidden.Focus();
    }

    private void ClickCreateDebug(object sender, RoutedEventArgs e)
    {
        Main.SpawnErrorPopup(DateTime.UtcNow, 1800);
    }

    private void localeComboboxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxItem item = (ComboBoxItem)localeCombobox.SelectedItem;

        string selectedLocale = item.Tag.ToString();
        SettingsViewModel.Locale = selectedLocale;
        Save();

        _ = OCR.updateEngineAsync();
        _ = Task.Run(async () => { Main.DataBase.ReloadItems(); });
    }

    private void LightRadioChecked(object sender, RoutedEventArgs e)
    {
        SettingsViewModel.Display = Display.Light;
        Overlay_sliders.Visibility = Visibility.Collapsed;
        SettingsViewModel.Clipboard = true;
        clipboardCheckbox.IsChecked = true;
        clipboardCheckbox.IsEnabled = false;
        Save();
    }

    private void Searchit_key_box_KeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == SettingsViewModel.SnapitModifierKey || e.Key == SettingsViewModel.MasterItModifierKey)
        {
            hidden.Focus();
            return;
        }

        Key key = e.Key != Key.System ? e.Key : e.SystemKey;
        SettingsViewModel.SearchItModifierKey = key;
        hidden.Focus();
    }

    private void Snapit_key_box_KeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == SettingsViewModel.SearchItModifierKey || e.Key == SettingsViewModel.MasterItModifierKey)
        {
            hidden.Focus();
            return;
        }

        Key key = e.Key != Key.System ? e.Key : e.SystemKey;
        SettingsViewModel.SnapitModifierKey = key;
        hidden.Focus();
    }


    private void Masterit_key_box_KeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == SettingsViewModel.SearchItModifierKey || e.Key == SettingsViewModel.SnapitModifierKey)
        {
            hidden.Focus();
            return;
        }

        Key key = e.Key != Key.System ? e.Key : e.SystemKey;
        SettingsViewModel.MasterItModifierKey = key;
        hidden.Focus();
    }

    private void ConfigureTheme_button_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ThemeAdjuster.ShowThemeAdjuster();
    }

    private void ThemeSelectionComboBox_OnDropDownClosed(object sender, EventArgs e)
    {
        MessageBoxResult messageBoxResult = MessageBox.Show(
            "This option will not change WFInfo screen style. It will force app to think you have selected this theme in Warframe (and will use its pixel colors for item scanning). Unless you know what you're doing, leave Auto selected.",
            "Change of target theme", System.Windows.MessageBoxButton.OK);
    }
}