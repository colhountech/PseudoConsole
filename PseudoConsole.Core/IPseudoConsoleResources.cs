namespace PseudoConsole.Core
{
    internal interface IPseudoConsoleResources : IDisposable
    {
        void RunCommand(string command, Action<string> outputHandler);
    }
}