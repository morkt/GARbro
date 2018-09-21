//! \file       ScriptText.cs
//! \date       Thu Jul 10 11:09:32 2014
//! \brief      Script text resource interface.
//

using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;

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

    public abstract class GenericScriptFormat : ScriptFormat
    {
        public override ScriptData Read (string name, Stream file)
        {
            throw new System.NotImplementedException();
        }

        public override void Write (Stream file, ScriptData script)
        {
            throw new System.NotImplementedException();
        }
    }

    [Export(typeof(ScriptFormat))]
    public class TextScriptFormat : GenericScriptFormat
    {
        public override string         Tag { get { return "TXT"; } }
        public override string Description { get { return "Text file"; } }
        public override uint     Signature { get { return 0; } }
    }

    [Export(typeof(ScriptFormat))]
    public class BinScriptFormat : GenericScriptFormat
    {
        public override string         Tag { get { return "SCR"; } }
        public override string Description { get { return "Binary script format"; } }
        public override uint     Signature { get { return 0; } }

        public BinScriptFormat ()
        {
            Extensions = new[] { "scr", "bin" };
        }
    }
}
