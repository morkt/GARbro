//! \file       ResourceSettings.cs
//! \date       2018 Jan 08
//! \brief      Persistent resource settings implementation.
//
// Copyright (C) 2018 by morkt
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

namespace GameRes
{
    /// <summary>
    /// Interface to assembly app.config settings.
    /// </summary>
    public interface IResourceSetting
    {
        /// <summary>
        /// Internal setting name, should match the name defined in app.config.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Short text description of the setting (suitable for use in GUI dialog).
        /// </summary>
        string Text { get; }

        /// <summary>
        /// More elaborate setting description.
        /// </summary>
        string Description { get; }


        /// <summary>
        /// Actual setting value.
        /// </summary>
        object Value { get; set; }
    }

    /// <summary>
    /// Manage assembly settings during application session. Implementations of this interface are made
    /// available to GameRes library by means of MEF.
    /// </summary>
    public interface ISettingsManager
    {
        /// <summary>
        /// Called on application startup to check if settings need upgrading after assembly version change.
        /// </summary>
        void UpgradeSettings ();

        /// <summary>
        /// Called on application exit.
        /// </summary>
        void SaveSettings ();
    }

    public abstract class ResourceSettingBase : IResourceSetting
    {
        public string        Name { get; set; }
        public string        Text { get; set; }
        public string Description { get; set; }

        public abstract object Value { get; set; }

        public TValue Get<TValue> ()
        {
            var value = this.Value;
            if (null == value || !(value is TValue))
                return default(TValue);
            return (TValue)value;
        }
    }

    internal class LocalResourceSetting : ResourceSettingBase
    {
        public override object Value {
            get { return GameRes.Properties.Settings.Default[Name]; }
            set { GameRes.Properties.Settings.Default[Name] = value; }
        }
    }
}
