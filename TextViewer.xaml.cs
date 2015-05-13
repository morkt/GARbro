//! \file       TextViewer.cs
//! \date       Mon May 11 23:24:33 2015
//! \brief      Text file viewer widget.
//
// Copyright (C) 2015 by morkt
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
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace JustView
{
    /// <summary>
    /// Interaction logic for TextViewer.xaml
    /// </summary>
    public partial class TextViewer : FlowDocumentScrollViewer
    {
        Lazy<double>        m_default_width;

        public TextViewer ()
        {
            m_default_width = new Lazy<double> (() => GetFixedWidth (80));
            InitializeComponent();
            DefaultZoom = 100;
        }

        public void Clear ()
        {
            this.Document.Blocks.Clear();
            Input = null;
        }

        public ScrollViewer ScrollViewer { get { return FindScrollViewer(); } }
        public double       DefaultWidth { get { return m_default_width.Value; } }
        public double        DefaultZoom { get; private set; }
        public Stream              Input { get; set; }
        public double       MaxLineWidth { get; set; }

        private bool m_word_wrap;
        public bool IsWordWrapEnabled
        {
            get { return m_word_wrap; }
            set
            {
                m_word_wrap = value;
                if (Input != null)
                    ApplyWordWrap (value);
            }
        }

        private Encoding m_current_encoding;
        public Encoding CurrentEncoding
        {
            get { return m_current_encoding; }
            set
            {
                if (m_current_encoding != value)
                {
                    m_current_encoding = value;
                    Refresh();
                }
            }
        }

        public void DisplayStream (Stream file, Encoding enc)
        {
            if (file.Length > 0xffffff)
                throw new ApplicationException ("File is too long");
            ReadStream (file, enc);
            var sv = FindScrollViewer();
            if (sv != null)
                sv.ScrollToHome();
            Input = file;
            m_current_encoding = enc;
        }

        public void Refresh ()
        {
            if (Input != null)
            {
                Input.Position = 0;
                ReadStream (Input, CurrentEncoding);
            }
        }

        byte[] m_test_buf = new byte[0x400];

        public Encoding GuessEncoding (Stream file)
        {
            var enc = Encoding.Default;
            if (3 == file.Read (m_test_buf, 0, 3))
            {
                if (0xef == m_test_buf[0] && 0xbb == m_test_buf[1] && 0xbf == m_test_buf[2])
                    enc = Encoding.UTF8;
                else if (0xfe == m_test_buf[0] && 0xff == m_test_buf[1])
                    enc = Encoding.BigEndianUnicode;
                else if (0xff == m_test_buf[0] && 0xfe == m_test_buf[1])
                    enc = Encoding.Unicode;
            }
            file.Position = 0;
            return enc;
        }

        public bool IsTextFile (Stream file)
        {
            int read = file.Read (m_test_buf, 0, m_test_buf.Length);
            file.Position = 0;
            bool found_eol = false;
            for (int i = 0; i < read; ++i)
            {
                byte c = m_test_buf[i];
                if (c < 9 || (c > 0x0d && c < 0x1a) || (c > 0x1b && c < 0x20))
                    return false;
                if (!found_eol && 0x0a == c)
                    found_eol = true;
            }
            return found_eol || read < 80;
        }

        double GetFixedWidth (int char_width)
        {
            var block = new TextBlock();
            block.FontFamily = this.Document.FontFamily;
            block.FontSize = this.Document.FontSize;
            block.Padding = this.Document.PagePadding;
            block.Text = new string ('M', char_width);
            block.Measure (new Size (double.PositiveInfinity, double.PositiveInfinity));
            return block.DesiredSize.Width;
        }

        void ReadStream (Stream file, Encoding enc)
        {
            using (var reader = new StreamReader (file, enc, false, 0x400, true))
            {
                this.Document.Blocks.Clear();
                var para = new Paragraph();
                var block = new TextBlock();
                block.FontFamily = this.Document.FontFamily;
                block.FontSize = this.Document.FontSize;
                block.Padding = this.Document.PagePadding;
                double max_width = 0;
                var max_size = new Size (double.PositiveInfinity, double.PositiveInfinity);
                for (;;)
                {
                    var line = reader.ReadLine();
                    if (null == line)
                        break;
                    if (line.Length > 0)
                    {
                        block.Text = line;
                        block.Measure (max_size);
                        var width = block.DesiredSize.Width;
                        if (width > max_width)
                            max_width = width;
                        para.Inlines.Add (new Run (line));
                    }
                    para.Inlines.Add (new LineBreak());
                }
                this.Document.Blocks.Add (para);
                MaxLineWidth = max_width;
                ApplyWordWrap (IsWordWrapEnabled);
            }
        }

        public void ApplyWordWrap (bool word_wrap)
        {
            var scroll = this.ScrollViewer;
            if (word_wrap && scroll != null)
            {
                this.Document.PageWidth = scroll.ViewportWidth;
                var width_binding = new Binding ("ViewportWidth");
                width_binding.Source = scroll;
                width_binding.Mode = BindingMode.OneWay;
                width_binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
                BindingOperations.SetBinding (this.Document, FlowDocument.PageWidthProperty, width_binding);
            }
            else
            {
                BindingOperations.ClearBinding (this.Document, FlowDocument.PageWidthProperty);
                this.Document.PageWidth = MaxLineWidth;
            }
        }

        private ScrollViewer FindScrollViewer ()
        {
            if (VisualTreeHelper.GetChildrenCount (this) == 0)
                return null;

            // Border is the first child of first child of a ScrolldocumentViewer
            var firstChild = VisualTreeHelper.GetChild (this, 0);
            if (firstChild == null)
                return null;

            var border = VisualTreeHelper.GetChild (firstChild, 0) as Decorator;
            if (border == null)
                return null;

            return border.Child as ScrollViewer;
        }
    }
}
