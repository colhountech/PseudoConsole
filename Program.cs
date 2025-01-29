using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

/*
 * CoPilot This version will compile but won't work as intended.
 */

namespace PseudoConsoleExample
{
    class Program
    {
        static void Main(string[] args)
        {
            //var command = "ffmpeg -i input.mp4 output.mp4";
            var command = "dir /s /b";

            using (var pseudoConsole = PseudoConsole.Create())
            {
                pseudoConsole.Run(command);
            }
        }
    }

    public class PseudoConsole : IDisposable
    {
        private readonly SafeFileHandle _inputReadSide;
        private readonly SafeFileHandle _inputWriteSide;
        private readonly SafeFileHandle _outputReadSide;
        private readonly SafeFileHandle _outputWriteSide;
        private readonly IntPtr _pseudoConsoleHandle;

        private PseudoConsole(SafeFileHandle inputReadSide, SafeFileHandle inputWriteSide, SafeFileHandle outputReadSide, SafeFileHandle outputWriteSide, IntPtr pseudoConsoleHandle)
        {
            _inputReadSide = inputReadSide;
            _inputWriteSide = inputWriteSide;
            _outputReadSide = outputReadSide;
            _outputWriteSide = outputWriteSide;
            _pseudoConsoleHandle = pseudoConsoleHandle;
        }

        public static PseudoConsole Create()
        {
            CreatePipe(out var inputReadSide, out var inputWriteSide);
            CreatePipe(out var outputReadSide, out var outputWriteSide);

            var pseudoConsoleHandle = CreatePseudoConsole(inputReadSide, outputWriteSide);

            return new PseudoConsole(inputReadSide, inputWriteSide, outputReadSide, outputWriteSide, pseudoConsoleHandle);
        }

        public void Run(string command)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();

                using (var writer = new StreamWriter(new FileStream(_inputWriteSide, FileAccess.Write)))
                using (var reader = new StreamReader(new FileStream(_outputReadSide, FileAccess.Read)))
                {
                    writer.AutoFlush = true;

                    var outputThread = new Thread(() =>
                    {
                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            Console.WriteLine(line);
                        }
                    });

                    outputThread.Start();

                    process.WaitForExit();
                    outputThread.Join();
                }
            }
        }

        public void Dispose()
        {
            CloseHandle(_pseudoConsoleHandle);
            _inputReadSide.Dispose();
            _inputWriteSide.Dispose();
            _outputReadSide.Dispose();
            _outputWriteSide.Dispose();
        }

        private static void CreatePipe(out SafeFileHandle readSide, out SafeFileHandle writeSide)
        {
            if (!CreatePipe(out readSide, out writeSide, IntPtr.Zero, 0))
            {
                throw new InvalidOperationException("Failed to create pipe.");
            }
        }

        private static IntPtr CreatePseudoConsole(SafeFileHandle inputReadSide, SafeFileHandle outputWriteSide)
        {
            var size = new COORD { X = 80, Y = 25 };
            if (CreatePseudoConsole(size, inputReadSide, outputWriteSide, 0, out var pseudoConsoleHandle) != 0)
            {
                throw new InvalidOperationException("Failed to create pseudoconsole.");
            }

            return pseudoConsoleHandle;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }
    }
}