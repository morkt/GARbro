using System.Windows;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for UpdateDialog.xaml
    /// </summary>
    public partial class UpdateDialog : Window
    {
        public UpdateDialog (GarUpdateInfo info, bool enable_release, bool enable_formats)
        {
            InitializeComponent ();
            this.ReleasePane.Visibility = enable_release ? Visibility.Visible : Visibility.Collapsed;
            this.FormatsPane.Visibility = enable_formats ? Visibility.Visible : Visibility.Collapsed;
            if (string.IsNullOrEmpty (info.ReleaseNotes))
                this.ReleaseNotes.Visibility = Visibility.Collapsed;
            this.DataContext = info;
        }

        private void Hyperlink_RequestNavigate (object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            if (App.NavigateUri (e.Uri))
                e.Handled = true;
        }

        private void Button_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
