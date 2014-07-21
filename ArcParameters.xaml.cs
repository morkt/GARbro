using System.Windows;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for ArcParameters.xaml
    /// </summary>
    public partial class ArcParametersDialog : Window
    {
        public ArcParametersDialog (UIElement widget, string notice)
        {
            InitializeComponent();
            this.WidgetPane.Children.Add (widget);
            this.Notice.Text = notice;
        }

        private void Button_Click (object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
