using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using WFInfo.Domain;
using WFInfo.Services;

namespace WFInfo;

public class INPC : INotifyPropertyChanged
{
    protected bool SetField<T>(ref T backingField, T value, [CallerMemberName] string propName = null)
    {
        var valueChanged = false;

        // Can't use equality operator on generic types
        if (!EqualityComparer<T>.Default.Equals(backingField, value))
        {
            backingField = value;
            RaisePropertyChanged(propName);
            valueChanged = true;
        }

        return valueChanged;
    }

    public event PropertyChangedEventHandler PropertyChanged = delegate { };

    protected void RaisePropertyChanged([CallerMemberName] string propName = null)
    {
        if (!string.IsNullOrWhiteSpace(propName) && (PropertyChanged != null))
        {
            PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
    }
}

public class SimpleCommand(Action? action) : ICommand
{
    public bool CanExecute(object? parameter)
    {
        return action is not null;
    }

    public event EventHandler CanExecuteChanged
    {
        // This will never be used, this is just to ignore the warning prompt in VS
        add { }    // doesn't matter
        remove { } // doesn't matter
    }

    public void Execute(object? parameter)
    {
        action?.Invoke();
    }
}

public partial class TreeNode : INPC
{
    private static readonly ILogger Logger = Log.Logger.ForContext<TreeNode>();

    private const double INTACT_CHANCE_RARE = 0.02;
    private const double RADIANT_CHANCE_RARE = 0.1;
    private const double INTACT_CHANCE_UNCOMMON = 0.11;
    private const double RADIANT_CHANCE_UNCOMMON = 0.2;
    private const double INTACT_CHANCE_COMMON = 0.2533;
    private const double RADIANT_CHANCE_COMMON = 0.1667;

    private static ImageSource PLAT_SRC =
        (ImageSource)new ImageSourceConverter().ConvertFromString("pack://application:,,,/Resources/plat.gif");

    private static ImageSource DUCAT_SRC =
        (ImageSource)new ImageSourceConverter().ConvertFromString("pack://application:,,,/Resources/ducat_w.gif");

    private static Color RARE_COLOR = Color.FromRgb(255, 215, 0);
    private static Color UNCOMMON_COLOR = Color.FromRgb(192, 192, 192);
    private static Color COMMON_COLOR = Color.FromRgb(205, 127, 50);
    private static Brush RARE_BRUSH = new SolidColorBrush(RARE_COLOR);
    private static Brush UNCOMMON_BRUSH = new SolidColorBrush(UNCOMMON_COLOR);
    private static Brush COMMON_BRUSH = new SolidColorBrush(COMMON_COLOR);

    private static Color BACK_D_COLOR = Color.FromRgb(22, 22, 22);
    private static Color BACK_COLOR = Color.FromRgb(27, 27, 27);
    private static Color BACK_U_COLOR = Color.FromRgb(32, 32, 32);
    public static Brush BACK_D_BRUSH = new SolidColorBrush(BACK_D_COLOR);
    private static Brush BACK_BRUSH = new SolidColorBrush(BACK_COLOR);
    public static Brush BACK_U_BRUSH = new SolidColorBrush(BACK_U_COLOR);

    public TreeNode(string name, string vaulted, bool mastered, byte showAll)
    {
        Name = name;
        Vaulted = vaulted;
        Mastered = mastered;
        ShowAll = showAll;
        ChildrenFiltered = [];
        Children = [];
        SetSilent();
    }

    public object ShowAll { get; set; }

    public bool TopLevel { get; set; } = false;

    private string _era;

    public string Era
    {
        get => _era;
        set => SetField(ref _era, value);
    }

    private int _sortNum = -1;

    public int SortNum
    {
        get => _sortNum;
        set => SetField(ref _sortNum, value);
    }

    private string _name;

    public string Name
    {
        get => TopLevel ? $"{_era} {_name}" : _name;
        set => SetField(ref _name, value);
    }

    public string Name_Sort
    {
        get => current != null ? $"{current.SortNum}{_era} {_name}" : SortNum + _name;
        set => SetField(ref _name, value);
    }

    public string EqmtName_Sort
    {
        get => SortNum + _name;
        set => SetField(ref _name, value);
    }

    private Brush _colorBrush = new SolidColorBrush(Color.FromRgb(177, 208, 217));

    public Brush NameBrush
    {
        get => _colorBrush;
        set => SetField(ref _colorBrush, value);
    }

    private Color _color = Color.FromRgb(177, 208, 217);

    public Color NameColor
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    private Brush _backcolor = BACK_BRUSH;

    public Brush Background_Color
    {
        get => _backcolor;
        set => SetField(ref _backcolor, value);
    }

    private Thickness _col1margin = new(0, 0, 18, 0);

    public Thickness Col1_Margin1
    {
        get => _col1margin;
        set => SetField(ref _col1margin, value);
    }

    private Thickness _col1margin2 = new(0, 0, 0, 0);

    public Thickness Col1_Margin2
    {
        get => _col1margin2;
        set => SetField(ref _col1margin2, value);
    }

    private Thickness _col2margin = new(0, 0, 18, 0);

    public Thickness Col2_Margin1
    {
        get => _col2margin;
        set => SetField(ref _col2margin, value);
    }

    private Thickness _col2margin2 = new(0, 0, 0, 0);

    public Thickness Col2_Margin2
    {
        get => _col2margin2;
        set => SetField(ref _col2margin2, value);
    }

    private string _vaulted;

    public string Vaulted
    {
        get => _vaulted;
        set => SetField(ref _vaulted, value);
    }

    public bool IsVaulted()
    {
        return Vaulted.Length == 0;
    }

    public void SetSilent()
    {
        Grid_Shown = "Visible";

        Col1_Text1 = string.Empty;
        Col1_Text2 = string.Empty;
        Col1_Img1 = null;
        Col1_Img1_Shown = "Hidden";

        Col2_Text1 = string.Empty;
        Col2_Text2 = string.Empty;
        Col2_Text3 = string.Empty;
        Col2_Img1 = null;
        Col2_Img1_Shown = "Hidden";
    }

    public void SetEraText()
    {
        _intact = 0;
        _radiant = 0;

        foreach (var node in Children)
        {
            // IsVaulted is true if its not vaulted
            if (node.IsVaulted())
            {
                _intact += node._intact;
                _radiant += node._radiant;
            }
        }

        _bonus = _radiant - _intact;

        Col1_Text1 = "INT:";
        Col1_Text2 = _intact.ToString("F1");

        Col1_Img1 = PLAT_SRC;
        Col1_Img1_Shown = "Visible";

        Col2_Text1 = "RAD:";
        Col2_Text2 = _radiant.ToString("F1");
        var tempBonus = (int)(_bonus * 10);
        Col2_Text3 = "(";
        if (tempBonus >= 0)
            Col2_Text3 += "+";
        Col2_Text3 += $"{(tempBonus / 10.0):F1})";

        Col2_Img1 = PLAT_SRC;
        Col2_Img1_Shown = "Visible";
    }

    public void SetRelicText()
    {
        _intact = 0;
        _radiant = 0;

        foreach (var node in Children)
        {
            if (node.NameColor == RARE_COLOR)
            {
                _intact += INTACT_CHANCE_RARE   * node._plat;
                _radiant += RADIANT_CHANCE_RARE * node._plat;
            }
            else if (node.NameColor == UNCOMMON_COLOR)
            {
                _intact += INTACT_CHANCE_UNCOMMON   * node._plat;
                _radiant += RADIANT_CHANCE_UNCOMMON * node._plat;
            }
            else
            {
                _intact += INTACT_CHANCE_COMMON   * node._plat;
                _radiant += RADIANT_CHANCE_COMMON * node._plat;
            }
        }

        _bonus = _radiant - _intact;
        Grid_Shown = "Visible";

        Col1_Text1 = "INT:";
        Col1_Text2 = _intact.ToString("F1");

        Col1_Img1 = PLAT_SRC;
        Col1_Img1_Shown = "Visible";

        Col2_Text1 = "RAD:";
        Col2_Text2 = _radiant.ToString("F1");
        var tempBonus = (int)(_bonus * 10);
        Col2_Text3 = "(";
        if (tempBonus >= 0)
            Col2_Text3 += "+";
        Col2_Text3 += (tempBonus / 10.0).ToString("F1") + ")";

        Col2_Img1 = PLAT_SRC;
        Col2_Img1_Shown = "Visible";
    }

    public bool GetSetInfo()
    {
        var primeSetName = Data.GetSetName(Name);
        if (!Main.DataBase.MarketData.TryGetValue(primeSetName, out var primeSetJToken))
        {
            return false; // This is not a set
        }

        var primeSet = (JObject)primeSetJToken;

        var primeSetPlat = primeSet["plat"].ToObject<string>();

        Grid_Shown = "Visible";
        Plat_Val = double.Parse(primeSetPlat, ApplicationConstants.Culture);
        Owned_Capped_Val = 0;
        Owned_Plat_Val = 0;
        Owned_Ducat_Val = 0;
        Owned_Val = 0;
        Count_Val = 0;
        Mastered = Main.DataBase.EquipmentData[this.dataRef]["mastered"].ToObject<bool>();
        foreach (var kid in Children)
        {
            Owned_Capped_Val += kid.Owned_Capped_Val;
            Owned_Plat_Val += kid.Owned_Plat_Val;
            Owned_Ducat_Val += kid.Owned_Ducat_Val;
            Owned_Val += kid.Owned_Val;
            Count_Val += kid.Count_Val;
        }

        PrimeUpdateDiff(true);
        Col1_Text2 = _plat.ToString("F1");

        Col1_Img1 = PLAT_SRC;
        Col1_Img1_Shown = "Visible";
        return true;
    }

    internal void SetPrimeEqmt(double plat, double ducat, int owned, int count)
    {
        Plat_Val = plat;
        Owned_Capped_Val = Math.Min(owned, count);
        Owned_Plat_Val = owned  * plat;
        Owned_Ducat_Val = owned * ducat;
        Owned_Val = owned;
        Count_Val = count;

        PrimeUpdateDiff(false);
        Col1_Text2 = _plat.ToString("F1");

        Col1_Img1 = PLAT_SRC;
        Col1_Img1_Shown = "Visible";

        Col2_Text1 = string.Empty;
        Col2_Text2 = string.Empty;
        Col2_Text3 = string.Empty;
        Col2_Img1 = null;
        Col2_Img1_Shown = "Hidden";
    }

    internal void ChangeExpandedTo(bool expand)
    {
        IsExpanded = expand;
        foreach (var kid in Children)
            kid.ChangeExpandedTo(expand);
    }

    public void SetPrimePart(double plat, int ducat, int owned, int count)
    {
        SetPrimeEqmt(plat, ducat, owned, count);
        Col2_Text3 = ducat.ToString();
        Col2_Img1 = DUCAT_SRC;
        Col2_Img1_Shown = "Visible";
        Col2_Margin1 = new Thickness(0, 0, 28, 0);
        Col2_Margin2 = new Thickness(0, 0, 10, 0);
    }

    public void SetPartText(double plat, int ducat, string rarity)
    {
        if (rarity.Contains("rare"))
        {
            NameColor = RARE_COLOR;
            NameBrush = RARE_BRUSH;
        }
        else if (rarity.Contains("uncomm"))
        {
            NameColor = UNCOMMON_COLOR;
            NameBrush = UNCOMMON_BRUSH;
        }
        else if (rarity.Contains("comm"))
        {
            NameColor = COMMON_COLOR;
            NameBrush = COMMON_BRUSH;
        }

        if (Name != "Forma Blueprint")
        {
            _plat = plat;
            _ducat = ducat;

            Col1_Text1 = string.Empty;
            Col1_Text2 = _plat.ToString("F1");

            Col1_Img1 = PLAT_SRC;
            Col1_Img1_Shown = "Visible";
            Col1_Margin1 = new Thickness(0, 0, 38, 0);
            Col1_Margin2 = new Thickness(0, 0, 20, 0);

            Col2_Text1 = string.Empty;
            Col2_Text2 = string.Empty;
            Col2_Text3 = ducat.ToString();
            Col2_Img1 = DUCAT_SRC;
            Col2_Img1_Shown = "Visible";
            Col2_Margin1 = new Thickness(0, 0, 78, 0);
            Col2_Margin2 = new Thickness(0, 0, 60, 0);
        }
        else
        {
            Col1_Img1 = null;
            Col1_Text1 = string.Empty;
            Col1_Text2 = string.Empty;

            Col2_Img1 = null;
            Col2_Text1 = string.Empty;
            Col2_Text2 = string.Empty;
        }
    }

    public void ResetFilter()
    {
        foreach (var node in Children)
            node.ResetFilter();

        // This doesn't work, maybe i made mistake
        //Children.AsParallel().ForAll(node => node.ResetFilter());

        ForceVisibility = false;
        ChildrenFiltered = Children;
    }

    public void FilterOutVaulted(bool additionalFilter = false)
    {
        var filterList = additionalFilter ? ChildrenFiltered : Children;
        ChildrenFiltered = filterList.AsParallel().Where(node => node.IsVaulted()).ToList();
    }

    public void RecolorChildren()
    {
        var i = false;
        foreach (var child in ChildrenFiltered)
        {
            i = !i;
            var brush = i ? BACK_D_BRUSH : BACK_U_BRUSH;
            child.Background_Color = brush;
        }
    }

    public string GetFullName()
    {
        var prnt = Name;
        var temp = current;
        while (temp != null)
        {
            prnt = temp.Name + "/" + prnt;
            temp = temp.current;
        }

        return prnt;
    }

    private void PrintItemToConsole(Dictionary<string, bool> matchedText)
    {
        var prnt = $"{Name}: ";
        var temp = current;
        while (temp != null)
        {
            prnt = temp.Name + "/" + prnt;
            temp = temp.current;
        }

        foreach (var kvp in matchedText)
        {
            prnt += kvp.Key + "(" + kvp.Value + ") ";
        }

        Logger.Debug("Matched text : {Text}", prnt);
    }

    public bool FilterSearchText(
        IEnumerable<string> searchText,
        bool removeLeaves,
        bool additionalFilter = false,
        Dictionary<string, bool> matchedText = null)
    {
        var matchedTextCopy = new Dictionary<string, bool>();

        var done = true;
        foreach (var text in searchText)
        {
            var tempVal = (matchedText != null && matchedText[text]) || Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);
            matchedTextCopy[text] = tempVal;
            done = done && tempVal;
        }

        List<TreeNode> filterList = additionalFilter ? ChildrenFiltered : Children;
        if (done)
        {
            if (ChildrenFiltered.Count > 0)
                ChildrenFiltered = filterList;
            else
                ForceVisibility = true;

            return true;
        }

        List<TreeNode> temp = [];
        foreach (var node in filterList)
            if (node.FilterSearchText(searchText, removeLeaves, additionalFilter, matchedTextCopy))
                temp.Add(node);

        if (temp.Count == Children.Count)
            foreach (var node in filterList)
                node.ForceVisibility = false;

        ChildrenFiltered = (filterList.Count > 0 && filterList[0].ChildrenFiltered.Count > 0) || removeLeaves
            ? temp
            : filterList;
        return temp.Count > 0;
    }

    internal void Sort(int index, bool isRelics = true, int depth = 0)
    {
        foreach (var node in Children)
            node.Sort(index, isRelics, depth + 1);
        if (Children.Count <= 0)
            return;

        if (isRelics)
        {
            if (depth == 0) // Relics
            {
                switch (index)
                {
                    // 0 - Name
                    // 1 - Average intact plat
                    // 2 - Average radiant plat
                    // 3 - Difference (radiant-intact)
                    case 1:
                        Children = Children.OrderByDescending(p => p._intact).ToList();
                        ChildrenFiltered = ChildrenFiltered.OrderByDescending(p => p._intact).ToList();
                        break;
                    case 2:
                        Children = Children.OrderByDescending(p => p._radiant).ToList();
                        ChildrenFiltered = ChildrenFiltered.OrderByDescending(p => p._radiant).ToList();
                        break;
                    case 3:
                        Children = Children.OrderByDescending(p => p._bonus).ToList();
                        ChildrenFiltered = ChildrenFiltered.OrderByDescending(p => p._bonus).ToList();
                        break;
                    default:
                        Children = Children.OrderBy(p => PadNumbers(p.Name)).ToList();
                        ChildrenFiltered = ChildrenFiltered.OrderBy(p => PadNumbers(p.Name)).ToList();
                        break;
                }
            }
            else // Parts
            {
                Children = Children.OrderByDescending(p => p.NameColor.G).ToList();
                ChildrenFiltered = ChildrenFiltered.OrderByDescending(p => p.NameColor.G).ToList();
            }
        }
        else
        {
            switch (index)
            {
                // 0 - Name
                // 1 - Plat
                // 2 - Unowned (Capped)
                // 3 - Owned (Uncapped)
                // 4 - Owned Plat Value

                case 1:
                    Children = Children.OrderByDescending(p => p.Plat_Val).ToList();
                    ChildrenFiltered = ChildrenFiltered.OrderByDescending(p => p.Plat_Val).ToList();
                    break;
                case 2:
                    Children = Children.OrderBy(p => p.Owned_Capped_Val).ThenBy(p => p.Diff_Val).ToList();
                    ChildrenFiltered = ChildrenFiltered
                                       .OrderBy(p => p.Owned_Capped_Val)
                                       .ThenBy(p => p.Diff_Val)
                                       .ToList();
                    break;
                case 3:
                    Children = Children.OrderByDescending(p => p.Owned_Val).ToList();
                    ChildrenFiltered = ChildrenFiltered.OrderByDescending(p => p.Owned_Val).ToList();
                    break;
                case 4:
                    Children = Children.OrderByDescending(p => p.Owned_Plat_Val).ToList();
                    ChildrenFiltered = ChildrenFiltered.OrderByDescending(p => p.Owned_Plat_Val).ToList();
                    break;
                case 5:
                    Children = Children.OrderByDescending(p => p.Owned_Ducat_Val).ToList();
                    ChildrenFiltered = ChildrenFiltered.OrderByDescending(p => p.Owned_Ducat_Val).ToList();
                    break;
                default:
                    Children = Children.OrderBy(p => PadNumbers(p.Name)).ToList();
                    ChildrenFiltered = ChildrenFiltered.OrderBy(p => PadNumbers(p.Name)).ToList();
                    break;
            }
        }
    }

    public static string PadNumbers(string input)
    {
        return NumberRegEx().Replace(input, match => match.Value.PadLeft(5, '0'));
    }

    private string _col1_text1 = "INT:";

    public string Col1_Text1
    {
        get => _col1_text1;
        private set => SetField(ref _col1_text1, value);
    }

    private string _col1_text2 = "4.4";

    public string Col1_Text2
    {
        get => _col1_text2;
        private set => SetField(ref _col1_text2, value);
    }

    private ImageSource _col1_img1;

    public ImageSource Col1_Img1
    {
        get => _col1_img1;
        private set => SetField(ref _col1_img1, value);
    }

    private string _grid_shown = "Visible";

    public string Grid_Shown
    {
        get => _grid_shown;
        private set => SetField(ref _grid_shown, value);
    }

    private string _col1_img1_shown = "Visible";

    public string Col1_Img1_Shown
    {
        get => _col1_img1_shown;
        private set => SetField(ref _col1_img1_shown, value);
    }

    private string _col2_text1 = "RAD:";

    public string Col2_Text1
    {
        get => _col2_text1;
        private set => SetField(ref _col2_text1, value);
    }

    private string _col2_text2 = "9.9";

    public string Col2_Text2
    {
        get => _col2_text2;
        private set => SetField(ref _col2_text2, value);
    }

    private string _col2_text3 = "(+5.5)";

    public string Col2_Text3
    {
        get => _col2_text3;
        private set => SetField(ref _col2_text3, value);
    }

    private ImageSource _col2_img1;

    public ImageSource Col2_Img1
    {
        get => _col2_img1;
        private set => SetField(ref _col2_img1, value);
    }

    private string _col2_img1_shown = "Visible";

    public string Col2_Img1_Shown
    {
        get => _col2_img1_shown;
        private set => SetField(ref _col2_img1_shown, value);
    }

    private double _plat;

    public double Plat_Val
    {
        get => _plat;
        set => SetField(ref _plat, value);
    }

    private int _ducat;

    public int Ducat_Val
    {
        get => _ducat;
        set => SetField(ref _ducat, value);
    }

    private int _owned;

    public int Owned_Val
    {
        get => _owned;
        set => SetField(ref _owned, value);
    }

    private int _owned_capped;

    public int Owned_Capped_Val
    {
        get => _owned_capped;
        set => SetField(ref _owned_capped, value);
    }

    private double _owned_plat;

    public double Owned_Plat_Val
    {
        get => _owned_plat;
        set => SetField(ref _owned_plat, value);
    }

    private double _owned_ducat;

    public double Owned_Ducat_Val
    {
        get => _owned_ducat;
        set => SetField(ref _owned_ducat, value);
    }

    private int _count;

    public int Count_Val
    {
        get => _count;
        set => SetField(ref _count, value);
    }

    private double _diff;

    public double Diff_Val
    {
        get => _diff;
        set => SetField(ref _diff, value);
    }

    private double _intact;

    public double Intact_Val
    {
        get => _intact;
        set => SetField(ref _intact, value);
    }

    private double _radiant;

    public double Radiant_Val
    {
        get => _radiant;
        set => SetField(ref _radiant, value);
    }

    private double _bonus;

    public double Bonus_Val
    {
        get => _bonus;
        set => SetField(ref _bonus, value);
    }

    public Visibility IsVisible =>
        _forceVisibility || current == null || current.IsExpanded || TopLevel
            ? Visibility.Visible
            : Visibility.Collapsed;

    private bool _forceVisibility;

    public bool ForceVisibility
    {
        get => _forceVisibility;
        set
        {
            SetField(ref _forceVisibility, value);
            RaisePropertyChanged(nameof(IsVisible));
        }
    }

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            SetField(ref _isExpanded, value);
            foreach (var kid in Children)
                kid.RaisePropertyChanged(nameof(IsVisible));
        }
    }

    private List<TreeNode> _childrenFiltered;

    public List<TreeNode> ChildrenFiltered
    {
        get => _childrenFiltered;
        private set => SetField(ref _childrenFiltered, value);
    }

    private List<TreeNode> _children;

    public List<TreeNode> Children
    {
        get => _children;
        private set => SetField(ref _children, value);
    }

    private bool _mastered;

    public bool Mastered
    {
        get => _mastered;
        set => SetField(ref _mastered, value);
    }

    private TreeNode? current;

    public void AddChild(TreeNode kid)
    {
        kid.current = this;
        Children.Add(kid);
    }

    public override string ToString()
    {
        return $"{Era} {Name}";
    }

    private ICommand _decrement;

    public ICommand DecrementPart
    {
        get => _decrement;
        private set => SetField(ref _decrement, value);
    }

    private ICommand _increment;

    public ICommand IncrementPart
    {
        get => _increment;
        private set => SetField(ref _increment, value);
    }

    private ICommand _markcomplete;

    public ICommand MarkComplete
    {
        get => _markcomplete;
        private set => SetField(ref _markcomplete, value);
    }

    private string dataRef;

    public void MakeClickable(string eqmtRef)
    {
        dataRef = eqmtRef;
        DecrementPart = new SimpleCommand(DecrementPartFunc);
        IncrementPart = new SimpleCommand(IncrementPartFunc);
        MarkComplete = new SimpleCommand(MarkCompleteFunc);
    }

    public async void DecrementPartFunc()
    {
        if (current.dataRef != null)
        {
            await Task.Run(() => DecrementPartThreaded(current));
        }
    }

    public async void IncrementPartFunc()
    {
        if (current.dataRef != null)
        {
            await Task.Run(() => IncrementPartThreaded(current));
        }
    }

    private async void MarkCompleteFunc()
    {
        await Task.Run(MarkSetAsComplete);

        /*Main.AddLog("test");
        Main.AddLog(current.dataRef);
        if (current.dataRef != null)
        {
            Main.AddLog("test");
        }*/
    }

    public void ReloadPartOwned(TreeNode Parent)
    {
        //DOES NOT UPDATE PARENT
        var job = Main.DataBase.EquipmentData[Parent.dataRef]["parts"][dataRef] as JObject;
        Owned_Val = job["owned"].ToObject<int>();
        Owned_Capped_Val = Math.Min(Owned_Val, Count_Val);
        Owned_Plat_Val = Owned_Val  * Plat_Val;
        Owned_Ducat_Val = Owned_Val * Ducat_Val;
        PrimeUpdateDiff(false);
    }

    private void DecrementPartThreaded(TreeNode Parent)
    {
        var job = Main.DataBase.EquipmentData[Parent.dataRef]["parts"][dataRef] as JObject;
        var owned = Owned_Val;
        if (owned > 0)
        {
            job["owned"] = owned - 1;
            Main.DataBase.SaveAll(DataTypes.All);
            Owned_Val--;
            Owned_Capped_Val = Math.Min(Owned_Val, Count_Val);
            Owned_Plat_Val = Owned_Val  * Plat_Val;
            Owned_Ducat_Val = Owned_Val * Ducat_Val;
            PrimeUpdateDiff(false);
            var count = Count_Val;
            Parent.Owned_Val--;
            Parent.Owned_Plat_Val -= Plat_Val;
            Parent.Owned_Ducat_Val -= Ducat_Val;
            if (owned <= count)
            {
                Parent.Owned_Capped_Val--;
                Parent.PrimeUpdateDiff(true);
            }
        }
    }

    private void IncrementPartThreaded(TreeNode Parent)
    {
        var job = Main.DataBase.EquipmentData[Parent.dataRef]["parts"][dataRef] as JObject;
        var count = Count_Val;
        var owned = Owned_Val;
        job["owned"] = owned + 1;
        Main.DataBase.SaveAll(DataTypes.All);
        Owned_Val++;
        Owned_Capped_Val = Math.Min(Owned_Val, Count_Val);
        Owned_Plat_Val = Owned_Val  * Plat_Val;
        Owned_Ducat_Val = Owned_Val * Ducat_Val;
        PrimeUpdateDiff(false);
        Parent.Owned_Val++;
        Parent.Owned_Plat_Val += Plat_Val;
        Parent.Owned_Ducat_Val += Ducat_Val;
        if (owned < count)
        {
            Parent.Owned_Capped_Val++;
            Parent.PrimeUpdateDiff(true);
        }
    }

    private void MarkSetAsComplete()
    {
        Main.DataBase.EquipmentData[this.dataRef]["mastered"] = !Mastered;
        Mastered = !Mastered;
        Main.DataBase.SaveAll(DataTypes.All);
    }

    private void PrimeUpdateDiff(bool UseCappedOwned)
    {
        var owned = UseCappedOwned ? Owned_Capped_Val : Owned_Val;

        Diff_Val = owned / (double)Count_Val - 0.01 * Count_Val;
        Col1_Text1 = $"{owned}/{Count_Val}";
    }

    [System.Text.RegularExpressions.GeneratedRegex("[0-9]+")]
    private static partial System.Text.RegularExpressions.Regex NumberRegEx();
}
