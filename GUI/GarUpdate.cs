//! \file       GarUpdate.cs
//! \date       Tue Feb 14 00:02:14 2017
//! \brief      Application update routines.
//
// Copyright (C) 2017 by morkt
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
using System.Net;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using GameRes;
using GARbro.GUI.Strings;
using System.IO;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        GarUpdate m_updater;

        private void InitUpdatesChecker ()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var update_url = App.Resources["UpdateUrl"] as Uri;
            m_updater = new GarUpdate (this, update_url);
            m_updater.CanExecuteChanged += (s, e) => CommandManager.InvalidateRequerySuggested();
        }

        public void CanExecuteUpdate (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = m_updater.CanExecute (e.Parameter);
        }

        /// <summary>
        /// Handle "Check for updates" command.
        /// </summary>
        private void CheckUpdatesExec (object sender, ExecutedRoutedEventArgs e)
        {
            m_updater.Execute (e.Parameter);
        }
    }

    public class GarUpdateInfo
    {
        public Version  ReleaseVersion { get; set; }
        public Uri          ReleaseUrl { get; set; }
        public string     ReleaseNotes { get; set; }
        public IEnumerable<FormatsDbInfo> FormatsData { get; set; }

        public static GarUpdateInfo Parse (XmlDocument xml)
        {
            var root = xml.DocumentElement.SelectSingleNode ("/GARbro");
            if (null == root)
                return null;
            var info = new GarUpdateInfo
            {
                ReleaseVersion = Version.Parse (GetInnerText (root.SelectSingleNode ("Release/Version"))),
                ReleaseUrl = new Uri (GetInnerText (root.SelectSingleNode ("Release/Url"))),
                ReleaseNotes = GetInnerText (root.SelectSingleNode ("Release/Notes")),
                FormatsData = root.SelectNodes ("FormatsData").Cast<XmlNode>()
                    .Select (node => new FormatsDbInfo {
                        Version = Int32.Parse (GetInnerText (node.SelectSingleNode ("FileVersion"))),
                        Url = new Uri (GetInnerText (node.SelectSingleNode ("Url"))),
                        Assemblies = ParseAssemblies (node.SelectNodes ("Requires/Assembly")),
                    }),
            };
            return info;
        }

        public FormatsDbInfo FindMatchingFormatsData (int current_version, IEnumerable<Assembly> assemblies)
        {
            var loaded = assemblies.Select (a => a.GetName()).ToDictionary (a => a.Name, a => a.Version);
            // select first formats db that has greater version number and no new assemblies
            return FormatsData.Where (f => f.Version > current_version)
                              .OrderByDescending (f => f.Version)
                              .FirstOrDefault (f => f.Assemblies.All (a => loaded.ContainsKey (a.Key) && loaded[a.Key] >= a.Value));
        }

        static string GetInnerText (XmlNode node)
        {
            // XXX node?.InnerText ?? ""
            return node != null ? node.InnerText : "";
        }

        static IDictionary<string, Version> ParseAssemblies (XmlNodeList nodes)
        {
            var dict = new Dictionary<string, Version>();
            foreach (XmlNode node in nodes)
            {
                var attr = node.Attributes;
                var name = attr["Name"];
                var version = attr["Version"];
                if (name != null && version != null)
                    dict[name.Value] = Version.Parse (version.Value);
            }
            return dict;
        }
    }

    public class FormatsDbInfo
    {
        public int Version { get; set; }
        public Uri     Url { get; set; }
        public IDictionary<string, Version> Assemblies { get; set; }
    }

    internal sealed class GarUpdate : ICommand, IDisposable
    {
        private readonly MainWindow     m_main;
        private readonly BackgroundWorker m_update_checker = new BackgroundWorker();
        private readonly Uri            m_url;

        const int RequestTimeout = 20000; // milliseconds

        public GarUpdate (MainWindow main, Uri url)
        {
            m_main = main;
            m_url = url;
            m_update_checker.DoWork += StartUpdatesCheck;
            m_update_checker.RunWorkerCompleted += UpdatesCheckComplete;
        }

        public void Execute (object parameter)
        {
            if (!m_update_checker.IsBusy)
                m_update_checker.RunWorkerAsync();
        }

        public bool CanExecute (object parameter)
        {
            return !m_update_checker.IsBusy;
        }

        public event EventHandler CanExecuteChanged;

        void OnCanExecuteChanged ()
        {
            var handler = CanExecuteChanged;
            if (handler != null)
                handler (this, EventArgs.Empty);
        }

        private void StartUpdatesCheck (object sender, DoWorkEventArgs e)
        {
            OnCanExecuteChanged();
            if (m_url != null)
                e.Result = Check (m_url);
        }

        private void UpdatesCheckComplete (object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (e.Error != null)
                {
                    m_main.SetStatusText (string.Format ("{0} {1}", guiStrings.MsgUpdateFailed, e.Error.Message));
                    return;
                }
                else if (e.Cancelled)
                    return;
                var result = e.Result as GarUpdateInfo;
                if (null == result)
                {
                    m_main.SetStatusText (guiStrings.MsgNoUpdates);
                    return;
                }
                ShowUpdateResult (result);
            }
            finally
            {
                OnCanExecuteChanged();
            }
        }

        UpdateDialog    m_dialog;
        FormatsDbInfo   m_formats_db;

        private void ShowUpdateResult (GarUpdateInfo result)
        {
            var app_version = Assembly.GetExecutingAssembly().GetName().Version;
            bool has_app_update = app_version < result.ReleaseVersion;

            var loaded_assemblies = AppDomain.CurrentDomain.GetAssemblies();
            m_formats_db = result.FindMatchingFormatsData (FormatCatalog.Instance.CurrentSchemeVersion, loaded_assemblies);
            bool has_db_update = m_formats_db != null;

            if (!has_app_update && !has_db_update)
            {
                m_main.SetStatusText (guiStrings.MsgUpToDate);
                return;
            }
            m_dialog = new UpdateDialog (result, has_app_update, has_db_update);
            m_dialog.Owner = m_main;
            m_dialog.FormatsDownload.Click += StartFormatsDownload;
            m_dialog.ShowDialog();
        }

        private async void StartFormatsDownload (object control, RoutedEventArgs e)
        {
            if (null == m_formats_db || null == m_formats_db.Url)
                return;
            var dialog = m_dialog;
            try
            {
                dialog.FormatsDownload.IsEnabled = false;
                var app_data_folder = m_main.App.GetLocalAppDataFolder();
                Directory.CreateDirectory (app_data_folder);
                using (var client = new WebClientEx())
                using (var tmp_file = new GARbro.Shell.TemporaryFile (app_data_folder, Path.GetRandomFileName()))
                {
                    client.Timeout = RequestTimeout;
                    await client.DownloadFileTaskAsync (m_formats_db.Url, tmp_file.Name);

                    m_main.App.DeserializeScheme (tmp_file.Name);
                    var local_formats_dat = Path.Combine (app_data_folder, App.FormatsDat);
                    if (!GARbro.Shell.File.Rename (tmp_file.Name, local_formats_dat))
                        throw new Win32Exception (GARbro.Shell.File.GetLastError());
                }
                SetFormatsUpdateStatus (dialog, guiStrings.MsgUpdateComplete);
            }
            catch (Exception X)
            {
                SetFormatsUpdateStatus (dialog, guiStrings.MsgDownloadFailed, X.Message);
            }
            finally
            {
                dialog.FormatsDownload.Visibility = Visibility.Hidden;
            }
        }

        void SetFormatsUpdateStatus (UpdateDialog dialog, string text1, string text2 = null)
        {
            if (dialog.IsClosed)
                m_main.SetStatusText (text1);
            else if (null == text2)
                dialog.FormatsUpdateText.Text = text1;
            else
                dialog.FormatsUpdateText.Text = string.Format ("{0}\n{1}", text1, text2);
        }

        /// <summary>
        /// Check if loaded assemblies match required versions.
        /// </summary>
        bool CheckAssemblies (IDictionary<string, Version> assemblies)
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select (a => a.GetName())
                         .ToDictionary (a => a.Name, a => a.Version);
            return assemblies.All (a => loaded.ContainsKey (a.Key) && loaded[a.Key] >= a.Value);
        }

        GarUpdateInfo Check (Uri version_url)
        {
            var request = WebRequest.Create (version_url);
            request.Timeout = RequestTimeout;
            var response = (HttpWebResponse)request.GetResponse();
            using (var input = response.GetResponseStream())
            {
                var xml = new XmlDocument();
                xml.Load (input);
                return GarUpdateInfo.Parse (xml);
            }
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_update_checker.Dispose();
                m_disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }

    /// <summary>
    /// WebClient with timeout setting.
    /// </summary>
    internal class WebClientEx : WebClient
    {
        /// <summary>
        /// Request timeout, in milliseconds.
        /// </summary>
        public int Timeout { get; set; }

        public WebClientEx ()
        {
            Timeout = 60000;
        }

        protected override WebRequest GetWebRequest (Uri uri)
        {
            var request = base.GetWebRequest (uri);
            request.Timeout = Timeout;
            return request;
        }
    }
}
