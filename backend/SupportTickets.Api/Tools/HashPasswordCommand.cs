using SupportTickets.Api.Auth;

namespace SupportTickets.Api.Tools;

/// <summary>
/// <c>dotnet run -- hash-password</c> reads a password from standard input and prints a hash for <c>Auth:Admin:PasswordHash</c> and exits.
/// It runs only when those exact arguments are given — never as a side effect of loading the program (the test host
/// loads Program too, and must start the web app, not print a hash).
/// </summary>
public static class HashPasswordCommand
{
    public static bool TryRun(string[] args)
    {
        if (args is not ["hash-password", ..])
        {
            return false;
        }

        // Preferred: no argument, password read from standard input, so it stays out of shell history and the
        // process list (`echo` it in, or type it and press Enter). An argument is accepted for convenience.
        var password = args is [_, var fromArgs] ? fromArgs : Console.In.ReadLine();
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Usage: dotnet run -- hash-password   (then type the password and press Enter)");
            Environment.ExitCode = 2;
            return true;
        }

        Console.WriteLine(PasswordHashing.Hash(password));
        return true;
    }
}
