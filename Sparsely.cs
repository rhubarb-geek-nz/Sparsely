// Copyright (c) 2025 Roger Brown.
// Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace RhubarbGeekNz.Sparsely
{
    [Cmdlet(VerbsCommon.Copy, "File")]
    sealed public class CopyFile : PSCmdlet
    {
        private const int FSCTL_QUERY_ALLOCATED_RANGES = 0x000940cf;
        private const int FSCTL_SET_SPARSE = 0x000900c4;
        private const int FSCTL_SET_ZERO_DATA = 0x000980c8;
        private const int ERROR_MORE_DATA = 234;
        private const int ERROR_ALREADY_EXISTS = 183;

        [Parameter(ParameterSetName = "path", Mandatory = true, Position = 0, HelpMessage = "Source path")]
        public string[] Path;

        [Parameter(ParameterSetName = "literal", Mandatory = true, HelpMessage = "Source path")]
        public string[] LiteralPath;

        [Parameter(ParameterSetName = "path", Mandatory = true, Position = 1, HelpMessage = "Target path")]
        [Parameter(ParameterSetName = "literal", Mandatory = true, HelpMessage = "Target path")]
        public string Destination;

        private bool useForce;
        [Parameter(Mandatory = false, HelpMessage = "Use the force, Luke")]
        public SwitchParameter Force
        {
            get
            {
                return useForce;
            }

            set
            {
                useForce = value;
            }
        }

        protected override void ProcessRecord()
        {
            if (Path != null)
            {
                foreach (string path in Path)
                {
                    try
                    {
                        var paths = GetResolvedProviderPathFromPSPath(path, out var providerPath);

                        if ("FileSystem".Equals(providerPath.Name))
                        {
                            foreach (string item in paths)
                            {
                                ProcessFile(item);
                            }
                        }
                        else
                        {
                            WriteError(new ErrorRecord(new Exception($"Provider {providerPath.Name} not handled"), "ProviderError", ErrorCategory.NotImplemented, providerPath));
                        }
                    }
                    catch (ItemNotFoundException ex)
                    {
                        WriteError(new ErrorRecord(ex, ex.GetType().Name, ErrorCategory.ResourceUnavailable, path));
                    }
                }
            }

            if (LiteralPath != null)
            {
                foreach (string literalPath in LiteralPath)
                {
                    try
                    {
                        ProcessFile(GetUnresolvedProviderPathFromPSPath(literalPath));
                    }
                    catch (ItemNotFoundException ex)
                    {
                        WriteError(new ErrorRecord(ex, ex.GetType().Name, ErrorCategory.ResourceUnavailable, literalPath));
                    }
                }
            }
        }

        private void ProcessFile(string source)
        {
            FileAttributes fileAttributes = File.GetAttributes(source);
            string destPath = GetUnresolvedProviderPathFromPSPath(Destination);

            if (Directory.Exists(destPath))
            {
                string fileName = System.IO.Path.GetFileName(source);
                destPath = System.IO.Path.Combine(destPath, fileName);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (fileAttributes.HasFlag(FileAttributes.SparseFile))
                {
                    if ((!useForce) && File.Exists(destPath))
                    {
                        throw new Win32Exception(ERROR_ALREADY_EXISTS);
                    }

                    CopySparseFile(source, destPath);
                }
                else
                {
                    File.Copy(source, destPath, useForce);
                }
            }
            else
            {
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.AddCommand("/bin/cp");
                    shell.AddArgument(source);
                    shell.AddArgument(destPath);
                    shell.Invoke();
                    foreach (var error in shell.Streams.Error)
                    {
                        WriteError(error);
                    }
                }
            }
        }

        void CopySparseFile(string source, string dest)
        {
            int inLength = 16;
            IntPtr inMem = Marshal.AllocCoTaskMem(inLength);

            try
            {
                int outLength = 4096;
                IntPtr outMem = Marshal.AllocCoTaskMem(outLength);

                try
                {
                    using (FileStream inFile = File.OpenRead(source))
                    {
                        long inFileLength = inFile.Length;

                        using (FileStream outFile = File.Create(dest))
                        {
                            byte[] buffer = new byte[0x10000];
                            long offset = 0;
                            bool isMore = true;

                            Marshal.WriteByte(inMem, 1);

                            if (!DeviceIoControl(outFile.SafeFileHandle,
                                FSCTL_SET_SPARSE,
                                inMem, 1,
                                IntPtr.Zero, 0,
                                out int len,
                                IntPtr.Zero))
                            {
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                            }

                            while (isMore)
                            {
                                Marshal.WriteInt64(inMem, 0, offset);
                                Marshal.WriteInt64(inMem, 8, inFileLength);

                                if (!DeviceIoControl(inFile.SafeFileHandle,
                                    FSCTL_QUERY_ALLOCATED_RANGES,
                                    inMem, 16,
                                    outMem, outLength,
                                    out int lenRange,
                                    IntPtr.Zero))
                                {
                                    int err = Marshal.GetLastWin32Error();

                                    if (err != ERROR_MORE_DATA)
                                    {
                                        throw new Win32Exception(err);
                                    }
                                }
                                else
                                {
                                    isMore = false;
                                }

                                int i = 0;

                                while (i < lenRange)
                                {
                                    long off = Marshal.ReadInt64(outMem, i); i += 8;
                                    long length = Marshal.ReadInt64(outMem, i); i += 8;

                                    if (off != offset)
                                    {
                                        if (off < offset)
                                        {
                                            throw new IndexOutOfRangeException();
                                        }

                                        outFile.Flush();

                                        Marshal.WriteInt64(inMem, 0, offset);
                                        Marshal.WriteInt64(inMem, 8, off);

                                        if (!DeviceIoControl(outFile.SafeFileHandle,
                                            FSCTL_SET_ZERO_DATA,
                                            inMem, 16,
                                            IntPtr.Zero, 0,
                                            out len,
                                            IntPtr.Zero))
                                        {
                                            throw new Win32Exception(Marshal.GetLastWin32Error());
                                        }

                                        offset = off;
                                        outFile.Seek(offset, SeekOrigin.Begin);
                                    }

                                    inFile.Seek(off, SeekOrigin.Begin);

                                    while (length > 0)
                                    {
                                        int amount = (int)Math.Min(length, buffer.Length);
                                        amount = inFile.Read(buffer, 0, amount);

                                        outFile.Write(buffer, 0, amount);
                                        length -= amount;
                                        offset += amount;
                                    }
                                }
                            }

                            if (offset < inFileLength)
                            {
                                outFile.Flush();

                                Marshal.WriteInt64(inMem, 0, offset);
                                Marshal.WriteInt64(inMem, 8, inFile.Length);

                                if (!DeviceIoControl(outFile.SafeFileHandle,
                                    FSCTL_SET_ZERO_DATA,
                                    inMem, 16,
                                    IntPtr.Zero, 0,
                                    out len,
                                    IntPtr.Zero))
                                {
                                    throw new Win32Exception(Marshal.GetLastWin32Error());
                                }

                                outFile.SetLength(inFileLength);
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(outMem);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(inMem);
            }
        }

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer, int nInBufferSize,
            IntPtr lpOutBuffer, int nOutBufferSize,
            out int lpBytesReturned, IntPtr lpOverlapped);
    }

    [Cmdlet(VerbsCommon.Get, "CompressedFileSize")]
    sealed public class GetCompressedFileSize : PSCmdlet
    {
        [Parameter(ParameterSetName = "path", Mandatory = true, Position = 0, HelpMessage = "Source path")]
        public string[] Path;

        [Parameter(ParameterSetName = "literal", Mandatory = true, HelpMessage = "Source path")]
        public string[] LiteralPath;

        [Parameter(ParameterSetName = "file", Mandatory = true, ValueFromPipeline = true, HelpMessage = "Source path")]
        public FileInfo[] FileInfo;

        protected override void ProcessRecord()
        {
            if (Path != null)
            {
                foreach (string path in Path)
                {
                    try
                    {
                        var paths = GetResolvedProviderPathFromPSPath(path, out var providerPath);

                        if ("FileSystem".Equals(providerPath.Name))
                        {
                            foreach (string item in paths)
                            {
                                ProcessFile(new FileInfo(item));
                            }
                        }
                        else
                        {
                            WriteError(new ErrorRecord(new Exception($"Provider {providerPath.Name} not handled"), "ProviderError", ErrorCategory.NotImplemented, providerPath));
                        }
                    }
                    catch (ItemNotFoundException ex)
                    {
                        WriteError(new ErrorRecord(ex, ex.GetType().Name, ErrorCategory.ResourceUnavailable, path));
                    }
                }
            }

            if (LiteralPath != null)
            {
                foreach (string literalPath in LiteralPath)
                {
                    try
                    {
                        ProcessFile(new FileInfo(GetUnresolvedProviderPathFromPSPath(literalPath)));
                    }
                    catch (ItemNotFoundException ex)
                    {
                        WriteError(new ErrorRecord(ex, ex.GetType().Name, ErrorCategory.ResourceUnavailable, literalPath));
                    }
                }
            }

            if (FileInfo != null)
            {
                foreach (FileInfo fileInfo in FileInfo)
                {
                    ProcessFile(fileInfo);
                }
            }
        }

        private void ProcessFile(FileInfo fileInfo)
        {
            string path = fileInfo.FullName;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    UInt32 high = 0;
                    UInt32 low = GetCompressedFileSizeW(path, ref high);

                    if (low == UInt32.MaxValue)
                    {
                        int err = Marshal.GetLastWin32Error();

                        if (err != 0)
                        {
                            throw new Win32Exception(err);
                        }
                    }

                    UInt64 total = high;
                    total <<= 32;
                    total += low;
                    WriteFileSize(fileInfo, (long)total);
                }
                else
                {
                    using (PowerShell shell = PowerShell.Create())
                    {
                        shell.AddCommand("/usr/bin/du");
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            shell.AddArgument("--block-size=512");
                        }
                        shell.AddArgument(path);
                        var result = shell.Invoke();
                        foreach (var error in shell.Streams.Error)
                        {
                            WriteError(error);
                        }
                        foreach (var output in result)
                        {
                            string str = output.ToString();
                            int i = 0;
                            while (i < str.Length)
                            {
                                if (Char.IsWhiteSpace(str[i]))
                                {
                                    break;
                                }
                                i++;
                            }
                            long total = Int64.Parse(str.Substring(0, i));
                            total <<= 9;
                            WriteFileSize(fileInfo, total);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, ex.GetType().Name, ErrorCategory.ReadError, null));
            }
        }

        private void WriteFileSize(FileInfo file, long size)
        {
            PSObject obj = new PSObject();
            obj.Members.Add(new PSNoteProperty("Size", size));
            obj.Members.Add(new PSNoteProperty("File", file));
            WriteObject(obj);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern UInt32 GetCompressedFileSizeW(string path, ref UInt32 len);
    }
}
