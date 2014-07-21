MSCS = D:/WINDOWS/Microsoft.NET/Framework/v4.0.30319/csc //nologo

.SUFFIXES: .cs .exe

all: GARbro

adler32: adler32.cs
	$(MSCS) $(MSCSFLAGS) //out:$@.exe $^

inflate: inflate.cs
	$(MSCS) $(MSCSFLAGS) //out:$@.exe $^ //r:zlib\\zlibnet.dll

deflate: deflate.cs
	$(MSCS) $(MSCSFLAGS) //out:$@.exe $^ //r:zlib\\zlibnet.dll

GARbro: Program.cs GameRes.cs ArcXFL.cs
	$(MSCS) $(MSCSFLAGS) //out:$@.exe $^ //r:System.ComponentModel.Composition.dll //r:System.ComponentModel.DataAnnotations.dll

tags:
	ctags *.cs
