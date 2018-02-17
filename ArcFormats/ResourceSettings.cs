//! \file       ResourceSettings.cs
//! \date       2018 Jan 08
//! \brief      Persistent resource settings implementation.
//

using System.ComponentModel.Composition;
using System.Text;
using GameRes.Formats.Strings;

namespace GameRes.Formats
{
    internal class LocalResourceSetting : ResourceSettingBase
    {
        public override object Value {
            get { return Properties.Settings.Default[Name]; }
            set { Properties.Settings.Default[Name] = value; }
        }

        public LocalResourceSetting () { }

        public LocalResourceSetting (string name)
        {
            Name = name;
            Text = arcStrings.ResourceManager.GetString (name, arcStrings.Culture) ?? name;
        }
    }

    internal class EncodingSetting : LocalResourceSetting
    {
        public override object Value {
            get { return Encoding.GetEncoding ((int)base.Value); }
            set { base.Value = ((Encoding)value).CodePage; }
        }

        public EncodingSetting () { }

        public EncodingSetting (string name) : base (name) { }
    }

    [Export(typeof(ISettingsManager))]
    internal class SettingsManager : ISettingsManager
    {
        public void UpgradeSettings ()
        {
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }
        }

        public void SaveSettings ()
        {
            Properties.Settings.Default.Save();
        }
    }
}
