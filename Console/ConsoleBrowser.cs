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

	public enum ConsoleCommand {
		Invalid,
		Info,
		List,
		Extract
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

		private void Archive_ExtractFiles(Entry[] fileList, ArcFile arc) {
			Directory.CreateDirectory(outputDirectory);

			var iSkipped = 0;
			for (var i = 0; i < fileList.Length; i++) {
				var entry = fileList[i];
				PrintProgress(i + 1, fileList.Length, entry.Name);

				try {
					try {
						if (imageFormat != null && entry.Type == "image") {
							Archive_ExtractConvertedImage(arc, entry);
						}
						else if (convertAudio && entry.Type == "audio") {
							Archive_ExtractConvertedAudio(arc, entry);
						}
						else {
							Archive_ExtractRaw(arc, entry);
						}
					}
					catch (TargetException) {
						iSkipped++;
					}
					catch (InvalidFormatException) {
						PrintWarning("Invalid file format, extracting as raw");
						Archive_ExtractRaw(arc, entry);
					}
				}
				catch (Exception e) {
					PrintError(string.Format($"Failed to extract {entry.Name}: {e.Message}"));
					if (!ignoreErrors) return;

					iSkipped++;

#if DEBUG
					throw e;
#endif
				}
			}

			Console.WriteLine();
			Console.WriteLine(iSkipped > 0? iSkipped + " files were skipped": "All OK");
		}

		void Archive_ExtractConvertedImage(ArcFile arc, Entry entry) {
			var targetFormat = imageFormat ?? ImageFormat.Png;

			using (var decoder = arc.OpenImage(entry)) {
				var sourceFormat = decoder.SourceFormat; // Can be null
				if (autoImageFormat && sourceFormat != null) targetFormat = CommonImageFormats.Contains(sourceFormat.Tag.ToLower())? sourceFormat: ImageFormat.Png;

				var outputExtension = targetFormat.Extensions.FirstOrDefault() ?? "";
				var outputName = Path.ChangeExtension(entry.Name, outputExtension);
				if (sourceFormat == targetFormat) {
					// Source format is the same as a target, copy file as is
					using (var outputStream = CreateNewFile(outputName)) {
						if (outputStream == null) return;
						decoder.Source.CopyTo(outputStream);
					}
					return;
				}

				var image = decoder.Image;
				if (adjustImageOffset) image = AdjustImageOffset(image);

				using (var outputStream = CreateNewFile(outputName)) {
					if (outputStream == null) return;
					targetFormat.Write(outputStream, image);
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

		void Archive_ExtractConvertedAudio(ArcFile arc, Entry entry) {
			using (var file = arc.OpenBinaryEntry(entry))
			using (var sound = AudioFormat.Read(file)) {
				if (sound == null) throw new InvalidFormatException("Unable to interpret audio format");
				ConvertAudio(entry.Name, sound);
			}
		}

		void Archive_ExtractRaw(ArcFile arc, Entry entry) {
			using (var input = arc.OpenEntry(entry))
			using (var outputStream = CreateNewFile(entry.Name)) {
				if (outputStream == null) return;
				input.CopyTo(outputStream);
			}
		}

		void ConvertAudio(string fileName, SoundInput soundInput) {
			var sourceFormat = soundInput.SourceFormat;

			if (CommonAudioFormats.Contains(sourceFormat)) {
				var outputName = Path.ChangeExtension(fileName, sourceFormat);
				using (var outputStream = CreateNewFile(outputName)) {
					if (outputStream == null) return;
					soundInput.Source.Position = 0;
					soundInput.Source.CopyTo(outputStream);
				}
			}
			else {
				var outputName = Path.ChangeExtension(fileName, "wav");
				using (var outputStream = CreateNewFile(outputName)) {
					if (outputStream == null) return;
					AudioFormat.Wav.Write(soundInput, outputStream);
				}
			}
		}

		bool ConvertAudio(string fileName, IBinaryStream binaryStream) {
			using (var soundInput = AudioFormat.Read(binaryStream)) {
				if (soundInput == null) return false;

				Console.WriteLine($"Converting {soundInput.SourceFormat} audio");
				ConvertAudio(fileName, soundInput);
			}

			return true;
		}

		bool ConvertFile(string file) {
			var fileName = Path.GetFileName(file);
			PrintProgress(1, 1, fileName);

			try {
				using (var binaryStream = BinaryStream.FromFile(file)) {
					if (!skipImages && ConvertImage(fileName, binaryStream)) return true;
					if (!skipAudio && ConvertAudio(fileName, binaryStream)) return true;
					throw new UnknownFormatException();
				}
			}
			catch(Exception e) {
				PrintError("Failed to convert file: " + e.Message);
			}

			return false;
		}

		bool ConvertImage(string fileName, IBinaryStream binaryStream) {
			var sourceImageFormatTuple = ImageFormat.FindFormat(binaryStream);
			if (sourceImageFormatTuple == null) return false;

			var sourceImageFormat = sourceImageFormatTuple.Item1;
			var targetImageFormat = imageFormat;

			binaryStream.Position = 0;
			var imageData = sourceImageFormat.Read(binaryStream, sourceImageFormatTuple.Item2);

			if (autoImageFormat && sourceImageFormat != null) targetImageFormat = CommonImageFormats.Contains(sourceImageFormat.Tag.ToLower())? sourceImageFormat: ImageFormat.Png;

			//Console.WriteLine($"Converting {sourceImageFormat.Tag} to {targetImageFormat.Tag}");
			var outputExtension = targetImageFormat.Extensions.FirstOrDefault() ?? "";
			var outputName = Path.ChangeExtension(fileName, outputExtension);
			
			if (sourceImageFormat == targetImageFormat) {
				PrintWarning("Input and output format are identical. No conversion neccessary");
				return true;
			}

			using (var outputStream = CreateNewFile(outputName)) {
				if (outputStream == null) return true;
				imageFormat.Write(outputStream, imageData);
			}

			return true;
		}

		Stream CreateNewFile(string filename) {
			var path = Path.Combine(outputDirectory, filename);
			path = Path.GetFullPath(path);
			Directory.CreateDirectory(Path.GetDirectoryName(path));

			if (File.Exists(path)) {
				path = OverwritePrompt(path);
				if (path == null) return null;
			}

			return File.Open(path, FileMode.Create);
		}

		void Run(string[] args) {
			var commandString = args.Length < 1? "h": args[0];
			ConsoleCommand command;

			switch (commandString) {
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
				case "i":
					command = ConsoleCommand.Info;
					break;
				case "l":
					command = ConsoleCommand.List;
					break;
				case "x":
					command = ConsoleCommand.Extract;
					break;
				default:
					PrintError(File.Exists(commandString) ? "No command specified. Use -h command line parameter to show help." : "Invalid command: " + commandString);
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
				//Console.WriteLine("Input file is not an archive");
				ProcessNonArchiveFile(inputFile, command);
				return;
			}

			ProcessArchiveFile(command);
		}

		/// <summary>
		/// Helper function used if the input file is an archive
		/// </summary>
		/// <param name="command"></param>
		void ProcessArchiveFile(ConsoleCommand command) {
			var archiveFileSystem = (ArchiveFileSystem) VFS.Top;
			var fileList = archiveFileSystem.GetFilesRecursive().Where(e => e.Offset >= 0);

			if (skipImages || skipScript || skipAudio || fileFilter != null) {
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
				case ConsoleCommand.Info:
					Console.WriteLine(archiveFileSystem.Source.Tag);
					break;
				case ConsoleCommand.List:
					ListFiles(fileArray);
					break;
				case ConsoleCommand.Extract:
					Archive_ExtractFiles(fileArray, archiveFileSystem.Source);
					break;
			}
		}

		/// <summary>
		/// Helper function used if the input file is not an archive, e.g. encrypted image/audio.
		/// Theses files use other data structures and need to implement the command line actions differently.
		/// </summary>
		/// <param name="file">Full path to the input file</param>
		/// <param name="command"></param>
		void ProcessNonArchiveFile(string file, ConsoleCommand command) {
			var fileName = Path.GetFileName(file);
			if (fileFilter != null && fileFilter.IsMatch(fileName)) PrintError("No files match the given filter");

			switch (command) {
				case ConsoleCommand.Info:
					Console.WriteLine(IdentifyNonArchiveFile(file));
					break;
				case ConsoleCommand.List:
					if (IdentifyNonArchiveFile(file) == null) break;

					var entry = new Entry() {
						Name = fileName,
						Offset = 0
					};
					ListFiles(new Entry[] { entry });
					break;
				case ConsoleCommand.Extract:
					if (ConvertFile(file)) Console.WriteLine("All OK");
					break;
			}
		}

		string IdentifyNonArchiveFile(string file) {
			try {
				using (var binaryStream = BinaryStream.FromFile(file)) {
					try {
						var image = ImageFormat.FindFormat(binaryStream);
						if (image != null) return image.Item1.Tag;
					}
					catch (Exception) {}

					try {
						var audio = AudioFormat.Read(binaryStream);
						if (audio != null) return audio.SourceFormat;
					}
					catch (Exception) {}

					throw new UnknownFormatException();
				}
			}
			catch (Exception) {
				PrintError("Input file has an unknown format");
			}

			return null;
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

		static void PrintProgress(int current, int total, string fileName) {
			Console.WriteLine(string.Format("[{0}/{1}] {2}", current, total, fileName));
		}

		static void PrintWarning(string msg) {
			Console.WriteLine("Warning: " + msg);
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
				var key = Console.ReadKey().KeyChar;
				Console.WriteLine();
				switch (key) {
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
			Console.WriteLine(string.Format("GARbro - Game Resource browser, version {0}\n2014-2020 by mørkt, published under a MIT license", Assembly.GetAssembly(typeof(FormatCatalog)).GetName().Version));
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