using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mediator;
using WFInfo.Domain;
using WFInfo.Extensions;

namespace WFInfo;

/// <summary>
/// Interaction logic for AutoCount.xaml
/// </summary>
public partial class AutoCount : Window, INotificationHandler<AutoCountShow>
{
    public AutoAddViewModel ViewModel { get; }
    public SimpleCommand IncrementAll { get; }
    public SimpleCommand RemoveAll { get; }

    private readonly IMediator _mediator;

    public AutoCount(AutoAddViewModel viewModel, IMediator mediator)
    {
        this.ViewModel = viewModel;
        _mediator = mediator;
        RemoveAll = new SimpleCommand(RemoveFromParentAll);
        IncrementAll = new SimpleCommand(AddCountAll);

        /*
        for (int i = 0; i < 30; i++) //test fill block
        {
            List<string> tmp = new List<string>();
            tmp.Add("Ivara Prime Blueprint");
            tmp.Add("Braton Prime Blueprint");
            tmp.Add("Paris Prime Upper Limb");
            AutoAddSingleItem tmpItem = new AutoAddSingleItem(tmp, i % 5, viewModel);
            viewModel.addItem(tmpItem);
        }
        */
        InitializeComponent();
    }

    private void ShowAutoCount()
    {
        Show();
        Focus();
    }

    private void Hide(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void AddCountAll()
    {
        foreach (var item in ViewModel.ItemList)
        {
            if (item._parent != ViewModel)
            {
                item._parent = ViewModel;
            }
        }

        while (ViewModel.ItemList.Count > 0)
        {
            ViewModel.ItemList.FirstOrDefault().AddCount(false, _mediator);
        }

        Main.DataBase.SaveAll(DataTypes.All);
        EquipmentWindow.INSTANCE.ReloadItems();
    }

    private void RemoveFromParentAll()
    {
        foreach (var item in ViewModel.ItemList)
        {
            if (item._parent != ViewModel)
                item._parent = ViewModel;
        }

        while (ViewModel.ItemList.Count > 0)
        {
            ViewModel.ItemList.FirstOrDefault().Remove.Execute(null);
        }
    }

    // Allows the dragging of the window
    private new void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void RedirectScrollToParent(object sender, MouseWheelEventArgs e)
    {
        object tmp = VisualTreeHelper.GetParent(sender as DependencyObject);

        if (tmp is not ScrollContentPresenter scp)
            return;

        var eventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        };

        scp.RaiseEvent(eventArgs);
    }

    public ValueTask Handle(AutoCountShow notification, CancellationToken cancellationToken)
    {
        Dispatcher.InvokeIfRequired(ShowAutoCount);
        return ValueTask.CompletedTask;
    }
}
