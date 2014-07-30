//! \file       ListViewEx.cs
//! \date       Thu Jul 24 02:19:38 2014
//! \brief      Extended ListView with explorer-like multiple items selection.
//
// Copyright (C) 2014 by morkt
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections;
using System.Diagnostics;
using System.Linq;

namespace GARbro.GUI
{
    /// <summary>
    /// This Extended ListView allows selecting multiple items by dragging mouse over them.
    /// </summary>
    public class ListViewEx : ListView
    {
        public bool          SelectionActive { get; set; }
        public ListViewItem LastSelectedItem { get; set; }

        public ListViewEx ()
        {
        }

        protected override DependencyObject GetContainerForItemOverride()
        {
            return new ListViewItemEx();
        }

        protected override void OnMouseLeftButtonDown (MouseButtonEventArgs e)
        {
            if (0 == (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)))
            {
                StartDragSelect();
            }
            base.OnMouseLeftButtonDown (e);
        }

        protected override void OnMouseLeftButtonUp (MouseButtonEventArgs e)
        {
            if (SelectionActive)
            {
                EndDragSelect();
            }
            base.OnMouseLeftButtonUp (e);
        }

        protected override void OnMouseLeave (MouseEventArgs e)
        {
            if (SelectionActive)
            {
                EndDragSelect();
            }
            base.OnMouseLeave (e);
        }

        protected override void OnItemsSourceChanged (IEnumerable oldValue, IEnumerable newValue)
        {
            if (SelectionActive)
            {
                EndDragSelect();
            }
            base.OnItemsSourceChanged (oldValue, newValue);
        }

        internal void StartDragSelect ()
        {
            SelectionActive = true;
            SelectedItems.Clear();
        }

        internal void EndDragSelect ()
        {
            SelectionActive = false;
            LastSelectedItem = null;
        }

        internal void ContinueDragSelect (ListViewItem addition)
        {
            if (null != LastSelectedItem)
            {
                int start = ItemContainerGenerator.IndexFromContainer (LastSelectedItem);
                int end   = ItemContainerGenerator.IndexFromContainer (addition);
                if (start != -1 && end != -1)
                {
                    if (start > end)
                    {
                        int index = start;
                        start = end;
                        end = index;
                    }
                    // for each item in the range [start, end]
                    foreach (var item in Items.Cast<object>().Skip (start).Take (end-start+1))
                    {
                        var lvi = (ListViewItem)ItemContainerGenerator.ContainerFromItem (item);
                        if (!lvi.IsSelected)
                            lvi.IsSelected = true;
                    }
                }
            }
            if (!addition.IsSelected)
                addition.IsSelected = true;
            LastSelectedItem = addition;
        }
    }

    class ListViewItemEx : ListViewItem
    {
        private ListViewEx ParentListView
        {
            get { return ItemsControl.ItemsControlFromItemContainer (this) as ListViewEx; }
        }

        protected override void OnPreviewMouseLeftButtonDown (MouseButtonEventArgs e)
        {
            var lv = ParentListView;
            if (null != lv)
            {
                if (0 == (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)))
                {
                    lv.StartDragSelect();
                    lv.ContinueDragSelect (this);
                    return;
                }
            }
            base.OnPreviewMouseLeftButtonDown(e);
        }

        protected override void OnPreviewMouseLeftButtonUp (MouseButtonEventArgs e)
        {
            var lv = ParentListView;
            if (null != lv && lv.SelectionActive)
            {
                lv.EndDragSelect();
            }
            base.OnPreviewMouseLeftButtonUp(e);
        }

        protected override void OnMouseDoubleClick (MouseButtonEventArgs e)
        {
            var lv = ParentListView;
            if (null != lv && lv.SelectionActive)
            {
                lv.EndDragSelect();
            }
            base.OnMouseDoubleClick (e);
        }

        protected override void OnMouseEnter (MouseEventArgs e)
        {
            var lv = ParentListView;
            if (null != lv && lv.SelectionActive)
            {
                lv.ContinueDragSelect (this);
                return;
            }
            base.OnMouseEnter (e);
        }
    }
}
