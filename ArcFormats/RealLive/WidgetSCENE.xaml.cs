using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.Properties;
using GameRes.Formats.RealLive;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetSCENE.xaml
    /// </summary>
    public partial class WidgetSCENE : Grid
    {
        public WidgetSCENE ()
        {
            InitializeComponent();
            var guess = new string[] { arcStrings.YPFTryGuess };
            Title.ItemsSource = guess.Concat (SceneOpener.KnownKeys.Keys.OrderBy (x => x));
            if (-1 == Title.SelectedIndex)
                Title.SelectedIndex = 0;
        }
    }
}
