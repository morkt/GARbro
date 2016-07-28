using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Properties;
using GameRes.Formats.Tactics;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetTactics.xaml
    /// </summary>
    public partial class WidgetTactics : StackPanel
    {
        public WidgetTactics()
        {
            InitializeComponent();
            Title.ItemsSource = Arc2Opener.KnownSchemes.OrderBy (x => x.Key);
        }
    }
}
