# PseudoConsole.Core

This refactoring offers several benefits:

## Clean Public API:

* Users only interact with PseudoConsoleProcess class
* Simple event-based or callback-based output handling
* Hides all the complexity of Win32 APIs


## Better Separation of Concerns:

* NativeMethods: Contains all P/Invoke code
* IPseudoConsoleResources: Defines the internal interface
* PseudoConsoleResources: Internal implementation
* PseudoConsoleProcess: Public facade


## Flexible Output Handling:

* Event-based approach for continuous monitoring
* Direct callback for simple usage
* Both can be used simultaneously


## More Library-Friendly:

* Internal implementation details are hidden
* Clear public API surface
* Easy to version and maintain


## To use this in a project:

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