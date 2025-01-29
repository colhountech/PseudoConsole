using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

/*
 * This is Claud's attempte of fixing the code
 * using Claude 3.5 Sonnet
 */
namespace PseudoConsoleExample
{
    class Program
    {
        static void Main(string[] args)
        {
            var command = "dir /s /b";

            using (var pseudoConsole = PseudoConsole.Create())
            {
                pseudoConsole.Run(command);
            }
        }
    }

    public class PseudoConsole : IDisposable
    {
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        private readonly SafeFileHandle _inputReadSide;
        private readonly SafeFileHandle _inputWriteSide;
        private readonly SafeFileHandle _outputReadSide;
        private readonly SafeFileHandle _outputWriteSide;
        private readonly IntPtr _pseudoConsoleHandle;
        // Change this line:
        //private readonly STARTUPINFOEX _startupInfo;
        // To this:
        private STARTUPINFOEX _startupInfo;  // Remove readonly

        private  IntPtr _processHandle;
        private  IntPtr _threadHandle;

        private PseudoConsole(
            SafeFileHandle inputReadSide,
            SafeFileHandle inputWriteSide,
            SafeFileHandle outputReadSide,
            SafeFileHandle outputWriteSide,
            IntPtr pseudoConsoleHandle,
            STARTUPINFOEX startupInfo)
        {
            _inputReadSide = inputReadSide;
            _inputWriteSide = inputWriteSide;
            _outputReadSide = outputReadSide;
            _outputWriteSide = outputWriteSide;
            _pseudoConsoleHandle = pseudoConsoleHandle;
            _startupInfo = startupInfo;
        }

        public static PseudoConsole Create()
        {
            // Create the pipes
            SECURITY_ATTRIBUTES securAttr = new SECURITY_ATTRIBUTES();
            securAttr.nLength = Marshal.SizeOf(securAttr);
            securAttr.bInheritHandle = true;

            if (!CreatePipe(out SafeFileHandle inputReadSide, out SafeFileHandle inputWriteSide, ref securAttr, 0))
                throw new InvalidOperationException("Failed to create input pipe");

            if (!CreatePipe(out SafeFileHandle outputReadSide, out SafeFileHandle outputWriteSide, ref securAttr, 0))
                throw new InvalidOperationException("Failed to create output pipe");

            // Create the pseudo console
            var size = new COORD { X = 120, Y = 30 };
            int createResult = CreatePseudoConsole(
                size,
                inputReadSide.DangerousGetHandle(),
                outputWriteSide.DangerousGetHandle(),
                ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN,
                out IntPtr hPC);

            if (createResult != 0)
                throw new InvalidOperationException($"Failed to create pseudo console: {createResult}");

            // Prepare startup info
            var startupInfo = new STARTUPINFOEX();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

            var lpSize = IntPtr.Zero;
            var success = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
            startupInfo.lpAttributeList = Marshal.AllocHGlobal(lpSize);

            success = InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref lpSize);
            if (!success)
                throw new InvalidOperationException("Failed to initialize attribute list");

            success = UpdateProcThreadAttribute(
                startupInfo.lpAttributeList,
                0,
                (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero);

            if (!success)
                throw new InvalidOperationException("Failed to update attribute list");

            return new PseudoConsole(
                inputReadSide,
                inputWriteSide,
                outputReadSide,
                outputWriteSide,
                hPC,
                startupInfo);
        }

        public void Run(string command)
        {
            // Change the command to use cmd.exe explicitly
            var cmdPath = System.IO.Path.Combine(
                Environment.SystemDirectory,
                "cmd.exe"
            );
            var commandLine = $"{cmdPath} /c {command}";


            var processInfo = new PROCESS_INFORMATION();
            try
            {
                var success = CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    null,
                    ref _startupInfo,
                    out processInfo);



                if (!success)
                    throw new InvalidOperationException($"Failed to create process: {Marshal.GetLastWin32Error()}");

                // Store the handles for cleanup
                _processHandle = processInfo.hProcess;
                _threadHandle = processInfo.hThread;


                // Start reading thread
                var outputThread = new Thread(() =>
                {
                    var buffer = new byte[4096];
                    while (true)
                    {
                        var read = 0;
                        var success = ReadFile(
                            _outputReadSide.DangerousGetHandle(),
                            buffer,
                            buffer.Length,
                            out read,
                            IntPtr.Zero);

                        if (!success || read == 0)
                            break;

                        Console.Write(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
                    }
                });


                outputThread.Start();

                // Wait for the process to exit
                WaitForSingleObject(processInfo.hProcess, INFINITE);
                outputThread.Join();
            }
            catch
            {
                if (processInfo.hProcess != IntPtr.Zero)
                    CloseHandle(processInfo.hProcess);
                if (processInfo.hThread != IntPtr.Zero)
                    CloseHandle(processInfo.hThread);
                throw;
            }
        }

        public void Dispose()
        {
            if (_pseudoConsoleHandle != IntPtr.Zero)
                ClosePseudoConsole(_pseudoConsoleHandle);

            if (_startupInfo.lpAttributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(_startupInfo.lpAttributeList);
                Marshal.FreeHGlobal(_startupInfo.lpAttributeList);
            }

            if (_processHandle != IntPtr.Zero)
            {
                CloseHandle(_processHandle);
                _processHandle = IntPtr.Zero;
            }

            if (_threadHandle != IntPtr.Zero)
            {
                CloseHandle(_threadHandle);
                _threadHandle = IntPtr.Zero;
            }

            _inputReadSide?.Dispose();
            _inputWriteSide?.Dispose();
            _outputReadSide?.Dispose();
            _outputWriteSide?.Dispose();
        }

        #region Native Methods and Structures
        private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const uint INFINITE = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(
            out SafeFileHandle hReadPipe,
            out SafeFileHandle hWritePipe,
            ref SECURITY_ATTRIBUTES lpPipeAttributes,
            int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CreatePseudoConsole(
            COORD size,
            IntPtr hInput,
            IntPtr hOutput,
            uint dwFlags,
            out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(
            IntPtr lpAttributeList,
            int dwAttributeCount,
            int dwFlags,
            ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr Attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            [In] ref STARTUPINFOEX lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            IntPtr hFile,
            [Out] byte[] lpBuffer,
            int nNumberOfBytesToRead,
            out int lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
        #endregion
    }
}