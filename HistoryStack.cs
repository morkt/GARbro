//! \file       HistoryStack.cs
//! \date       Sun Aug 21 01:06:53 2011
//! \brief      action history stack interface (undo/redo).
//
// Copyright (C) 2011 by poddav
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Rnd.Windows
{
    public class HistoryStack<State>
    {
        private List<State>     m_back    = new List<State>();
        private Stack<State>    m_forward = new Stack<State>();

        public int Limit { get; set; }

        public IEnumerable<State> UndoStack { get { return m_back; } }
        public IEnumerable<State> RedoStack { get { return m_forward; } }

        public HistoryStack (int limit = 50)
        {
            Limit = limit;
        }

        public State Undo (State current)
        {
            if (!CanUndo())
                return default(State);

            m_forward.Push (current);
            current = m_back.Last();
            m_back.RemoveAt (m_back.Count - 1);
            OnStateChanged();

            return current;
        }

        public State Redo (State current)
        {
            if (!CanRedo())
                return default(State);

            m_back.Add (current);
            current = m_forward.Pop();
            OnStateChanged();

            return current;
        }

        public bool CanUndo ()
        {
            return m_back.Any();
        }

        public bool CanRedo ()
        {
            return m_forward.Any();
        }

        public void Push (State current)
        {
            m_back.Add (current);
            if (m_back.Count > Limit)
                m_back.RemoveRange (0, m_back.Count - Limit);

            m_forward.Clear();
            OnStateChanged();
        }

        public void Clear ()
        {
            if (m_back.Any() || m_forward.Any())
            {
                m_back.Clear();
                m_forward.Clear();
                OnStateChanged();
            }
        }

        public event EventHandler StateChanged;

        private void OnStateChanged ()
        {
            if (StateChanged != null)
                StateChanged (this, EventArgs.Empty);
        }
    }
}
