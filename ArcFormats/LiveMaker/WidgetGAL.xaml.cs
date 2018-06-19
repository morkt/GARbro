using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using GameRes.Formats.LiveMaker;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetGAL.xaml
    /// </summary>
    public partial class WidgetGAL : StackPanel
    {
        public WidgetGAL (IDictionary<string, string> known_keys)
        {
            InitializeComponent();
            var first_item = new KeyValuePair<string, string> (arcStrings.ArcIgnoreEncryption, "");
            var items = new KeyValuePair<string, string>[] { first_item };
            this.Title.ItemsSource = items.Concat (known_keys.OrderBy (x => x.Key));
        }
    }
}
