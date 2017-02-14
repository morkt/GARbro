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

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        private readonly BackgroundWorker m_update_checker = new BackgroundWorker();

        private void InitUpdatesChecker ()
        {
            m_update_checker.DoWork += StartUpdatesCheck;
            m_update_checker.RunWorkerCompleted += UpdatesCheckComplete;
        }

        /// <summary>
        /// Handle "Check for updates" command.
        /// </summary>
        private void CheckUpdatesExec (object sender, ExecutedRoutedEventArgs e)
        {
            if (!m_update_checker.IsBusy)
                m_update_checker.RunWorkerAsync();
        }

        private void StartUpdatesCheck (object sender, DoWorkEventArgs e)
        {
            var url = m_app.Resources["UpdateUrl"] as Uri;
            if (null == url)
                return;
            using (var updater = new GarUpdate (this))
            {
                e.Result = updater.Check (url);
            }
        }

        private void UpdatesCheckComplete (object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                SetStatusText (string.Format ("{0} {1}", "Update failed.", e.Error.Message));
                return;
            }
            else if (e.Cancelled)
                return;
            var result = e.Result as GarUpdateInfo;
            if (null == result)
            {
                SetStatusText ("No updates currently available.");
                return;
            }
            var app_version = Assembly.GetExecutingAssembly().GetName().Version;
            var db_version = FormatCatalog.Instance.CurrentSchemeVersion;
            bool has_app_update = app_version < result.ReleaseVersion;
            bool has_db_update = db_version < result.FormatsVersion && CheckAssemblies (result.Assemblies);
            if (!has_app_update && !has_db_update)
            {
                SetStatusText ("GARbro version is up to date.");
                return;
            }
            var dialog = new UpdateDialog (result, has_app_update, has_db_update);
            dialog.Owner = this;
            dialog.FormatsDownload.Click = FormatsDownloadExec;
            dialog.ShowDialog();
        }

        private void FormatsDownloadExec (object sender, RoutedEventArgs e)
        {
        }

        /// <summary>
        /// Check if loaded assemblies match required versions.
        /// </summary>
        bool CheckAssemblies (IDictionary<string, Version> assemblies)
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select (a => a.GetName())
                         .ToDictionary (a => a.Name, a => a.Version);
            foreach (var item in assemblies)
            {
                if (!loaded.ContainsKey (item.Key))
                    return false;
                if (loaded[item.Key] < item.Value)
                    return false;
            }
            return true;
        }
    }

    public class GarUpdateInfo
    {
        public Version  ReleaseVersion { get; set; }
        public Uri          ReleaseUrl { get; set; }
        public string     ReleaseNotes { get; set; }
        public int      FormatsVersion { get; set; }
        public Uri          FormatsUrl { get; set; }
        public IDictionary<string, Version> Assemblies { get; set; }
    }

    internal sealed class GarUpdate : IDisposable
    {
        Window      m_main;

        const int RequestTimeout = 20000; // milliseconds

        public GarUpdate (Window main)
        {
            m_main = main;
        }

        public GarUpdateInfo Check (Uri version_url)
        {
            var request = WebRequest.Create (version_url);
            request.Timeout = RequestTimeout;
            var response = (HttpWebResponse)request.GetResponse();
            using (var input = response.GetResponseStream())
            {
                var xml = new XmlDocument();
                xml.Load (input);
                var root = xml.DocumentElement.SelectSingleNode ("/GARbro");
                if (null == root)
                    return null;
                var info = new GarUpdateInfo
                {
                    ReleaseVersion = Version.Parse (GetInnerText (root.SelectSingleNode ("Release/Version"))),
                    ReleaseUrl = new Uri (GetInnerText (root.SelectSingleNode ("Release/Url"))),
                    ReleaseNotes = GetInnerText (root.SelectSingleNode ("Release/Notes")),

                    FormatsVersion = Int32.Parse (GetInnerText (root.SelectSingleNode ("FormatsData/FileVersion"))),
                    FormatsUrl = new Uri (GetInnerText (root.SelectSingleNode ("FormatsData/Url"))),
                    Assemblies = ParseAssemblies (root.SelectNodes ("FormatsData/Requires/Assembly")),
                };
                return info;
            }
        }

        static string GetInnerText (XmlNode node)
        {
            return node != null ? node.InnerText : "";
        }

        IDictionary<string, Version> ParseAssemblies (XmlNodeList nodes)
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

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }
}
