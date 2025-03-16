# rhubarb-geek-nz/Sparsely
Sparse file aware tool for PowerShell

# Copy-File on Windows

Copy a file using either [File.Copy](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.copy?view=netstandard-2.0) or a sparse aware method.

See [Sparsely.cs](Sparsely.cs) for details.

# Copy-File on Linux/OSX

Uses `/bin/cp` as this deals with sparse files.

# Get-CompressedFileSize on Windows

Uses the Win32 API [GetCompressedFileSize](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) to return the size of the file on disk.

# Get-CompressedFileSize

Uses `/usr/bin/du` and multiplies by 1024 on Linux and 512 on others.

# Syntax

```
Copy-File [-Path] <string[]> -Destination <string> [-Force]

Copy-File -LiteralPath <string[]> -Destination <string> [-Force]

Get-CompressedFileSize [-LiteralPath] <string>
```
