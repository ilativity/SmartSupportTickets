using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace SupportTickets.Api.Auth;

/// <summary>Password hashing in ASP.NET Core Identity's format (PBKDF2 with a per-password salt).</summary>
public static class PasswordHashing
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object User = new(); // the hasher's API wants a user object; the format does not use it

    public static string Hash(string password) => Hasher.HashPassword(User, password);

    /// <summary>
    /// True for a complete hash in the current Identity format (V3): base64 of
    /// [0x01][PRF:4][iterations:4][salt length:4][salt][subkey], all integers big-endian. Checking the lengths catches
    /// a truncated copy-paste, which would otherwise still be valid base64 starting with 0x01.
    /// </summary>
    public static bool LooksValid(string hash)
    {
        var bytes = new byte[hash.Length];
        if (!Convert.TryFromBase64String(hash.Trim(), bytes, out var length) || length < 13 || bytes[0] != 0x01)
        {
            return false;
        }

        var saltLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9, 4));
        const int minimumSubkeyLength = 16;
        return saltLength >= 16 && length >= 13 + saltLength + minimumSubkeyLength;
    }

    public static bool Verify(string hash, string password)
    {
        try
        {
            return Hasher.VerifyHashedPassword(User, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false; // a malformed configured hash means nobody can log in, not a 500
        }
    }
}

/// <summary>Checks the single admin account's username and password.</summary>
public sealed class AdminCredentialsVerifier(AdminOptions admin)
{
    // A real hash of a random password: verifying against it costs the same time as verifying a real one.
    private static readonly string DummyHash = PasswordHashing.Hash(Guid.NewGuid().ToString());

    public bool Verify(string username, string password)
    {
        var userMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(username.ToLowerInvariant()), Encoding.UTF8.GetBytes(admin.Username.ToLowerInvariant()));

        // The password hash is checked even when the username is wrong (against a dummy hash). Otherwise a wrong
        // username would answer in microseconds and a right one in tens of milliseconds, and the response time
        // alone would tell an attacker which usernames exist.
        var passwordMatches = PasswordHashing.Verify(userMatches ? admin.PasswordHash! : DummyHash, password);

        return userMatches && passwordMatches;
    }
}
