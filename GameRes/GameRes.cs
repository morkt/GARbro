//! \file       GameRes.cs
//! \date       Mon Jun 30 20:12:13 2014
//! \brief      game resources browser.
//

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;
using GameRes.Collections;
using GameRes.Strings;

namespace GameRes
{
    /// <summary>
    /// Basic filesystem entry.
    /// </summary>
    public class Entry
    {
        public virtual string Name   { get; set; }
        public virtual string Type   { get; set; }
        public         long   Offset { get; set; }
        public         uint   Size   { get; set; }

        public Entry ()
        {
            Type = "";
            Offset = -1;
        }

        /// <summary>
        /// Check whether entry lies within specified file bound.
        /// </summary>
        public bool CheckPlacement (long max_offset)
        {
            return Offset < max_offset && Size < max_offset && Offset <= max_offset - Size;
        }
    }

    public class PackedEntry : Entry
    {
        public uint UnpackedSize { get; set; }
        public bool IsPacked     { get; set; }
    }

    public abstract class IResource
    {
        /// <summary>Short tag sticked to resource (usually filename extension)</summary>
        public abstract string Tag { get; }

        /// <summary>Resource description (its source/inventor)</summary>
        public abstract string Description { get; }

        /// <summary>Resource type (image/archive/script)</summary>
        public abstract string Type { get; }

        /// <summary>First 4 bytes of the resource file as little-endian 32-bit integer,
        /// or zero if it could vary.</summary>
        public abstract uint Signature { get; }

        /// <summary>Signatures peculiar to the resource (the one above is also included here).</summary>
        public IEnumerable<uint> Signatures
        {
            get { return m_signatures; }
            protected set { m_signatures = value; }
        }

        /// <summary>Filename extensions peculiar to the resource.</summary>
        public IEnumerable<string> Extensions
        {
            get { return m_extensions; }
            protected set { m_extensions = value; }
        }

        /// <summary>
        /// Create empty Entry that corresponds to implemented resource.
        /// </summary>
        public virtual Entry CreateEntry ()
        {
            return new Entry { Type = this.Type };
        }

        private IEnumerable<string> m_extensions;
        private IEnumerable<uint>   m_signatures;

        protected IResource ()
        {
            m_extensions = new string[] { Tag.ToLower() };
            m_signatures = new uint[] { Signature };
        }
    }

    public class ResourceOptions
    {
        public object Widget { get; set; }
    }

    public abstract class ArchiveFormat : IResource
    {
        public override string Type { get { return "archive"; } }

        public abstract bool IsHierarchic { get; }

        public abstract ArcFile TryOpen (ArcView view);

        /// <summary>
        /// Extract file referenced by <paramref name="entry"/> into current directory.
        /// </summary>
        public virtual void Extract (ArcFile file, Entry entry)
        {
            using (var reader = OpenEntry (file, entry))
            {
                using (var writer = CreateFile (entry))
                {
                    reader.CopyTo (writer);
                }
            }
        }

        /// <summary>
        /// Open file referenced by <paramref name="entry"/> as Stream.
        /// </summary>
        public virtual Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return arc.File.CreateStream (entry.Offset, entry.Size);
        }

        /// <summary>
        /// Create file corresponding to <paramref name="entry"/> in current directory and open it
        /// for writing.
        /// </summary>
        public virtual Stream CreateFile (Entry entry)
        {
            string dir = Path.GetDirectoryName (entry.Name);
            if (!string.IsNullOrEmpty (dir))
            {
                Directory.CreateDirectory (dir);
            }
            return File.Create (entry.Name);
        }

        /// <summary>
        /// Create resource archive named <paramref name="filename"/> containing entries from the
        /// supplied <paramref name="list"/> and applying necessary <paramref name="options"/>.
        /// </summary>
        public virtual void Create (string filename, IEnumerable<Entry> list, ResourceOptions options = null)
        {
            throw new NotImplementedException ("ArchiveFormat.Create is not implemented");
        }

        public virtual ResourceOptions GetOptions ()
        {
            return null;
        }
    }

    public delegate void ParametersRequestEventHandler (object sender, ParametersRequestEventArgs e);

    public class ParametersRequestEventArgs : EventArgs
    {
        /// <summary>
        /// String describing request nature (encryption key etc).
        /// </summary>
        public string Notice        { get; set; }

        /// <summary>
        /// UIElement responsible for displaying request.
        /// </summary>
        public object InputWidget   { get; set; }

        /// <summary>
        /// Return value from ShowDialog()
        /// </summary>
        public bool   InputResult   { get; set; }
    }

    public sealed class FormatCatalog
    {
        private static readonly FormatCatalog m_instance = new FormatCatalog();

        #pragma warning disable 649
        [ImportMany(typeof(ArchiveFormat))]
        private IEnumerable<ArchiveFormat>  m_arc_formats;
        [ImportMany(typeof(ImageFormat))]
        private IEnumerable<ImageFormat>    m_image_formats;
        [ImportMany(typeof(ScriptFormat))]
        private IEnumerable<ScriptFormat>   m_script_formats;
        #pragma warning restore 649

        private MultiValueDictionary<string, IResource> m_extension_map = new MultiValueDictionary<string, IResource>();
        private MultiValueDictionary<uint, IResource> m_signature_map = new MultiValueDictionary<uint, IResource>();

        /// <summary> The only instance of this class.</summary>
        public static FormatCatalog       Instance      { get { return m_instance; } }

        public IEnumerable<ArchiveFormat> ArcFormats    { get { return m_arc_formats; } }
        public IEnumerable<ImageFormat>   ImageFormats  { get { return m_image_formats; } }
        public IEnumerable<ScriptFormat>  ScriptFormats { get { return m_script_formats; } }

        public Exception LastError { get; set; }

        public event ParametersRequestEventHandler  ParametersRequest;

        private FormatCatalog ()
        {
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();
            //Adds all the parts found in the same assembly as the Program class
            catalog.Catalogs.Add (new AssemblyCatalog (typeof(FormatCatalog).Assembly));
            //Adds parts matching pattern found in the directory of the assembly
            catalog.Catalogs.Add (new DirectoryCatalog (Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly().Location), "Arc*.dll"));

            //Create the CompositionContainer with the parts in the catalog
            var container = new CompositionContainer (catalog);

            //Fill the imports of this object
            container.ComposeParts (this);
            AddResourceImpl (m_arc_formats);
            AddResourceImpl (m_image_formats);
            AddResourceImpl (m_script_formats);
        }

        private void AddResourceImpl (IEnumerable<IResource> formats)
        {
            foreach (var impl in formats)
            {
                foreach (var ext in impl.Extensions)
                {
                    m_extension_map.Add (ext.ToUpper(), impl);
                }
                foreach (var signature in impl.Signatures)
                {
                    m_signature_map.Add (signature, impl);
                }
            }
        }

        /// <summary>
        /// Look up filename in format registry by filename extension and return corresponding interfaces.
        /// if no formats available, return empty range.
        /// </summary>
        public IEnumerable<IResource> LookupFileName (string filename)
        {
            string ext = Path.GetExtension (filename);
            if (null == ext)
                return new IResource[0];
            return LookupTag (ext.TrimStart ('.'));
        }

        public IEnumerable<IResource> LookupTag (string tag)
        {
            return m_extension_map.GetValues (tag.ToUpper(), true);
        }

        public IEnumerable<Type> LookupTag<Type> (string tag) where Type : IResource
        {
            return LookupTag (tag).OfType<Type>();
        }

        public IEnumerable<IResource> LookupSignature (uint signature)
        {
            return m_signature_map.GetValues (signature, true);
        }

        public IEnumerable<Type> LookupSignature<Type> (uint signature) where Type : IResource
        {
            return LookupSignature (signature).OfType<Type>();
        }

        /// <summary>
        /// Create GameRes.Entry corresponding to <paramref name="filename"/> extension.
        /// </summary>
        public Entry CreateEntry (string filename)
        {
            Entry entry = null;
            string ext = Path.GetExtension (filename);
            if (null != ext)
            {
                ext = ext.TrimStart ('.').ToUpper();
                var range = m_extension_map.GetValues (ext, false);
                if (null != range)
                    entry = range.First().CreateEntry();
            }
            if (null == entry)
                entry = new Entry();
            entry.Name = filename;
            return entry;
        }

        public string GetTypeFromName (string filename)
        {
            var formats = LookupFileName (filename);
            if (formats.Any())
                return formats.First().Type;
            return "";
        }

        public void InvokeParametersRequest (object source, ParametersRequestEventArgs args)
        {
            if (null != ParametersRequest)
                ParametersRequest (source, args);
        }

        /// <summary>
        /// Read first 4 bytes from stream and return them as 32-bit signature.
        /// </summary>
        public static uint ReadSignature (Stream file)
        {
            file.Position = 0;
            uint signature = (byte)file.ReadByte();
            if (BitConverter.IsLittleEndian)
            {
                signature |= (uint)file.ReadByte() << 8;
                signature |= (uint)file.ReadByte() << 16;
                signature |= (uint)file.ReadByte() << 24;
            }
            else
            {
                signature <<= 24;
                signature |= (uint)file.ReadByte() << 16;
                signature |= (uint)file.ReadByte() << 8;
                signature |= (byte)file.ReadByte();
            }
            return signature;
        }
    }

    public class InvalidFormatException : Exception
    {
        public InvalidFormatException() : base(garStrings.MsgInvalidFormat) { }
        public InvalidFormatException (string msg) : base (msg) { }
    }

    public class UnknownEncryptionScheme : Exception
    {
        public UnknownEncryptionScheme() : base(garStrings.MsgUnknownEncryption) { }
        public UnknownEncryptionScheme (string msg) : base (msg) { }
    }

    public class InvalidEncryptionScheme : Exception
    {
        public InvalidEncryptionScheme() : base(garStrings.MsgInvalidEncryption) { }
        public InvalidEncryptionScheme (string msg) : base (msg) { }
    }
}
