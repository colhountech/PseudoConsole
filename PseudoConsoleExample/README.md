# To use this in a project:

```c#
using PseudoConsole.Core;

namespace PseudoConsoleExample
{
    class Program
    {
        static void Main(string[] args)
        {


            // Simple usage
            using var console = new PseudoConsoleProcess();
            console.ExecuteCommand("dir /s /b", Console.Write);

            // Example 1: Write to console
            console.ExecuteCommand("dir", output => Console.Write(output));

            // Example 2: Write to a file
            console.ExecuteCommand("dir", output => File.AppendAllText("log.txt", output));

            // Example 3: Process the output
            console.ExecuteCommand("dir", output => {
                if (output.Contains("error"))
                {
                    // Handle error
                }
            });

        }
    }


}

```