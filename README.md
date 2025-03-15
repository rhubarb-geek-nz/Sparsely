# rhubarb-geek-nz/Sparsely
Sparse file aware tool for PowerShell

# Copy-File

Copy a file using either [File.Copy](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.copy?view=netstandard-2.0) or a sparse aware method.

# Get-CompressedFileSize

Uses the Win32 API [GetCompressedFileSize](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) to return the size of the file on disk.

# Syntax

```
Copy-File -LiteralPath <string[]> -Destination <string> [-Force]

Copy-File [-Path] <string[]> -Destination <string> [-Force]

Get-CompressedFileSize [-LiteralPath] <string>
```
