//! \file       GameTitle.cs
//! \date       Wed Jan 20 13:07:51 2016
//! \brief      class for game titles localization.
//
// Copyright (C) 2016 by morkt
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

using System.Globalization;

namespace GameRes
{
    public class GameTitle : System.IEquatable<GameTitle>
    {
        public string       EN { get; private set; }
        public string Original { get; private set; }
        public string Title
        {
            get
            {
                if ("ja" == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                    return Original;
                else
                    return EN;
            }
        }

        public GameTitle (string en) : this (en, en)
        {
        }

        public GameTitle (string en, string jp)
        {
            EN = en;
            Original = jp;
        }

        public override int GetHashCode ()
        {
            return Original.GetHashCode();
        }

        public override bool Equals (object obj)
        {
            return Equals (obj as GameTitle);
        }

        public bool Equals (GameTitle other)
        {
            return other != null && other.Original == this.Original;
        }
    }
}
