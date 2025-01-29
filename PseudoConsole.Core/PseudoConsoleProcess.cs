using System;
using Microsoft.Win32.SafeHandles;

namespace PseudoConsole.Core
{
    public sealed class PseudoConsoleProcess : IDisposable
    {
        public delegate void OutputReceivedHandler(string output);
        public event OutputReceivedHandler OutputReceived;

        private readonly IPseudoConsoleResources _resources;

        public PseudoConsoleProcess(int width = 120, int height = 30)
        {
            _resources = new PseudoConsoleResources(width, height);
        }

        public void ExecuteCommand(string command, Action<string> outputHandler = null)
        {
            if (string.IsNullOrEmpty(command))
                throw new ArgumentNullException(nameof(command));

            _resources.RunCommand(command, output => {
                outputHandler?.Invoke(output);
                OutputReceived?.Invoke(output);
            });
        }

        public void Dispose()
        {
            _resources.Dispose();
        }
    }
}