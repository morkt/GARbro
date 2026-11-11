using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using GameRes.Formats.DxLib;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Logika interakcji dla klasy WidgetDXA.xaml
    /// </summary>
    public partial class WidgetDXA : StackPanel
    {
        public WidgetDXA(IEnumerable titles)
        {
            InitializeComponent();
            this.DataContext = titles;
        }
    }
}
