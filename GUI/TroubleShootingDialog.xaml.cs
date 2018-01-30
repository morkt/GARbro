using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Navigation;
using GameRes;
using Microsoft.Win32;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for TroubleShooting.xaml
    /// </summary>
    public partial class TroubleShootingDialog : Window
    {
        public TroubleShootingDialog ()
        {
            InitializeComponent();

            this.EnvironmentInfo.Text = GetEnvironmentReportText();
        }

        private void Hyperlink_RequestNavigate (object sender, RequestNavigateEventArgs e)
        {
            if (App.NavigateUri (e.Uri))
                e.Handled = true;
        }

        private void Button_Copy (object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText (this.EnvironmentInfo.Text);
            }
            catch (Exception X)
            {
                System.Diagnostics.Trace.WriteLine (X.Message, "Clipboard error");
            }
        }

        internal static string GetEnvironmentReportText ()
        {
            var gui = Assembly.GetExecutingAssembly();
            var gui_path = Path.GetDirectoryName (gui.Location);
            var report = new StringBuilder();
            report.AppendFormat ("OS: {0}\n", GetOSVersion());
            report.AppendFormat ("Framework version: {0}\n", Environment.Version);
            report.AppendFormat ("Framework release: {0}\n", GetFrameWorkReleaseInfo());
            report.AppendFormat ("{0}: {1}\n", App.Name, gui.GetName().Version);
            report.AppendFormat ("Formats database version: {0}\n", FormatCatalog.Instance.CurrentSchemeVersion);
            try
            {
                report.Append ("\nLoaded assemblies:\n");
                var local_assemblies = AppDomain.CurrentDomain.GetAssemblies().Where (a => !a.IsDynamic && a.Location.StartsWith (gui_path));
                foreach (var assembly in local_assemblies.Select (a => a.GetName()))
                {
                    report.AppendFormat ("{0} {1}\n", assembly.Name, assembly.Version);
                }
            }
            catch (Exception X)
            {
                report.AppendFormat ("Assemblies enumeration failed:\n{0}", X.Message);
            }
            return report.ToString();
        }

        internal static string GetOSVersion ()
        {
            string id = Registry.GetValue (@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString();
            if (string.IsNullOrEmpty (id))
                id = Environment.OSVersion.VersionString;
            else
            {
                string sp = Environment.OSVersion.ServicePack;
                if (!string.IsNullOrEmpty (sp))
                    id += ' '+sp;
            }
            return id;
        }

        static readonly SortedDictionary<int, string> FrameworkReleases = new SortedDictionary<int, string> {
            { 378389, "4.5" },
            { 378675, "4.5.1 from Windows 8.1" },
            { 378758, "4.5.1" },
            { 379893, "4.5.2" },
            { 393295, "4.6 from Windows 10" },
            { 393297, "4.6" },
            { 394254, "4.6.1 from Windows 10" },
            { 394271, "4.6.1" },
            { 394802, "4.6.2 from Windows 10 Anniversary Update" },
            { 394806, "4.6.2" },
            { 460798, "4.7 from Windows 10 Creators Update" },
            { 460805, "4.7" },
            { 461308, "4.7.1 from Windows 10 Fall Creators Update" },
            { 461310, "4.7.1+" },
        };

        internal static string GetFrameWorkReleaseInfo ()
        {
            int release = Convert.ToInt32 (Registry.GetValue (@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", 0));
            if (0 == release)
                return "Unknown";
            var version = FrameworkReleases.Reverse().Where (r => release >= r.Key).Select (r => r.Value).FirstOrDefault();
            if (string.IsNullOrEmpty (version))
                version = release.ToString();
            else
                version = string.Format ("{0} ({1})", release, version);
            return version;
        }
    }
}
