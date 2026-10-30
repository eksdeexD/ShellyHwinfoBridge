// Copyright (c) 2026 Zapador. Licensed under the MIT License.
namespace CybeneticsImport;

/// <summary>Entry point of the Cybenetics report import tool.</summary>
internal static class Program
{
    private static int Main(string[] args) => ImportCommand.Run(args, Console.Out, Console.Error);
}
