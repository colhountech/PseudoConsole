using System;
using System.Drawing;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Permissions;


using static PseudoConsole.Core.NativeMethods;

namespace PseudoConsole.Core
{
    internal sealed class PseudoConsoleResources : IPseudoConsoleResources
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

            private IntPtr _processHandle;
            private IntPtr _threadHandle;


        public PseudoConsoleResources(int width = 120, int height = 30)
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
            var size = new COORD { X = (short)width, Y = (short)height };
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

            _inputReadSide = inputReadSide;
            _inputWriteSide = inputWriteSide;
            _outputReadSide = outputReadSide;
            _outputWriteSide = outputWriteSide;
            _pseudoConsoleHandle = hPC;
            _startupInfo = startupInfo;
        }

        public void RunCommand(string command, Action<string> outputHandler)
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
                            int read;
                            try
                            {
                                var success = ReadFile(
                                    _outputReadSide.DangerousGetHandle(),
                                    buffer,
                                    buffer.Length,
                                    out read,
                                    IntPtr.Zero);

                                if (!success || read == 0)
                                {
                                    break;
                                }


                                //Console.Write(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
                                //
                                outputHandler?.Invoke(System.Text.Encoding.UTF8.GetString(buffer, 0, read));

                            }
                            catch (Exception)
                            {
                                break;  // Exit the thread on any error
                            }
                        }
                    });


                    outputThread.Start();

                    try
                    {
                        WaitForSingleObject(processInfo.hProcess, INFINITE);

                        // Get exit code to ensure process is really done
                        uint exitCode;
                        if (GetExitCodeProcess(processInfo.hProcess, out exitCode))
                        {
                            // Close write side first
                            _outputWriteSide.Dispose();

                            // Give the read thread a chance to get all remaining data
                            if (!outputThread.Join(1000))
                            {
                                outputThread.Interrupt();
                                outputThread.Join(100); // Short wait for interrupt
                            }
                        }
                    }
                    catch
                    {
                        outputThread.Interrupt();
                        throw;
                    }
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
        }
}