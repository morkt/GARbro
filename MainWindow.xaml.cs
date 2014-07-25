// Game Resource Browser
//
// Copyright (C) 2014 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading;
using Microsoft.VisualBasic.FileIO;
using GARbro.GUI.Properties;
using GARbro.GUI.Strings;
using GameRes;
using Rnd.Windows;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private App m_app;

        const StringComparison StringIgnoreCase = StringComparison.CurrentCultureIgnoreCase;

        public MainWindow()
        {
            m_app = Application.Current as App;
            InitializeComponent();
            InitDirectoryChangesWatcher();
            InitPreviewPane();

            FormatCatalog.Instance.ParametersRequest += OnParametersRequest;

            CurrentDirectory.SizeChanged += (s, e) => {
                if (e.WidthChanged)
                {
                    pathLine.MinWidth = e.NewSize.Width-79;
                    this.MinWidth = e.NewSize.Width+65;
                }
            };
            pathLine.EnterKeyDown += acb_OnKeyDown;
        }

        void WindowLoaded (object sender, RoutedEventArgs e)
        {
            lv_SetSortMode (Settings.Default.lvSortColumn, Settings.Default.lvSortDirection);
            Dispatcher.InvokeAsync (WindowRendered, DispatcherPriority.ContextIdle);
        }

        void WindowRendered ()
        {
            ViewModel = CreateViewModel (m_app.InitPath);
            lv_SelectItem (0);
            SetStatusText (guiStrings.MsgReady);
        }

        /// <summary>
        /// Save settings when main window is about to close
        /// </summary>
        protected override void OnClosing (CancelEventArgs e)
        {
            SaveSettings();
            base.OnClosing (e);
        }

        /// <summary>
        /// Manually save settings that are not automatically saved by bindings.
        /// </summary>
        private void SaveSettings()
        {
            if (null != m_lvSortByColumn)
            {
                Settings.Default.lvSortColumn = m_lvSortByColumn.Tag.ToString();
                Settings.Default.lvSortDirection = m_lvSortDirection;
            }
            else
                Settings.Default.lvSortColumn = "";
        }

        /// <summary>
        /// Set status line text. Could be called from any thread.
        /// </summary>
        public void SetStatusText (string text)
        {
            Dispatcher.Invoke (() => { appStatusText.Text = text; });
        }

        /// <summary>
        /// Popup error message box. Could be called from any thread.
        /// </summary>
        public void PopupError (string message, string title)
        {
            Dispatcher.Invoke (() => MessageBox.Show (this, message, title, MessageBoxButton.OK, MessageBoxImage.Error));
        }

        /// <summary>
        /// Set data context of the ListView.
        /// </summary>

        private DirectoryViewModel ViewModel
        {
            get
            {
                var source = CurrentDirectory.ItemsSource as CollectionView;
                if (null == source)
                    return null;
                return source.SourceCollection as DirectoryViewModel;
            }
            set
            {
                StopWatchDirectoryChanges();
                var cvs = this.Resources["ListViewSource"] as CollectionViewSource;
                cvs.Source = value;
                pathLine.Text = value.Path;
                if (m_lvSortByColumn != null)
                    lv_Sort (m_lvSortByColumn.Tag.ToString(), m_lvSortDirection);
                else
                    lv_Sort (null, m_lvSortDirection);
                if (!value.IsArchive && !string.IsNullOrEmpty (value.Path))
                {
                    Directory.SetCurrentDirectory (value.Path);
                    WatchDirectoryChanges (value.Path);
                }
                CurrentDirectory.UpdateLayout();
            }
        }

        DirectoryViewModel GetNewViewModel (string path)
        {
            path = Path.GetFullPath (path);
            if (Directory.Exists (path))
            {
                return new DirectoryViewModel (path, m_app.GetDirectoryList (path));
            }
            else
            {
                SetBusyState();
                return new ArchiveViewModel (path, m_app.GetArchive (path));
            }
        }

        public void SetBusyState()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            Dispatcher.InvokeAsync (() => {
                Mouse.OverrideCursor = null;
            }, DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Create view model corresponding to <paramref name="path">. Returns null on error.
        /// </summary>
        DirectoryViewModel TryCreateViewModel (string path)
        {
            try
            {
                return GetNewViewModel (path);
            }
            catch (Exception X)
            {
                SetStatusText (string.Format ("{0}: {1}", Path.GetFileName (path), X.Message));
                return null;
            }
        }

        /// <summary>
        /// Create view model corresponding to <paramref name="path"> or empty view model if there was
        /// an error accessing path.
        /// </summary>
        DirectoryViewModel CreateViewModel (string path)
        {
            try
            {
                return GetNewViewModel (path);
            }
            catch (Exception X)
            {
                PopupError (X.Message, guiStrings.MsgErrorOpening);
                return new DirectoryViewModel ("", new Entry[0]);
            }
        }

        #region Refresh view on filesystem changes

        private FileSystemWatcher m_watcher = new FileSystemWatcher();

        void InitDirectoryChangesWatcher ()
        {
            m_watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            m_watcher.Changed += InvokeRefreshView;
            m_watcher.Created += InvokeRefreshView;
            m_watcher.Deleted += InvokeRefreshView;
            m_watcher.Renamed += InvokeRefreshView;
        }

        void WatchDirectoryChanges (string path)
        {
            m_watcher.Path = path;
            m_watcher.EnableRaisingEvents = true;
        }

        void StopWatchDirectoryChanges()
        {
            m_watcher.EnableRaisingEvents = false;
        }

        private void InvokeRefreshView (object source, FileSystemEventArgs e)
        {
            var watcher = source as FileSystemWatcher;
            if (watcher.Path == ViewModel.Path)
            {
                watcher.EnableRaisingEvents = false;
                Dispatcher.Invoke (RefreshView, DispatcherPriority.Send, CancellationToken.None,
                                   TimeSpan.FromMilliseconds (100));
            }
        }
        #endregion

        /// <summary>
        /// Select specified item within CurrentDirectory and bring it into a view.
        /// </summary>

        void lv_SelectItem (EntryViewModel item)
        {
            if (item != null)
            {
                CurrentDirectory.SelectedItem = item;
                CurrentDirectory.ScrollIntoView (item);
                var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item);
                if (lvi != null)
                    lvi.Focus();
            }
        }

        void lv_SelectItem (int index)
        {
            CurrentDirectory.SelectedIndex = index;
            CurrentDirectory.ScrollIntoView (CurrentDirectory.SelectedItem);
            var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromIndex (index);
            if (lvi != null)
                lvi.Focus();
        }

        void lv_SelectItem (string name)
        {
            if (!string.IsNullOrEmpty (name))
                lv_SelectItem (ViewModel.Find (name));
        }

        private void lv_Focus ()
        {
            if (CurrentDirectory.SelectedIndex != -1)
            {
                var item = CurrentDirectory.SelectedItem;
                var lvi = CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item) as ListViewItem;
                if (lvi != null)
                {
                    lvi.Focus();
                    return;
                }
            }
            CurrentDirectory.Focus();
        }

        void lvi_Selected (object sender, RoutedEventArgs args)
        {
            var lvi = sender as ListViewItem;
            if (lvi == null)
                return;
            var entry = lvi.Content as EntryViewModel;
            if (entry == null)
                return;
            PreviewEntry (entry.Source);
        }

        void lvi_DoubleClick (object sender, MouseButtonEventArgs args)
        {
            var lvi = sender as ListViewItem;
            if (Commands.OpenItem.CanExecute (null, lvi))
            {
                Commands.OpenItem.Execute (null, lvi);
                args.Handled = true;
            }
        }

        /// <summary>
        /// Get currently selected item from ListView widget.
        /// </summary>
        private ListViewItem lv_GetCurrentContainer ()
        {
            int current = CurrentDirectory.SelectedIndex;
            if (-1 == current)
                 return null;

            return CurrentDirectory.ItemContainerGenerator.ContainerFromIndex (current) as ListViewItem;
        }

        GridViewColumnHeader    m_lvSortByColumn = null;
        ListSortDirection       m_lvSortDirection = ListSortDirection.Ascending;

        public bool IsSortByName {
            get { return m_lvSortByColumn != null && "Name".Equals (m_lvSortByColumn.Tag); }
        }
        public bool IsSortByType {
            get { return m_lvSortByColumn != null && "Type".Equals (m_lvSortByColumn.Tag); }
        }
        public bool IsSortBySize {
            get { return m_lvSortByColumn != null && "Size".Equals (m_lvSortByColumn.Tag); }
        }
        public bool IsUnsorted { get { return m_lvSortByColumn == null; } }

        void lv_SetSortMode (string sortBy, ListSortDirection direction)
        {
            m_lvSortByColumn = null;
            GridView view = CurrentDirectory.View as GridView;
            foreach (var column in view.Columns)
            {
                var header = column.Header as GridViewColumnHeader;
                if (null != header && !string.IsNullOrEmpty (sortBy) && sortBy.Equals (header.Tag))
                {
                    if (ListSortDirection.Ascending == direction)
                        column.HeaderTemplate = Resources["SortArrowUp"] as DataTemplate;
                    else
                        column.HeaderTemplate = Resources["SortArrowDown"] as DataTemplate;
                    m_lvSortByColumn = header;
                    m_lvSortDirection = direction;
                }
                else
                {
                    column.HeaderTemplate = Resources["SortArrowNone"] as DataTemplate;
                }
            }
        }

        private void lv_Sort (string sortBy, ListSortDirection direction)
        {
            var dataView = CollectionViewSource.GetDefaultView (CurrentDirectory.ItemsSource) as ListCollectionView;
            dataView.CustomSort = new FileSystemComparer (sortBy, direction);
            /*
            using (dataView.DeferRefresh())
            {
                dataView.SortDescriptions.Clear();
                dataView.SortDescriptions.Add (new SortDescription ("Priority", ListSortDirection.Ascending));
                if (!string.IsNullOrEmpty (sortBy))
                {
                    dataView.SortDescriptions.Add (new SortDescription (sortBy, direction));
                    if (sortBy != "Name")
                        dataView.SortDescriptions.Add (new SortDescription ("Name", ListSortDirection.Ascending));
                }
            }
            */
        }

        /// <summary>
        /// Sort Listview by columns
        /// </summary>
        void lv_ColumnHeaderClicked (object sender, RoutedEventArgs e)
        {
            var headerClicked = e.OriginalSource as GridViewColumnHeader;

            if (null == headerClicked)
                return;
            if (headerClicked.Role == GridViewColumnHeaderRole.Padding)
                return;

            ListSortDirection direction;
            if (headerClicked != m_lvSortByColumn)
                direction = ListSortDirection.Ascending;
            else if (m_lvSortDirection == ListSortDirection.Ascending)
                direction = ListSortDirection.Descending;
            else
                direction = ListSortDirection.Ascending;

            string sortBy = headerClicked.Tag.ToString();
            lv_Sort (sortBy, direction);

            // Remove arrow from previously sorted header 
            if (m_lvSortByColumn != null && m_lvSortByColumn != headerClicked)
            {
                m_lvSortByColumn.Column.HeaderTemplate = Resources["SortArrowNone"] as DataTemplate;
            }

            if (ListSortDirection.Ascending == direction)
            {
                headerClicked.Column.HeaderTemplate = Resources["SortArrowUp"] as DataTemplate;
            }
            else
            {
                headerClicked.Column.HeaderTemplate = Resources["SortArrowDown"] as DataTemplate;
            }
            m_lvSortByColumn = headerClicked;
            m_lvSortDirection = direction;
        }

        /// <summary>
        /// Handle "Sort By" commands.
        /// </summary>

        private void SortByExec (object sender, ExecutedRoutedEventArgs e)
        {
            string sort_by = e.Parameter as string;
            lv_Sort (sort_by, ListSortDirection.Ascending);
            lv_SetSortMode (sort_by, ListSortDirection.Ascending);
        }

        /// <summary>
        /// Event handler for keys pressed in the right pane
        /// </summary>

        private void lv_TextInput (object sender, TextCompositionEventArgs e)
        {
            LookupItem (e.Text);
            e.Handled = true;
        }

        /// <summary>
        /// Lookup item in listview pane by first letter.
        /// </summary>

        private void LookupItem (string key)
        {
            if (string.IsNullOrEmpty (key))
                return;
            var source = CurrentDirectory.ItemsSource as CollectionView;
            if (source == null)
                return;

            var current = CurrentDirectory.SelectedItem as EntryViewModel;
            int index = 0;
            if (current != null && current.Name.StartsWith (key, StringIgnoreCase))
                index = CurrentDirectory.SelectedIndex+1;

            for (int i = index, count = source.Count; i < count; ++i)
            {
                var entry = source.GetItemAt (i) as EntryViewModel;
                if (entry != null && entry.Name.StartsWith (key, StringIgnoreCase))
                {
                    lv_SelectItem (entry);
                    break;
                }
            }
        }

        private void acb_OnKeyDown (object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Return)
                return;
            string path = (sender as AutoCompleteBox).Text;
            if (string.IsNullOrEmpty (path))
                return;
            try
            {
                ViewModel = GetNewViewModel (path);
                lv_Focus();
            }
            catch (Exception X)
            {
                PopupError (X.Message, guiStrings.MsgErrorOpening);
            }
        }

        #region Navigation history implementation

        private string CurrentPath { get { return ViewModel.Path; } }

        HistoryStack<DirectoryPosition> m_history = new HistoryStack<DirectoryPosition>();

        DirectoryPosition GetCurrentPosition ()
        {
            var evm = CurrentDirectory.SelectedItem as EntryViewModel;
            return new DirectoryPosition (ViewModel, evm);
        }

        bool SetCurrentPosition (DirectoryPosition pos)
        {
            var vm = TryCreateViewModel (pos.Path);
            if (null == vm)
                return false;
            try
            {
                vm.SetPosition (pos);
                ViewModel = vm;
                if (null != pos.Item)
                    lv_SelectItem (pos.Item.Name);
                return true;
            }
            catch (Exception X)
            {
                SetStatusText (X.Message);
                return false;
            }
        }

        private void SaveCurrentPosition ()
        {
            m_history.Push (GetCurrentPosition());
        }

        private void GoBackExec (object sender, ExecutedRoutedEventArgs e)
        {
            DirectoryPosition current = m_history.Undo (GetCurrentPosition());
            if (current != null)
                SetCurrentPosition (current);
        }

        private void GoForwardExec (object sender, ExecutedRoutedEventArgs e)
        {
            DirectoryPosition current = m_history.Redo (GetCurrentPosition());
            if (current != null)
                SetCurrentPosition (current);
        }

        private void CanExecuteGoBack (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = m_history.CanUndo();
        }

        private void CanExecuteGoForward (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = m_history.CanRedo();
        }
        #endregion

        /// <summary>
        /// Open file/directory.
        /// </summary>
        private void OpenItemExec (object control, ExecutedRoutedEventArgs e)
        {
            EntryViewModel entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null == entry)
            {
                var lvi = e.OriginalSource as ListViewItem;
                if (lvi != null)
                    entry = lvi.Content as EntryViewModel;
            }
            if (null == entry)
                return;

            var vm = ViewModel;
            if (null == vm)
                return;
            if (vm.IsArchive) // tried to open file inside archive
            {
                var arc_vm = vm as ArchiveViewModel;
                if (!("" == arc_vm.SubDir && ".." == entry.Name))
                {
                    OpenArchiveEntry (arc_vm, entry);
                    return;
                }
            }
            OpenDirectoryEntry (vm, entry);
        }

        private void OpenDirectoryEntry (DirectoryViewModel vm, EntryViewModel entry)
        {
            string old_dir = vm.Path;
            string new_dir = Path.Combine (old_dir, entry.Name);
            Trace.WriteLine (new_dir, "OpenDirectoryEntry");
            vm = TryCreateViewModel (new_dir);
            if (null == vm)
            {
                if (entry.Type != "archive")
                    SystemOpen (new_dir);
                return;
            }
            SaveCurrentPosition();
            ViewModel = vm;
            if (vm.IsArchive && null != m_app.CurrentArchive)
                SetStatusText (m_app.CurrentArchive.Description);
            else
                SetStatusText ("");
            var old_parent = Directory.GetParent (old_dir);
            if (null != old_parent && vm.Path == old_parent.FullName)
            {
                lv_SelectItem (Path.GetFileName (old_dir));
            }
            else
            {
                lv_SelectItem (0);
            }
        }

        private void OpenArchiveEntry (ArchiveViewModel vm, EntryViewModel entry)
        {
            if (entry.IsDirectory)
            {
                SaveCurrentPosition();
                var old_dir = vm.SubDir;
                try
                {
                    vm.ChDir (entry.Name);
                    if (".." == entry.Name)
                        lv_SelectItem (Path.GetFileName (old_dir));
                    else
                        lv_SelectItem (0);
                    SetStatusText ("");
                }
                catch (Exception X)
                {
                    SetStatusText (X.Message);
                }
            }
        }

        /// <summary>
        /// Launch specified file.
        /// </summary>
        private void SystemOpen (string file)
        {
            try
            {
                Process.Start (file);
            }
            catch (Exception X)
            {
                SetStatusText (X.Message);
            }
        }

        /// <summary>
        /// Refresh current view.
        /// </summary>
        private void RefreshExec (object sender, ExecutedRoutedEventArgs e)
        {
            RefreshView();
        }

        private void RefreshView ()
        {
            m_app.ResetCache();
            var pos = GetCurrentPosition();
            SetCurrentPosition (pos);
        }

        /// <summary>
        /// Open current file in Explorer.
        /// </summary>

        private void ExploreItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (entry != null && !ViewModel.IsArchive)
            {
                try
                {
                    string name = Path.Combine (CurrentPath, entry.Name);
                    Process.Start ("explorer.exe", "/select,"+name);
                }
                catch (Exception X)
                {
                    // ignore
                    Trace.WriteLine (X.Message, "explorer.exe");
                }
            }
        }

        /// <summary>
        /// Delete item from both media library and disk drive.
        /// </summary>
        private void DeleteItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (entry == null)
                return;

            this.IsEnabled = false;
            try
            {
                m_app.ResetCache();
                string item_name = Path.Combine (CurrentPath, entry.Name);
                Trace.WriteLine (item_name, "DeleteItemExec");
                FileSystem.DeleteFile (item_name, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
                DeleteItem (lv_GetCurrentContainer());
                SetStatusText (string.Format(guiStrings.MsgDeletedItem, item_name));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception X)
            {
                SetStatusText (X.Message);
            }
            finally
            {
                this.IsEnabled = true;
            }
        }

        /// <summary>
        /// Delete item at the specified position within ListView, correctly adjusting current
        /// position.
        /// </summary>
        private void DeleteItem (ListViewItem item)
        {
            int i = CurrentDirectory.SelectedIndex;
            int next = -1;
            if (i+1 < CurrentDirectory.Items.Count)
                next = i + 1;
            else if (i > 0)
                next = i - 1;

            if (next != -1)
                CurrentDirectory.SelectedIndex = next;

            var entry = item.Content as EntryViewModel;
            if (entry != null)
            {
                ViewModel.Remove (entry);
            }
        }

        /// <summary>
        /// Rename selected item.
        /// </summary>
        private void RenameItemExec(object sender, ExecutedRoutedEventArgs e)
        {
            RenameElement (lv_GetCurrentContainer());
        }

        /// <summary>
        /// Rename item contained within specified framework control.
        /// </summary>
        void RenameElement (ListViewItem  item)
        {
            if (item == null)
                return;
/*
            TextBlock block = FindByName (item, "item_Text") as TextBlock;
            TextBox box = FindSibling (block, "item_Input") as TextBox;

            if (block == null || box == null)
                return;

            IsRenameActive = true;

            block.Visibility = Visibility.Collapsed;
            box.Text = block.Text;
            box.Visibility = Visibility.Visible;
            box.Select (0, box.Text.Length);
            box.Focus();
*/
        }

        /// <summary>
        /// Handle "Exit" command.
        /// </summary>
        void ExitExec (object sender, ExecutedRoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
 
        private void MenuAbout_Click (object sender, RoutedEventArgs e)
        {
            var about = new AboutBox();
            about.Owner = this;
            about.ShowDialog();
        }

        private void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void CanExecuteControlCommand (object sender, CanExecuteRoutedEventArgs e)
        {
            Control target = e.Source as Control;
            e.CanExecute = target != null;
        }

        private void CanExecuteOnSelected (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CurrentDirectory.SelectedIndex != -1;
        }

        private void CanExecuteInArchive (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ViewModel.IsArchive && CurrentDirectory.SelectedIndex != -1;
        }

        private void CanExecuteCreateArchive (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ViewModel.IsArchive && CurrentDirectory.SelectedItems.Count > 0;
        }

        private void CanExecuteInDirectory (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ViewModel.IsArchive;
        }

        private void CanExecuteDelete (object sender, CanExecuteRoutedEventArgs e)
        {
            if (!ViewModel.IsArchive && CurrentDirectory.SelectedIndex != -1)
            {
                var entry = CurrentDirectory.SelectedItem as EntryViewModel;
                if (entry != null && !entry.IsDirectory)
                {
                    e.CanExecute = true;
                    return;
                }
            }
            e.CanExecute = false;
        }

        private void OnParametersRequest (object sender, ParametersRequestEventArgs e)
        {
            var control = e.InputWidget as UIElement;
            if (null != control)
            {
                var param_dialog = new ArcParametersDialog (control, e.Notice);
                param_dialog.Owner = this;
                e.InputResult = param_dialog.ShowDialog() ?? false;
            }
        }
    }

    /// <summary>
    /// TextBox that uses filesystem as source for autocomplete.
    /// </summary>
    public class ExtAutoCompleteBox : AutoCompleteBox
    {
        public delegate void EnterKeyDownEvent (object sender, KeyEventArgs e);
        public event EnterKeyDownEvent EnterKeyDown;
        /*
        public event EnterKeyDownEvent EnterKeyDown
        {
            add { AddHandler (KeyEvent, value); }
            remove { RemoveHandler (KeyEvent, value); }
        }
        public static readonly RoutedEvent EnterKeyEvent = EventManager.RegisterRoutedEvent(
            "EnterKeyDown", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ExtAutoCompleteBox));
        */

        protected override void OnKeyDown (KeyEventArgs e)
        {
            base.OnKeyDown (e);
            if (e.Key == Key.Enter)
                RaiseEnterKeyDownEvent (e);
        }

        private void RaiseEnterKeyDownEvent (KeyEventArgs e)
        {
            if (EnterKeyDown != null)
                EnterKeyDown (this, e);

//            var event_args = new RoutedEventArgs (ExtCompleteBox.EnterKeyEvent);
//            RaiseEvent (event_args);
        }

        protected override void OnPopulating (PopulatingEventArgs e)
        {
            var candidates = new List<string>();
            string dirname = Path.GetDirectoryName (this.Text);
            if (!string.IsNullOrEmpty (this.Text) && Directory.Exists (dirname))
            {
                foreach (var dir in Directory.GetDirectories (dirname))
                {
                    if (dir.StartsWith (dirname, StringComparison.CurrentCultureIgnoreCase))
                        candidates.Add (dir);
                }
            }
            this.ItemsSource = candidates;
            base.OnPopulating (e);
        }
    }

    public class BooleanToCollapsedVisibilityConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            //reverse conversion (false=>Visible, true=>collapsed) on any given parameter
            bool input = (null == parameter) ? (bool)value : !((bool)value);
            return (input) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    public static class Commands
    {
        public static readonly RoutedCommand OpenItem = new RoutedCommand();
        public static readonly RoutedCommand ExtractItem = new RoutedCommand();
        public static readonly RoutedCommand CreateArchive = new RoutedCommand();
        public static readonly RoutedCommand SortBy = new RoutedCommand();
        public static readonly RoutedCommand Exit = new RoutedCommand();
        public static readonly RoutedCommand GoBack = new RoutedCommand();
        public static readonly RoutedCommand GoForward = new RoutedCommand();
        public static readonly RoutedCommand DeleteItem = new RoutedCommand();
        public static readonly RoutedCommand RenameItem = new RoutedCommand();
        public static readonly RoutedCommand ExploreItem = new RoutedCommand();
        public static readonly RoutedCommand Refresh = new RoutedCommand();
        public static readonly RoutedCommand Browse = new RoutedCommand();
    }
}
