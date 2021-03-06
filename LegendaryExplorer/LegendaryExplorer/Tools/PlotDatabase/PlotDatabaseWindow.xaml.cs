﻿using LegendaryExplorer.Misc;
using LegendaryExplorer.SharedUI.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using LegendaryExplorerCore;
using LegendaryExplorerCore.PlotDatabase;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorer.SharedUI;
using LegendaryExplorerCore.Misc;
using System.Threading;
using Newtonsoft.Json;
using System.Globalization;
using System.ComponentModel;

namespace LegendaryExplorer.Tools.PlotManager
{
    /// <summary>
    /// Interaction logic for PlotManagerWindow.xaml
    /// </summary>
    public partial class PlotManagerWindow : NotifyPropertyChangedWindowBase
    {
        #region Declarations
        public ObservableCollectionExtended<PlotElement> ElementsTable { get; } = new();
        public ObservableCollectionExtended<PlotElement> RootNodes3 { get; } = new();
        public ObservableCollectionExtended<PlotElement> RootNodes2 { get; } = new();
        public ObservableCollectionExtended<PlotElement> RootNodes1 { get; } = new();

        private PlotElement _selectedNode;
        public PlotElement SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    UpdateSelection();
                }
            }
        }

        private string _currentOverallOperationText = "Plot Databases courtesy of Bioware.";
        public string CurrentOverallOperationText { get => _currentOverallOperationText; set => SetProperty(ref _currentOverallOperationText, value); }

        private MEGame _currentGame;
        public MEGame CurrentGame { get => _currentGame; set => SetProperty(ref _currentGame, value); }
        private int previousView { get; set; }
        private int _currentView;
        public int CurrentView { get => _currentView; set { previousView = _currentView; SetProperty(ref _currentView, value); } }
        private bool _needsSave;
        public bool NeedsSave { get => _needsSave; set => SetProperty(ref _needsSave, value); }

        private bool _ShowBoolStates = true;
        public bool ShowBoolStates { get => _ShowBoolStates; set => SetProperty(ref _ShowBoolStates, value); }
        private bool _ShowInts = true;
        public bool ShowInts { get => _ShowInts; set => SetProperty(ref _ShowInts, value); }
        private bool _ShowFloats = true;
        public bool ShowFloats { get => _ShowFloats; set => SetProperty(ref _ShowFloats, value); }
        private bool _ShowConditionals = true;
        public bool ShowConditionals { get => _ShowConditionals; set => SetProperty(ref _ShowConditionals, value); }
        private bool _ShowTransitions = true;
        public bool ShowTransitions { get => _ShowTransitions; set => SetProperty(ref _ShowTransitions, value); }
        private bool _ShowJournal = true;
        public bool ShowJournal { get => _ShowJournal; set => SetProperty(ref _ShowJournal, value); }
        private GridViewColumnHeader _lastHeaderClicked = null;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public ObservableCollectionExtended<PlotElementType> newItemTypes = new ObservableCollectionExtended<PlotElementType>() { PlotElementType.State,
            PlotElementType.SubState, PlotElementType.Integer, PlotElementType.Float, PlotElementType.Conditional, PlotElementType.Transition, PlotElementType.JournalGoal,
            PlotElementType.JournalItem, PlotElementType.JournalTask, PlotElementType.Consequence, PlotElementType.Flag };

        private bool updateTable = true;
        public ICommand FilterCommand { get; set; }
        public ICommand CopyToClipboardCommand { get; set; }
        public ICommand RefreshLocalCommand { get; set; }
        public ICommand SaveLocalCommand { get; set; }
        public ICommand ExitCommand { get; set; }
        public ICommand AddNewModCommand { get; set; }
        public ICommand ClickOkCommand { get; set; }
        public ICommand ClickCancelCommand { get; set; }
        public ICommand AddModCategoryCommand { get; set; }
        public ICommand AddModItemCommand { get; set; }
        public ICommand DeleteModItemCommand { get; set; }
        public bool IsModRoot() => SelectedNode?.ElementId == 100000;
        public bool IsMod() => SelectedNode?.Type == PlotElementType.Mod;
        public bool CanAddCategory() => SelectedNode?.Type == PlotElementType.Mod || SelectedNode?.Type == PlotElementType.Category;
        public bool CanDeleteItem() => SelectedNode?.ElementId > 100000 && SelectedNode.Children.IsEmpty();
        public bool CanAddItem() => SelectedNode?.Type == PlotElementType.Category;
        #endregion

        #region PDBInitialization
        public PlotManagerWindow()
        {

            LoadCommands();
            InitializeComponent();

            var rootlist3 = new List<PlotElement>();
            var dictionary3 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE3);
            rootlist3.Add(dictionary3[1]);
            if (PlotDatabases.LoadDatabase(MEGame.LE3, false, AppDirectories.AppDataFolder))
            {
                var mods3 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE3, false);
                rootlist3.Add(mods3[100000]);
            }
            dictionary3.Add(0, new PlotElement(0, 0, "Legendary Edition - Mass Effect 3 Plots", PlotElementType.None, -1, rootlist3));
            RootNodes3.Add(dictionary3[0]);

            RootNodes2.ClearEx();
            var rootlist2 = new List<PlotElement>();
            var dictionary2 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE2);
            rootlist2.Add(dictionary2[1]);
            if (PlotDatabases.LoadDatabase(MEGame.LE2, false, AppDirectories.AppDataFolder))
            {
                var mods2 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE2, false);
                rootlist2.Add(mods2[100000]);
            }
            dictionary2.Add(0, new PlotElement(0, 0, "Legendary Edition - Mass Effect 2 Plots", PlotElementType.None, -1, rootlist2));
            RootNodes2.Add(dictionary2[0]);

            RootNodes1.ClearEx();
            var rootlist1 = new List<PlotElement>();
            var dictionary1 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE1);
            rootlist1.Add(dictionary1[1]);
            if (PlotDatabases.LoadDatabase(MEGame.LE1, false, AppDirectories.AppDataFolder))
            {
                var mods1 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE1, false);
                rootlist1.Add(mods1[100000]);
            }
            dictionary1.Add(0, new PlotElement(0, 0, "Legendary Edition - Mass Effect 1 Plots", PlotElementType.None, -1, rootlist1));
            RootNodes1.Add(dictionary1[0]);
            Focus();
            SelectedNode = dictionary3[1];
        }

        private void LoadCommands()
        {
            FilterCommand = new GenericCommand(Filter);
            CopyToClipboardCommand = new GenericCommand(CopyToClipboard);
            RefreshLocalCommand = new GenericCommand(RefreshTrees);
            SaveLocalCommand = new GenericCommand(SaveModDB);
            ExitCommand = new GenericCommand(Close);
            ClickOkCommand = new RelayCommand(AddDataToModDatabase);
            ClickCancelCommand = new RelayCommand(CancelAddData);
            AddNewModCommand = new GenericCommand(AddNewModData, IsModRoot);
            AddModCategoryCommand = new GenericCommand(AddNewModCatData, CanAddCategory);
            AddModItemCommand = new GenericCommand(AddNewModItemData, CanAddItem);
            DeleteModItemCommand = new GenericCommand(DeleteNewModData, CanDeleteItem);
        }
        private void PlotDB_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void PlotDB_Closing(object sender, CancelEventArgs e)
        {
            if (NeedsSave)
            {
                var dlg = MessageBox.Show("Changes have been made to the modding database. Save now?", "Plot Database", MessageBoxButton.YesNo);
                if (dlg == MessageBoxResult.Yes)
                {
                    NeedsSave = false;
                    CurrentGame = MEGame.LE3;
                    SaveModDB();
                    CurrentGame = MEGame.LE2;
                    SaveModDB();
                    CurrentGame = MEGame.LE1;
                    SaveModDB();
                }
            }
        }

        private void UpdateSelection()
        {
            if(updateTable)
            {
                ElementsTable.ClearEx();
                var temptable = new List<PlotElement>();
                AddPlotsToList(SelectedNode, temptable);
                ElementsTable.AddRange(temptable);
                Filter();
            }
        }

        private void RefreshTrees()
        {
            RootNodes3.ClearEx();
            var rootlist3 = new List<PlotElement>();
            var dictionary3 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE3);
            rootlist3.Add(dictionary3[1]);
            var mods3 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE3, false);
            rootlist3.Add(mods3[100000]);
            dictionary3.Add(0, new PlotElement(0, 0, "Legendary Edition - Mass Effect 3 Plots", PlotElementType.None, -1, rootlist3));
            RootNodes3.Add(dictionary3[0]);

            RootNodes2.ClearEx();
            var rootlist2 = new List<PlotElement>();
            var dictionary2 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE2);
            rootlist2.Add(dictionary2[1]);
            if (PlotDatabases.LoadDatabase(MEGame.LE2, false, AppDirectories.AppDataFolder))
            {
                var mods2 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE2, false);
                rootlist2.Add(mods2[100000]);
            }
            dictionary2.Add(0, new PlotElement(0, 0, "Legendary Edition - Mass Effect 2 Plots", PlotElementType.None, -1, rootlist2));
            RootNodes2.Add(dictionary2[0]);

            RootNodes1.ClearEx();
            var rootlist1 = new List<PlotElement>();
            var dictionary1 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE1);
            rootlist1.Add(dictionary1[1]);
            if (PlotDatabases.LoadDatabase(MEGame.LE1, false, AppDirectories.AppDataFolder))
            {
                var mods1 = PlotDatabases.GetMasterDictionaryForGame(MEGame.LE1, false);
                rootlist1.Add(mods1[100000]);
            }
            dictionary1.Add(0, new PlotElement(0, 0, "Legendary Edition - Mass Effect 1 Plots", PlotElementType.None, -1, rootlist1));
            RootNodes1.Add(dictionary1[0]);
        }

        private void AddPlotsToList(PlotElement plotElement, List<PlotElement> elementList)
        {
            if (plotElement != null)
            {
                switch (plotElement.Type)
                {
                    case PlotElementType.Plot:
                    case PlotElementType.Region:
                    case PlotElementType.FlagGroup:
                    case PlotElementType.None:
                        break;
                    default:
                        elementList.Add(plotElement);
                        break;
                }

                foreach (var c in plotElement.Children)
                {
                    AddPlotsToList(c, elementList);
                }
            }
        }

        private void Tree_BW3_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            updateTable = true;
            SelectedNode = Tree_BW3.SelectedItem as PlotElement;
        }

        private void Tree_BW2_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            updateTable = true;
            SelectedNode = Tree_BW2.SelectedItem as PlotElement;
        }

        private void Tree_BW1_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            updateTable = true;
            SelectedNode = Tree_BW1.SelectedItem as PlotElement;
        }
        private void LV_Plots_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            updateTable = false;
            SelectedNode = LV_Plots.SelectedItem as PlotElement;
        }
        private void NewTab_Selected(object sender, SelectionChangedEventArgs e)
        {
            switch (CurrentView)
            {
                case 1:
                    CurrentGame = MEGame.LE2;
                    break;
                case 2:
                    CurrentGame = MEGame.LE1;
                    break;
                default:
                    CurrentGame = MEGame.LE3;
                    break;
            }
        }


        #endregion

        #region Filter
        private void FilterBox_KeyUp(object sender, KeyEventArgs e)
        {
            Filter();
        }

        private void Filter()
        {
            LV_Plots.Items.Filter = PlotFilter;
        }

        private bool PlotFilter(object p)
        {
            bool showthis = true;
            var e = (PlotElement)p;
            var t = FilterBox.Text;
            if (!string.IsNullOrEmpty(t))
            {
                showthis = e.Path.ToLower().Contains(t.ToLower());
                if (!showthis)
                {
                    showthis = e.PlotId.ToString().Contains(t);
                }
            }
            if (showthis && !ShowBoolStates)
            {
                showthis = e.Type != PlotElementType.State && e.Type != PlotElementType.SubState && e.Type != PlotElementType.Flag && e.Type != PlotElementType.Consequence;
            }
            if (showthis && !ShowInts)
            {
                showthis = e.Type != PlotElementType.Integer;
            }
            if (showthis && !ShowFloats)
            {
                showthis = e.Type != PlotElementType.Float;
            }
            if (showthis && !ShowConditionals)
            {
                showthis = e.Type != PlotElementType.Conditional;
            }
            if (showthis && !ShowTransitions)
            {
                showthis = e.Type != PlotElementType.Transition;
            }
            if (showthis && !ShowJournal)
            {
                showthis = e.Type != PlotElementType.JournalGoal && e.Type != PlotElementType.JournalItem && e.Type != PlotElementType.JournalTask;
            }

            return showthis;
        }

        #endregion

        #region UserCommands
        private void CopyToClipboard()
        {
            var elmnt = (PlotElement)LV_Plots.SelectedItem;
            if (elmnt != null)
            {
                Clipboard.SetText(elmnt.PlotId.ToString());
            }
        }

        private void list_ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader headerClicked)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    ListSortDirection direction;
                    if (headerClicked != _lastHeaderClicked)
                    {
                        direction = ListSortDirection.Ascending;
                    }
                    else
                    {
                        if (_lastDirection == ListSortDirection.Ascending)
                        {
                            direction = ListSortDirection.Descending;
                        }
                        else
                        {
                            direction = ListSortDirection.Ascending;
                        }
                    }

                    string primarySort;
                    ICollectionView linedataView = CollectionViewSource.GetDefaultView(LV_Plots.ItemsSource);
                    primarySort = headerClicked.Column.Header.ToString();
                    linedataView.SortDescriptions.Clear();
                    switch (primarySort)
                    {
                        case "Plot":
                            primarySort = "PlotId";
                            break;
                        case "Icon":
                        case "Type":
                            primarySort = "Type"; //Want to sort on alphabetical not Enum
                            break;
                        default:

                            break;
                    }
                    linedataView.SortDescriptions.Add(new SortDescription(primarySort, direction));
                    linedataView.Refresh();
                    LV_Plots.ItemsSource = linedataView;

                    if (direction == ListSortDirection.Ascending)
                    {
                        headerClicked.Column.HeaderTemplate =
                          Resources["HeaderTemplateArrowUp"] as DataTemplate;
                    }
                    else
                    {
                        headerClicked.Column.HeaderTemplate =
                          Resources["HeaderTemplateArrowDown"] as DataTemplate;
                    }

                    // Remove arrow from previously sorted header
                    if (_lastHeaderClicked != null && _lastHeaderClicked != headerClicked)
                    {
                        _lastHeaderClicked.Column.HeaderTemplate = null;
                    }

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
            e.Handled = true;
        }

        private async void SaveModDB()
        {
            bwLink.Visibility = Visibility.Collapsed;
            var statustxt = CurrentOverallOperationText.ToString();
            CurrentOverallOperationText = "Saving...";
            switch (CurrentGame)
            {
                case MEGame.LE3:
                    PlotDatabases.Le3ModDatabase.SaveDatabaseToFile(AppDirectories.AppDataFolder);
                    CurrentOverallOperationText = "Saved LE3 Mod Database Locally...";
                    break;
                case MEGame.LE2:
                    PlotDatabases.Le2ModDatabase.SaveDatabaseToFile(AppDirectories.AppDataFolder);
                    CurrentOverallOperationText = "Saved LE2 Mod Database Locally...";
                    break;
                case MEGame.LE1:
                    PlotDatabases.Le1ModDatabase.SaveDatabaseToFile(AppDirectories.AppDataFolder);
                    CurrentOverallOperationText = "Saved LE1 Mod Database Locally...";
                    break;
            }
            if (NeedsSave) //don't do this on exit
            {
                await Task.Delay(TimeSpan.FromSeconds(1.0));
            }
            CurrentOverallOperationText = statustxt;
            bwLink.Visibility = Visibility.Visible;
            NeedsSave = false;
        }

        private void AddNewModData()
        {
            GameTab.IsEnabled = false;
            LV_Plots.IsEnabled = false;
            //TreeContent.Visibility = Visibility.Collapsed;
            NewModForm.Visibility = Visibility.Visible;
            NewModForm.Focus();
        }
        private void AddNewModCatData()
        {

            NewModForm.Visibility = Visibility.Visible;
        }

        private void AddNewModItemData()
        {
            TreeContent.Visibility = Visibility.Collapsed;
            NewModForm.Visibility = Visibility.Visible;
        }

        private void RevertPanelsToDefault()
        {
            GameTab.IsEnabled = true;
            LV_Plots.IsEnabled = true;
            //TreeContent.Visibility = Visibility.Visible;
            NewModForm.Visibility = Visibility.Collapsed;
        }
        private void CancelAddData(object obj)
        {
            var command = obj.ToString();
            switch (command)
            {
                case "NewMod":
                    NewModForm.Visibility = Visibility.Collapsed;
                    TreeContent.Visibility = Visibility.Visible;
                    nwMod_Name.Clear();
                    break;
                default:
                    break;
            }
        }

        private void AddDataToModDatabase(object obj)
        {
            if (SelectedNode != null)
            {
                var command = obj.ToString();
                var mdb = new PlotDatabase();
                switch (CurrentGame)
                {
                    case MEGame.LE1:
                        mdb = PlotDatabases.Le1ModDatabase;
                        break;
                    case MEGame.LE2:
                        mdb = PlotDatabases.Le2ModDatabase;
                        break;
                    default:
                        mdb = PlotDatabases.Le3ModDatabase;
                        break;
                }
                int newElementId = mdb.GetNextElementId();
                var parent = SelectedNode;
                switch (command)
                {
                    case "NewMod":
                        var modname = nwMod_Name.Text;
                        var dlg = MessageBox.Show($"Do you want to add mod {modname} to the database?", "Modding Plots Database", MessageBoxButton.OKCancel);
                        if (dlg == MessageBoxResult.Cancel || modname == null)
                        {
                            CancelAddData(command);
                            return;
                        }
                        int parentId = 100000;
                        parent = mdb.Organizational[parentId];
                        var mods = parent.Children.ToList();
                        foreach (var mod in mods)
                        {
                            if (mod.Label == modname)
                            {
                                MessageBox.Show($"Mod {modname} already exists in the database.  Please use another name.");
                                CancelAddData(command);
                                return;
                            }
                        }
                        var newModPE = new PlotElement(-1, newElementId, modname, PlotElementType.Mod, parentId, new List<PlotElement>(), parent);
                        mdb.Organizational.Add(newElementId, newModPE);
                        parent.Children.Add(newModPE);
                        NewModForm.Visibility = Visibility.Collapsed;
                        nwMod_Name.Clear();
                        TreeContent.Visibility = Visibility.Visible;
                        NeedsSave = true;
                        break;
                    case "NewCategory":
                        var parentCat = SelectedNode;
                        var nameCat = ""; //Add name from input
                        var newModCatPE = new PlotElement(-1, newElementId, nameCat, PlotElementType.Category, parentCat.ElementId, new List<PlotElement>(), parentCat);
                        parentCat.Children.Add(newModCatPE);
                        mdb.Organizational.Add(newElementId, newModCatPE);
                        NeedsSave = true;
                        break;
                    case "NewItem":
                        var nameItem = ""; //Add name from input
                        var type = PlotElementType.Conditional; //Add type from input.
                        int newPlotId = 100101;  // input plot
                        var newModItemPE = new PlotElement(newPlotId, newElementId, nameItem, type, parent.ElementId, new List<PlotElement>(), parent);
                        switch (type)
                        {
                            case PlotElementType.State:
                            case PlotElementType.SubState:
                                var newModBool = new PlotBool(newPlotId, newElementId, nameItem, type, parent.ElementId, new List<PlotElement>(), parent);
                                //subtype, gamervariable, achievementid, galaxyatwar
                                mdb.Bools.Add(newPlotId, newModBool);
                                parent.Children.Add(newModBool);
                                break;
                            case PlotElementType.Integer:
                                mdb.Ints.Add(newPlotId, newModItemPE);
                                parent.Children.Add(newModItemPE);
                                break;
                            case PlotElementType.Float:
                                mdb.Floats.Add(newPlotId, newModItemPE);
                                parent.Children.Add(newModItemPE);
                                break;
                            case PlotElementType.Conditional:
                                var newModCnd = new PlotConditional(newPlotId, newElementId, nameItem, type, parent.ElementId, new List<PlotElement>(), parent);
                                //code
                                mdb.Conditionals.Add(newPlotId, newModCnd);
                                parent.Children.Add(newModCnd);
                                break;
                            case PlotElementType.Transition:
                                var newModTrans = new PlotTransition(newPlotId, newElementId, nameItem, type, parent.ElementId, new List<PlotElement>(), parent);
                                //argument
                                mdb.Transitions.Add(newPlotId, newModTrans);
                                parent.Children.Add(newModTrans);
                                break;
                            default:   //PlotElementType.JournalGoal, PlotElementType.JournalItem, PlotElementType.JournalTask, PlotElementType.Consequence, PlotElementType.Flag
                                mdb.Organizational.Add(newElementId, newModItemPE);
                                parent.Children.Add(newModItemPE);
                                break;
                        }



                        NeedsSave = true;
                        break;
                    default:
                        break;
                }
            }


            RefreshTrees();
        }

        private void DeleteNewModData()
        {
            if (SelectedNode.ElementId <= 100000)
            {
                MessageBox.Show("Cannot Delete Bioware plot states.");
                return;
            }
            if (!SelectedNode.Children.IsEmpty())
            {
                MessageBox.Show("This item has subitems.  Delete all the sub-items before deleting this.");
                return;
            }
            var dlg = MessageBox.Show($"Are you sure you wish to delete this item?\nType: {SelectedNode.Type}\nPlotId: {SelectedNode.PlotId}\nPath: {SelectedNode.Path}", "Plot Database", MessageBoxButton.OKCancel);
            if (dlg == MessageBoxResult.Cancel)
                return;
            var deleteId = SelectedNode.ElementId;
            var mdb = new PlotDatabase();
            switch (CurrentGame)
            {
                case MEGame.LE1:
                    mdb = PlotDatabases.Le1ModDatabase;
                    break;
                case MEGame.LE2:
                    mdb = PlotDatabases.Le2ModDatabase;
                    break;
                default:
                    mdb = PlotDatabases.Le3ModDatabase;
                    break;
            }
            mdb.RemoveFromParent(SelectedNode);
            switch (SelectedNode.Type)
            {
                case PlotElementType.State:
                case PlotElementType.SubState:
                    mdb.Bools.Remove(SelectedNode.PlotId);
                    break;
                case PlotElementType.Integer:
                    mdb.Ints.Remove(SelectedNode.PlotId);
                    break;
                case PlotElementType.Float:
                    mdb.Floats.Remove(SelectedNode.PlotId);
                    break;
                case PlotElementType.Conditional:
                    mdb.Conditionals.Remove(SelectedNode.PlotId);
                    break;
                case PlotElementType.Transition:
                    mdb.Transitions.Remove(SelectedNode.PlotId);
                    break;
                default:
                    mdb.Organizational.Remove(SelectedNode.ElementId);
                    break;
            }
            NeedsSave = true;
            RefreshTrees();
        }

        #endregion


    }

    [ValueConversion(typeof(IEntry), typeof(string))]
    public class PlotElementTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PlotElementType elementType)
            {
                switch (elementType)
                {
                    case PlotElementType.Conditional:
                        return "/Tools/PlotDatabase/PlotTypeIcons/icon_cnd.png";
                    case PlotElementType.Consequence:
                    case PlotElementType.Flag:
                    case PlotElementType.State:
                    case PlotElementType.SubState:
                        return "/Tools/PlotDatabase/PlotTypeIcons/icon_bool.png";
                    case PlotElementType.Float:
                        return "/Tools/PlotDatabase/PlotTypeIcons/icon_float.png";
                    case PlotElementType.Integer:
                        return "/Tools/PlotDatabase/PlotTypeIcons/icon_int.png";
                    case PlotElementType.JournalGoal:
                    case PlotElementType.JournalItem:
                    case PlotElementType.JournalTask:
                        return "/Tools/PackageEditor/ExportIcons/icon_world.png";
                    case PlotElementType.FlagGroup:
                    case PlotElementType.None:
                    case PlotElementType.Plot:
                    case PlotElementType.Region:
                    case PlotElementType.Category:
                        return "/Tools/PackageEditor/ExportIcons/icon_package.png";
                    case PlotElementType.Transition:
                        return "/Tools/PackageEditor/ExportIcons/icon_function.png";
                    case PlotElementType.Mod:
                        return "/Tools/PackageEditor/ExportIcons/icon_package_fileroot.png";
                    default:
                        break;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {    // Don't need any convert back
            return null;
        }

    }
}
