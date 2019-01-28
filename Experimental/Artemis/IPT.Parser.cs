using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameRes.Formats.Artemis
{
    internal partial struct ValueType
    {
        public object Value { get { return o ?? s as object ?? (object)n; } }
    }

    internal partial class IPTParser
    {
        public IPTParser () : base (null) { }

        internal IPTObject RootObject { get; set; }

        Stack<IPTObject>   m_value_stack;

        public void Parse (Stream s)
        {
            this.RootObject = new IPTObject();
            m_value_stack = new Stack<IPTObject>();
            this.Scanner = new IPTScanner (s);
            this.Parse();
        }

        internal IPTObject CurrentObject
        {
            get { return m_value_stack.Count > 0 ? m_value_stack.Peek() : RootObject; }
        }

        void BeginObject ()
        {
            m_value_stack.Push (new IPTObject());
        }

        void EndObject ()
        {
            CurrentSemanticValue.o = m_value_stack.Pop();
        }
    }

    internal class IPTObject
    {
        Hashtable m_dict = new Hashtable();
        ArrayList m_values = new ArrayList();

        public ArrayList Values { get { return m_values; } }

        public object this[string field]
        {
            get { return m_dict[field]; }
            set { m_dict[field] = value; }
        }

        public bool Contains (string field)
        {
            return m_dict.ContainsKey (field);
        }
    }
}
