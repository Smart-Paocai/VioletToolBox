using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WpfApp1
{
    public static class FilteredOpenFileDialog
    {
        private const uint FosForceFileSystem = 0x00000040;
        private const uint FosAllowMultiSelect = 0x00000200;
        private const uint FosPathMustExist = 0x00000800;
        private const uint FosFileMustExist = 0x00001000;
        private const int ErrorCancelled = unchecked((int)0x800704C7);

        public static string[]? Show(
            Window owner,
            string title,
            string? initialDirectory,
            Func<string, bool> includeFile)
        {
            IFileOpenDialog? dialog = null;
            IShellItem? initialFolder = null;
            IShellItemArray? results = null;
            ShellItemFilter? filter = null;
            FileDialogEvents? dialogEvents = null;
            uint eventCookie = 0;

            try
            {
                Guid classId = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
                Guid interfaceId = typeof(IFileOpenDialog).GUID;
                int createResult = CoCreateInstance(
                    ref classId,
                    IntPtr.Zero,
                    1,
                    ref interfaceId,
                    out dialog);
                if (createResult < 0)
                    Marshal.ThrowExceptionForHR(createResult);
                dialog.SetTitle(title);
                dialog.SetFileTypes(2, new[]
                {
                    new ComDlgFilterSpec("RawProgram XML (rawprogram*.xml)", "rawprogram*.xml"),
                    new ComDlgFilterSpec("所有文件 (*.*)", "*.*")
                });
                dialog.SetFileTypeIndex(1);
                dialog.SetOptions(
                    FosForceFileSystem
                    | FosAllowMultiSelect
                    | FosPathMustExist
                    | FosFileMustExist);
                filter = new ShellItemFilter(includeFile);
                dialogEvents = new FileDialogEvents(() =>
                {
                    dialog.GetFileTypeIndex(out uint fileTypeIndex);
                    filter.IsEnabled = fileTypeIndex == 1;
                });
                dialog.Advise(dialogEvents, out eventCookie);
                dialog.SetFilter(filter);

                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    Guid shellItemId = typeof(IShellItem).GUID;
                    SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref shellItemId, out initialFolder);
                    dialog.SetFolder(initialFolder);
                }

                int result = dialog.Show(new WindowInteropHelper(owner).Handle);
                if (result == ErrorCancelled)
                    return null;
                if (result < 0)
                    Marshal.ThrowExceptionForHR(result);

                dialog.GetResults(out results);
                results.GetCount(out uint count);
                var files = new List<string>((int)count);
                for (uint index = 0; index < count; index++)
                {
                    IShellItem? item = null;
                    try
                    {
                        results.GetItemAt(index, out item);
                        string? path = GetFileSystemPath(item);
                        if (!string.IsNullOrWhiteSpace(path) && includeFile(path))
                            files.Add(path);
                    }
                    finally
                    {
                        ReleaseComObject(item);
                    }
                }

                return files.ToArray();
            }
            finally
            {
                if (dialog != null && eventCookie != 0)
                {
                    try
                    {
                        dialog.Unadvise(eventCookie);
                    }
                    catch
                    {
                    }
                }
                ReleaseComObject(results);
                ReleaseComObject(initialFolder);
                ReleaseComObject(dialog);
                GC.KeepAlive(filter);
                GC.KeepAlive(dialogEvents);
            }
        }

        private static string? GetFileSystemPath(IShellItem item)
        {
            IntPtr pathPointer = IntPtr.Zero;
            try
            {
                item.GetDisplayName(Sigdn.FileSystemPath, out pathPointer);
                return pathPointer == IntPtr.Zero
                    ? null
                    : Marshal.PtrToStringUni(pathPointer);
            }
            catch (COMException)
            {
                return null;
            }
            finally
            {
                if (pathPointer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        [ComDefaultInterface(typeof(IShellItemFilter))]
        public sealed class ShellItemFilter : IShellItemFilter
        {
            private readonly Func<string, bool> _includeFile;
            public bool IsEnabled { get; set; } = true;

            public ShellItemFilter(Func<string, bool> includeFile)
            {
                _includeFile = includeFile;
            }

            public int IncludeItem(IShellItem item)
            {
                try
                {
                    if (!IsEnabled)
                        return 0;

                    string? path = GetFileSystemPath(item);
                    if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
                        return 0;

                    return _includeFile(path) ? 0 : 1;
                }
                catch
                {
                    return 0;
                }
            }

            public int GetEnumFlagsForItem(IShellItem item, out uint flags)
            {
                flags = 0;
                return 0;
            }
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        [ComDefaultInterface(typeof(IFileDialogEvents))]
        public sealed class FileDialogEvents : IFileDialogEvents
        {
            private readonly Action _typeChanged;

            public FileDialogEvents(Action typeChanged)
            {
                _typeChanged = typeChanged;
            }

            public int OnFileOk(IntPtr dialog) => 0;

            public int OnFolderChanging(IntPtr dialog, IShellItem folder) => 0;

            public void OnFolderChange(IntPtr dialog)
            {
            }

            public void OnSelectionChange(IntPtr dialog)
            {
            }

            public void OnShareViolation(IntPtr dialog, IShellItem item, out uint response)
            {
                response = 0;
            }

            public void OnTypeChange(IntPtr dialog)
            {
                _typeChanged();
            }

            public void OnOverwrite(IntPtr dialog, IShellItem item, out uint response)
            {
                response = 0;
            }
        }

        public enum Sigdn : uint
        {
            FileSystemPath = 0x80058000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private readonly struct ComDlgFilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public readonly string Name;

            [MarshalAs(UnmanagedType.LPWStr)]
            public readonly string Spec;

            public ComDlgFilterSpec(string name, string spec)
            {
                Name = name;
                Spec = spec;
            }
        }


        [ComImport]
        [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(
                uint fileTypeCount,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ComDlgFilterSpec[] filterSpec);

            void SetFileTypeIndex(uint fileTypeIndex);
            void GetFileTypeIndex(out uint fileTypeIndex);
            void Advise([MarshalAs(UnmanagedType.Interface)] IFileDialogEvents events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid clientGuid);
            void ClearClientData();
            void SetFilter([MarshalAs(UnmanagedType.Interface)] IShellItemFilter filter);
            void GetResults(out IShellItemArray results);
            void GetSelectedItems(out IShellItemArray items);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(Sigdn displayName, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem item, uint hint, out int order);
        }

        [ComImport]
        [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr result);
            void GetPropertyStore(int flags, ref Guid interfaceId, out IntPtr propertyStore);
            void GetPropertyDescriptionList(ref IntPtr propertyKey, ref Guid interfaceId, out IntPtr descriptionList);
            void GetAttributes(uint flags, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
        }

        [ComVisible(true)]
        [Guid("2659B475-EEB8-48B7-8F07-B378810F48CF")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemFilter
        {
            [PreserveSig]
            int IncludeItem([MarshalAs(UnmanagedType.Interface)] IShellItem item);

            [PreserveSig]
            int GetEnumFlagsForItem([MarshalAs(UnmanagedType.Interface)] IShellItem item, out uint flags);
        }

        [ComVisible(true)]
        [Guid("973510DB-7D7F-452B-8975-74A85828D354")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileDialogEvents
        {
            [PreserveSig]
            int OnFileOk(IntPtr dialog);

            [PreserveSig]
            int OnFolderChanging(IntPtr dialog, [MarshalAs(UnmanagedType.Interface)] IShellItem folder);

            void OnFolderChange(IntPtr dialog);
            void OnSelectionChange(IntPtr dialog);
            void OnShareViolation(
                IntPtr dialog,
                [MarshalAs(UnmanagedType.Interface)] IShellItem item,
                out uint response);
            void OnTypeChange(IntPtr dialog);
            void OnOverwrite(
                IntPtr dialog,
                [MarshalAs(UnmanagedType.Interface)] IShellItem item,
                out uint response);
        }

        [DllImport("ole32.dll", PreserveSig = true)]
        private static extern int CoCreateInstance(
            ref Guid classId,
            IntPtr outer,
            uint context,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IFileOpenDialog dialog);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindContext,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);
    }
}
