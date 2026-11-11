//! \file       AutoComplete.cs
//! \date       Tue Aug 04 20:41:22 2015
//! \brief      TextBox that uses filesystem as source for autocomplete.
//
// Copyright (C) 2014-2015 by morkt
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
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GARbro.GUI
{
    /// <summary>
    /// TextBox that uses filesystem as source for autocomplete.
    /// </summary>
    public class ExtAutoCompleteBox : AutoCompleteBox
    {
        public delegate void EnterKeyDownEvent (object sender, KeyEventArgs e);
        public event EnterKeyDownEvent EnterKeyDown;

        public ExtAutoCompleteBox ()
        {
            this.GotFocus += (s, e) => { IsTextBoxFocused = true; };
            this.LostFocus += (s, e) => { IsTextBoxFocused = false; };
        }

        public bool IsTextBoxFocused
        {
            get { return (bool)GetValue (IsTextBoxFocusedProperty); }
            private set { SetValue (IsTextBoxFocusedProperty, value); }
        }

        public static readonly DependencyProperty IsTextBoxFocusedProperty = 
            DependencyProperty.RegisterAttached ("IsTextBoxFocused", typeof(bool), typeof(ExtAutoCompleteBox), new UIPropertyMetadata());

        protected override void OnKeyDown (KeyEventArgs e)
        {
            base.OnKeyDown (e);
            if (e.Key == Key.Enter)
                RaiseEnterKeyDownEvent (e);
        }

        private void RaiseEnterKeyDownEvent (KeyEventArgs e)
        {
            if (EnterKeyDown != null)
                EnterKeyDown (this, e);
        }

        protected override void OnPopulating (PopulatingEventArgs e)
        {
            try
            {
                if (!GameRes.VFS.IsVirtual)
                {
                    var candidates = new List<string>();
                    string dirname = Path.GetDirectoryName (this.Text);
                    if (!string.IsNullOrEmpty (dirname) && Directory.Exists (dirname))
                    {
                        foreach (var dir in Directory.GetDirectories (dirname))
                        {
                            if (dir.StartsWith (dirname, StringComparison.CurrentCultureIgnoreCase))
                                candidates.Add (dir);
                        }
                    }
                    this.ItemsSource = candidates;
                }
            }
            catch
            {
                // ignore filesystem errors
            }
            base.OnPopulating (e);
        }
    }
}
