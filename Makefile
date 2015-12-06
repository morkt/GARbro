MSNETDIR = C:/WINDOWS/Microsoft.NET/Framework/v4.0.30319
MSCS = $(MSNETDIR)/csc //nologo
MSBUILD = $(MSNETDIR)/MSBuild.exe //nologo

.SUFFIXES: .cs .exe

all: GARbro.GUI

adler32: adler32.cs
	$(MSCS) $(MSCSFLAGS) //out:$@.exe $^

inflate: inflate.cs
	$(MSCS) $(MSCSFLAGS) //out:$@.exe $^ //r:zlib\\zlibnet.dll

deflate: deflate.cs
	$(MSCS) $(MSCSFLAGS) //out:$@.exe $^ //r:zlib\\zlibnet.dll

GARbro.GUI:
	$(MSBUILD) //p:Configuration=Debug //v:m GARbro.GUI.csproj

GARbro: Program.cs GameRes.cs ArcXFL.cs
	$(MSCS) $(MSCSFLAGS) //out:$@.exe $^ //r:System.ComponentModel.Composition.dll //r:System.ComponentModel.DataAnnotations.dll

tags:
	ctags *.cs
