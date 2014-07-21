//! \file       ScriptText.cs
//! \date       Thu Jul 10 11:09:32 2014
//! \brief      Script text resource interface.
//

using System.IO;
using System.Text;
using System.Collections.Generic;

namespace GameRes
{
    public struct ScriptLine
    {
        public uint   Id;
        public string Text;
    }

    public class ScriptData
    {
        public ICollection<ScriptLine> TextLines { get { return m_text; } }

        protected List<ScriptLine> m_text = new List<ScriptLine>();
/*
        public abstract void Serialize (Stream output);
        public abstract void Deserialize (Stream input);
*/
    }

    public abstract class ScriptFormat : IResource
    {
        public override string Type { get { return "script"; } }

        public abstract ScriptData Read (string name, Stream file);
        public abstract void Write (Stream file, ScriptData script);
    }
}
