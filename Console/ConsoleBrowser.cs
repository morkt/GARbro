using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using GameRes;
// ReSharper disable LocalizableElement

namespace GARbro {
	public enum ExistingFileAction {
		Ask,
		Skip,
		Overwrite,
		Rename
	}

	class ConsoleBrowser {
		private string outputDirectory;

		private Regex fileFilter;
		private ImageFormat imageFormat;
		private bool autoImageFormat = false;
		private bool ignoreErrors = true;
		private bool skipImages;
		private bool skipScript;
		private bool skipAudio;
		private bool convertAudio;
		private bool adjustImageOffset;

		private ExistingFileAction existingFileAction = ExistingFileAction.Ask;
		
		public static readonly HashSet<string> CommonAudioFormats = new HashSet<string> {"wav", "mp3", "ogg"};
		public static readonly HashSet<string> CommonImageFormats = new HashSet<string> {"jpeg", "png", "bmp", "tga"};

		private void ListFormats() {
			Console.WriteLine("Recognized resource formats:\n");
			foreach (var format in FormatCatalog.Instance.ArcFormats.OrderBy(format => format.Tag)) {
				Console.WriteLine("{0,-20} {1}", format.Tag, format.Description);
			}
		}

		private void ListFiles(Entry[] fileList) {
			Console.WriteLine("     Offset      Size  Name");
			Console.WriteLine(" ----------  --------  ------------------------------------------------------");
			foreach (var entry in fileList) {
				Console.WriteLine(" [{1:X8}] {0,9}  {2}", entry.Offset, entry.Size, entry.Name);
			}

			Console.WriteLine(" ----------  --------  ------------------------------------------------------");
			Console.WriteLine($"                       {fileList.Length} files");
		}

		private void ExtractFiles(Entry[] fileList, ArcFile arc) {
			Directory.CreateDirectory(outputDirectory);

			var iSkipped = 0;
			for (var i = 0; i < fileList.Length; i++) {
				var entry = fileList[i];
				Console.WriteLine(string.Format("[{0}/{1}] {2}", i + 1, fileList.Length, entry.Name));
				
				try {
					if (imageFormat != null && entry.Type == "image") {
						ExtractImage(arc, entry, imageFormat);
					}
					else if (convertAudio && entry.Type == "audio") {
						ExtractAudio(arc, entry);
					}
					else {
						using (var input = arc.OpenEntry(entry))
						using (var output = CreateNewFile(entry.Name))
							input.CopyTo(output);
					}
				}
				catch (TargetException) {
					iSkipped++;
				}
#if !DEBUG
				catch (Exception e) {
					PrintError(string.Format($"Failed to extract {entry.Name}: {e.Message}"));
					if (!ignoreErrors) return;

					iSkipped++;
				}
#endif
			}

			Console.WriteLine();
			Console.WriteLine(iSkipped > 0? iSkipped + " files were skipped": "All OK");
		}

		void ExtractImage(ArcFile arc, Entry entry, ImageFormat targetFormat) {
			using (var decoder = arc.OpenImage(entry)) {
				var src_format = decoder.SourceFormat; // could be null

				if (autoImageFormat && src_format != null) targetFormat = CommonImageFormats.Contains(src_format.Tag.ToLower())? src_format: ImageFormat.Png;

				var target_ext = targetFormat.Extensions.FirstOrDefault() ?? "";
				var outputName = Path.ChangeExtension(entry.Name, target_ext);
				if (src_format == targetFormat) {
					// source format is the same as a target, copy file as is
					using (var output = CreateNewFile(outputName)) decoder.Source.CopyTo(output);
					return;
				}

				var image = decoder.Image;
				if (adjustImageOffset) image = AdjustImageOffset(image);

				using (var outfile = CreateNewFile(outputName)) {
					targetFormat.Write(outfile, image);
				}
			}
		}

		static ImageData AdjustImageOffset(ImageData image) {
			if (0 == image.OffsetX && 0 == image.OffsetY) return image;
			var width = (int) image.Width + image.OffsetX;
			var height = (int) image.Height + image.OffsetY;
			if (width <= 0 || height <= 0) return image;

			var x = Math.Max(image.OffsetX, 0);
			var y = Math.Max(image.OffsetY, 0);
			var src_x = image.OffsetX < 0? Math.Abs(image.OffsetX): 0;
			var src_y = image.OffsetY < 0? Math.Abs(image.OffsetY): 0;
			var src_stride = (int) image.Width * (image.BPP + 7) / 8;
			var dst_stride = width * (image.BPP + 7) / 8;
			var pixels = new byte[height * dst_stride];
			var offset = y * dst_stride + x * image.BPP / 8;
			var rect = new Int32Rect(src_x, src_y, (int) image.Width - src_x, 1);
			for (var row = src_y; row < image.Height; ++row) {
				rect.Y = row;
				image.Bitmap.CopyPixels(rect, pixels, src_stride, offset);
				offset += dst_stride;
			}

			var bitmap = BitmapSource.Create(width, height, image.Bitmap.DpiX, image.Bitmap.DpiY,
				image.Bitmap.Format, image.Bitmap.Palette, pixels, dst_stride);
			return new ImageData(bitmap);
		}

		void ExtractAudio(ArcFile arc, Entry entry) {
			using (var file = arc.OpenBinaryEntry(entry))
			using (var sound = AudioFormat.Read(file)) {
				if (sound == null) throw new InvalidFormatException("Unable to interpret audio format");
				ConvertAudio(entry.Name, sound);
			}
		}

		public void ConvertAudio(string filename, SoundInput input) {
			var source_format = input.SourceFormat;
			if (CommonAudioFormats.Contains(source_format)) {
				var output_name = Path.ChangeExtension(filename, source_format);
				using (var output = CreateNewFile(output_name)) {
					input.Source.Position = 0;
					input.Source.CopyTo(output);
				}
			}
			else {
				var output_name = Path.ChangeExtension(filename, "wav");
				using (var output = CreateNewFile(output_name)) AudioFormat.Wav.Write(input, output);
			}
		}

		protected Stream CreateNewFile(string filename) {
			var path = Path.Combine(outputDirectory, filename);
			path = Path.GetFullPath(path);
			Directory.CreateDirectory(Path.GetDirectoryName(path));

			if (File.Exists(path)) {
				path = OverwritePrompt(path);
				if (path == null) throw new TargetException();
			}

			return File.Open(path, FileMode.Create);
		}

		void Run(string[] args) {
			var command = args.Length < 1? "h": args[0];

			switch (command) {
				case "h":
				case "-h":
				case "--help":
				case "/?":
				case "-?":
					Usage();
					return;
				case "f":
					ListFormats();
					return;
			}

			if (command.Length != 1) {
				PrintError(File.Exists(command)? "No command specified. Use -h command line parameter to show help.": "Invalid command: " + command);
				return;
			}
			if (args.Length < 2) {
				PrintError("No archive file specified");
				return;
			}

			var inputFile = args[args.Length - 1];
			if (!File.Exists(inputFile)) {
				PrintError("Input file " + inputFile + " does not exist");
				return;
			}

			var argLength = args.Length - 1;
			outputDirectory = Directory.GetCurrentDirectory();
			for (var i = 1; i < argLength; i++) {
				switch (args[i]) {
					case "-o":
						i++;
						if (i >= argLength) {
							PrintError("No output directory specified");
							return;
						}
						outputDirectory = args[i];
						if (File.Exists(outputDirectory)) {
							PrintError("Invalid output directory");
							return;
						}

						//Directory.SetCurrentDirectory(outputDirectory);
						break;
					case "-f":
						i++;
						if (i >= argLength) {
							PrintError("No filter specified");
							return;
						}

						try {
							fileFilter = new Regex(args[i]);
						}
						catch (ArgumentException e) {
							PrintError("Invalid filter: " + e.Message);
							return;
						}

						break;
					case "-if":
						i++;
						var formatTag = args[i].ToUpper();
						if (formatTag == "JPG") formatTag = "JPEG";

						imageFormat = ImageFormat.FindByTag(formatTag);
						if (imageFormat == null) {
							PrintError("Unknown image format specified: " + args[i]);
							return;
						}
						break;
					case "-ca":
						convertAudio = true;
						break;
					case "-na":
						skipAudio = true;
						break;
					case "-ni":
						skipImages = true;
						break;
					case "-ns":
						skipScript = true;
						break;
					case "-aio":
						adjustImageOffset = true;
						break;
					case "-ocu":
						autoImageFormat = true;
						break;
					default:
						Console.WriteLine("Warning: Unknown command line parameter: " + args[i]);
						return;
				}
			}

			if (autoImageFormat && imageFormat == null) {
				PrintError("The parameter -ocu requires the image format (-if parameter) to be set");
				return;
			}

			DeserializeGameData();

			try {
				VFS.ChDir(inputFile);
			}
			catch (Exception) {
				PrintError("Input file has an unknown format");
				return;
			}

			var m_fs = (ArchiveFileSystem) VFS.Top;
			var fileList = m_fs.GetFilesRecursive().Where(e => e.Offset >= 0);

			if (skipImages || skipScript || skipAudio|| fileFilter != null) {
				fileList = fileList.Where(f => !(skipImages && f.Type == "image") &&
											   !(skipScript && f.Type == "script") &&
											   !(skipAudio && f.Type == "audio") &&
											   (fileFilter == null || fileFilter.IsMatch(f.Name)));
			}

			if (!fileList.Any()) {
				var hasFilter = skipAudio || skipImages || skipScript || fileFilter != null;
				PrintError(hasFilter? "No files match the given filter": "Archive is empty");
				return;
			}

			var fileArray = fileList.OrderBy(e => e.Offset).ToArray();

			Console.WriteLine(fileArray[0].Offset);

			switch (command) {
				case "i":
					Console.WriteLine(m_fs.Source.Tag);
					break;
				case "l":
					ListFiles(fileArray);
					break;
				case "x":
					ExtractFiles(fileArray, m_fs.Source);
					break;
			}
		}

		void DeserializeGameData() {
			var scheme_file = Path.Combine(FormatCatalog.Instance.DataDirectory, "Formats.dat");
			try {
				using (var file = File.OpenRead(scheme_file)) FormatCatalog.Instance.DeserializeScheme(file);
			}
			catch (Exception) {
				//Console.Error.WriteLine("Scheme deserialization failed: {0}", e.Message);
			}
		}
		
		static void Usage() {
			Console.WriteLine(string.Format("Usage: {0} <command> [<switches>...] <archive_name>", Process.GetCurrentProcess().ProcessName));
			Console.WriteLine("\nCommands:");
			Console.WriteLine("  i   Identify archive format");
			Console.WriteLine("  f   List supported formats");
			Console.WriteLine("  l   List contents of archive");
			Console.WriteLine("  x   Extract files from archive");
			Console.WriteLine("\nSwitches:");
			Console.WriteLine("  -o <Directory>   Set output directory for extraction");
			Console.WriteLine("  -f <Filter>	   Only process files matching the regular expression <Filter>");
			Console.WriteLine("  -if <Format>	   Set image output format  (e.g. 'png', 'jpg', 'bmp')");
			Console.WriteLine("  -ca       	   Convert audio files to wav format");
			Console.WriteLine("  -na       	   Ignore audio files");
			Console.WriteLine("  -ni       	   Ignore image files");
			Console.WriteLine("  -ns       	   Ignore scripts");
			Console.WriteLine("  -aio       	   Adjust image offset");
			Console.WriteLine("  -ocu       	   Set -if switch to only convert unknown image formats");
			Console.WriteLine();
			//Console.WriteLine(FormatCatalog.Instance.ArcFormats.Count() + " supported formats");
		}

		static void PrintError(string msg) {
			Console.WriteLine("Error: " + msg);
		}

		string OverwritePrompt(string filename) {
			switch (existingFileAction) {
				
				case ExistingFileAction.Skip:
					return null;
				case ExistingFileAction.Overwrite:
					return filename;
				case ExistingFileAction.Rename:
					return GetPathToRename(filename);
			}

			Console.WriteLine(string.Format($"The file {filename} already exists. Overwrite? [Y]es | [N]o | [A]lways | n[E]ver | [R]ename | A[l]ways rename"));

			while (true) {
				switch (Console.Read()) {
					case 'y':
					case 'Y':
						return filename;
					case 'n':
					case 'N':
						return null;
					case 'a':
					case 'A':
						existingFileAction = ExistingFileAction.Overwrite;
						return filename;
					case 'e':
					case 'E':
						existingFileAction = ExistingFileAction.Skip;
						return null;
					case 'r':
					case 'R':
						return GetPathToRename(filename);
					case 'l':
					case 'L':
						existingFileAction = ExistingFileAction.Rename;
						return GetPathToRename(filename);
				}
			}
		}

		string GetPathToRename(string path) {
			var directory = Path.GetDirectoryName(path);
			var fileName = Path.GetFileNameWithoutExtension(path);
			var fileExtension = Path.GetExtension(path);

			var i = 2;
			do {
				path = Path.Combine(directory, string.Format($"{fileName} ({i}){fileExtension}"));
				i++;
			} while (File.Exists(path));

			return path;
		}

		private static void OnParametersRequest(object sender, ParametersRequestEventArgs eventArgs) {
			// Some archives are encrypted or require parameters to be set.
			// Let's just use the default values for now.
			var format = (IResource) sender;
			//Console.WriteLine(eventArgs.Notice);
			eventArgs.InputResult = true;
			eventArgs.Options = format.GetDefaultOptions();
		}

		static void Main(string[] args) {
			Console.OutputEncoding = Encoding.UTF8;
			Console.WriteLine(string.Format("GARbro - Game Resource browser, version {0}\n2014-2019 by mørkt, published under a MIT license", Assembly.GetAssembly(typeof(FormatCatalog)).GetName().Version));
			Console.WriteLine("-----------------------------------------------------------------------------\n");

			FormatCatalog.Instance.ParametersRequest += OnParametersRequest;
			//var listener = new TextWriterTraceListener(Console.Error);
			//Trace.Listeners.Add(listener);

			var browser = new ConsoleBrowser();
			browser.Run(args);

#if DEBUG
			Console.Read();
#endif
		}
	}
}