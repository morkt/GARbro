/// Game Resource browser
//
// Copyright (C) 2018 by morkt
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GameRes;
using GARbro.GUI.Strings;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow ()
        {
            InitializeComponent();

            this.DataContext = this.ViewModel = CreateSettingsTree();
        }

        SettingsViewModel ViewModel;

        static string LastSelectedSection = null;

        private void OnSectionChanged (object sender, System.Windows.RoutedEventArgs e)
        {
            this.SettingsPane.Child = null;
            var section = SectionsPane.SelectedValue as SettingsSectionView;
            if (section != null && section.Panel != null)
                this.SettingsPane.Child = section.Panel;
        }

        private void Button_ClickApply (object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyChanges();
        }

        private void Button_ClickOk (object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyChanges();
            DialogResult = true;
            var section = SectionsPane.SelectedItem as SettingsSectionView;
            if (section != null)
                LastSelectedSection = section.Label;
        }

        private void ApplyChanges ()
        {
            if (!ViewModel.HasChanges)
                return;
            if (OnApplyChanges != null)
                OnApplyChanges (this, new EventArgs());
            ViewModel.HasChanges = false;
        }

        private SettingsViewModel CreateSettingsTree ()
        {
            SettingsSectionView[] list = {
                new SettingsSectionView {
                    Label = guiStrings.TextFormats,
                    Children = EnumerateFormatsSettings(),
                    Panel = (UIElement)this.Resources["FormatsPanel"]
                },
            };
            SettingsSectionView selected_section = null;
            if (LastSelectedSection != null)
                selected_section = EnumerateSections (list).FirstOrDefault (s => s.Label == LastSelectedSection);
            if (null == selected_section)
                selected_section = list[0];
            selected_section.IsSelected = true;
            return new SettingsViewModel { Root = list };
        }

        IEnumerable<SettingsSectionView> EnumerateFormatsSettings ()
        {
            var list = new List<SettingsSectionView>();
            foreach (var format in FormatCatalog.Instance.Formats.Where (f => f.Settings != null && f.Settings.Any()))
            {
                var pane = new WrapPanel();
                foreach (var setting in format.Settings)
                {
                    if (setting.Value is bool)
                    {
                        var view = new ResourceSettingView<bool> (setting);
                        view.ValueChanged   += (s, e) => ViewModel.HasChanges = true;
                        this.OnApplyChanges += (s, e) => view.Apply();

                        var check_box = new CheckBox {
                            Template = (ControlTemplate)this.Resources["BoundCheckBox"],
                            DataContext = view,
                        };
                        pane.Children.Add (check_box);
                    }
                }
                if (pane.Children.Count > 0)
                {
                    var section = new SettingsSectionView {
                        Label = format.Tag,
                        SectionTitle = guiStrings.TextFormats+" :: "+format.Tag,
                        Panel = pane
                    };
                    list.Add (section);
                }
            }
            return list;
        }

        static IEnumerable<SettingsSectionView> EnumerateSections (IEnumerable<SettingsSectionView> list)
        {
            foreach (var section in list)
            {
                yield return section;
                if (section.Children != null)
                {
                    foreach (var child in EnumerateSections (section.Children))
                        yield return child;
                }
            }
        }

        private void tvi_MouseRightButtonDown (object sender, MouseButtonEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item != null && e.RightButton == MouseButtonState.Pressed)
            {
                item.Focus();
                item.IsSelected = true;
                e.Handled = true;
            }
        }

        public delegate void ApplyEventHandler (object sender, EventArgs e);

        public event ApplyEventHandler OnApplyChanges;
    }

    public class SettingsViewModel : INotifyPropertyChanged
    {
        public IEnumerable<SettingsSectionView> Root { get; set; }

        bool    m_has_changes;
        public bool HasChanges {
            get { return m_has_changes; }
            set {
                if (value != m_has_changes)
                {
                    m_has_changes = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged ([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged (this, new PropertyChangedEventArgs (propertyName));
            }
        }
    }

    public class SettingsSectionView
    {
        public string        Label { get; set; }
        public bool     IsSelected { get; set; }
        public UIElement     Panel { get; set; }

        string m_title;
        public string SectionTitle {
            get { return m_title ?? Label; }
            set { m_title = value; }
        }

        public IEnumerable<SettingsSectionView> Children { get; set; }
    }

    public class ResourceSettingView<TValue>
    {
        public IResourceSetting Source { get; private set; }
        public bool          IsChanged { get; private set; }
        public string             Text { get { return Source.Text; } }
        public string      Description { get { return Source.Description; } }

        TValue m_value;
        public TValue Value {
            get { return m_value; }
            set {
                if (!EqualityComparer<TValue>.Default.Equals (m_value, value))
                {
                    m_value = value;
                    IsChanged = true;
                    OnValueChanged();
                }
            }
        }

        public ResourceSettingView (IResourceSetting src)
        {
            Source = src;
            m_value = (TValue)src.Value;
        }

        public void Apply ()
        {
            if (IsChanged)
            {
                Source.Value = m_value;
                IsChanged = false;
            }
        }

        public event PropertyChangedEventHandler ValueChanged;

        void OnValueChanged ()
        {
            if (ValueChanged != null)
            {
                ValueChanged (this, new PropertyChangedEventArgs ("Value"));
            }
        }
    }

    public static class TreeViewItemExtensions
    {
        /// <returns>Depth of the given TreeViewItem</returns>
        public static int GetDepth (this TreeViewItem item)
        {
            var tvi = item.GetParent() as TreeViewItem;
            if (tvi != null)
                return tvi.GetDepth() + 1;
            return 0;
        }

        /// <returns>Control that contains specified TreeViewItem
        /// (either TreeView or another TreeViewItem).</returns>
        public static ItemsControl GetParent (this TreeViewItem item)
        {
            return ItemsControl.ItemsControlFromItemContainer (item);
        }
    }

    public class LeftMarginMultiplierConverter : IValueConverter
    {
        public double Length { get; set; }

        public object Convert (object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var item = value as TreeViewItem;
            if (item == null)
                return new Thickness(0);
            double thickness = Length * item.GetDepth();

            return new Thickness (thickness, 0, 0, 0);
        }

        public object ConvertBack (object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotImplementedException();
        }
    }
}
