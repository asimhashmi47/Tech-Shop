using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TechShop.Infrastructure.Persistence;

/// <summary>
/// The key the database file is encrypted with, and where it lives.
///
/// What this defends against is the realistic threat for a shop: the database is a file
/// on a PC behind the counter, and anyone who can reach that PC can copy it onto a USB
/// stick and read every customer, price and takings figure at home. Encrypting the file
/// makes that copy worthless on its own.
///
/// What it does not defend against is code running as the till's own Windows user: that
/// code can ask DPAPI to unprotect the key exactly as this class does. Keeping a key on
/// the same machine as the data it opens cannot be made to do more than that without a
/// passphrase someone types at every start, which a shop floor will not tolerate.
/// </summary>
public static class DatabaseKey
{
    /// <summary>Bound to this machine, so the key file is useless if copied with the database.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TechShop.DatabaseKey.v1");

    public static string KeyFilePath(string databasePath) => databasePath + ".key";

    /// <summary>
    /// Returns the key for this database, creating one on first run. The key is random
    /// per installation: there is no shared secret in the source for someone to find by
    /// downloading the application.
    /// </summary>
    public static string ForDatabase(string databasePath)
    {
        var keyFile = KeyFilePath(databasePath);

        if (File.Exists(keyFile))
        {
            return Unprotect(File.ReadAllBytes(keyFile));
        }

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var directory = Path.GetDirectoryName(keyFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(keyFile, Protect(key));

        return key;
    }

    /// <summary>
    /// Builds the connection string. The key goes through <c>Password</c>, which the
    /// SQLCipher build of the provider turns into the <c>PRAGMA key</c> it needs before
    /// the file can be read at all.
    /// </summary>
    public static string ConnectionString(string databasePath, string key)
        => new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Password = key,
        }.ToString();

    /// <summary>
    /// Encrypts a database that was created before this existed, rather than leaving the
    /// shop with an unreadable file or a silently plaintext one.
    ///
    /// SQLCipher cannot encrypt a file in place, so the contents are exported into a new
    /// encrypted database and that file takes the original's name. The original is kept
    /// alongside with a <c>.plaintext</c> suffix: deleting the only copy of a shop's
    /// books on the strength of an export that has not been verified is not a trade this
    /// code is entitled to make.
    /// </summary>
    public static void EncryptIfPlaintext(string databasePath, string key)
    {
        if (!File.Exists(databasePath) || !LooksPlaintext(databasePath))
        {
            return;
        }

        var encrypted = databasePath + ".encrypting";
        File.Delete(encrypted);

        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sqlcipher_export('encrypted')";
            command.CommandText = $"ATTACH DATABASE @path AS encrypted KEY @key; {command.CommandText}";
            command.Parameters.AddWithValue("@path", encrypted);
            command.Parameters.AddWithValue("@key", key);
            command.ExecuteNonQuery();

            using var detach = connection.CreateCommand();
            detach.CommandText = "DETACH DATABASE encrypted";
            detach.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        File.Move(databasePath, databasePath + ".plaintext", overwrite: true);
        File.Move(encrypted, databasePath);
    }

    /// <summary>True when the file still begins with SQLite's own unencrypted header.</summary>
    public static bool LooksPlaintext(string databasePath)
    {
        var header = new byte[16];

        using var file = File.OpenRead(databasePath);

        return file.Read(header) == header.Length
            && Encoding.ASCII.GetString(header) == "SQLite format 3\0";
    }

    private static byte[] Protect(string key)
        => OperatingSystem.IsWindows()
            ? ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.LocalMachine)

            // Only reached by tooling on another OS. Obfuscation, not protection, and
            // labelled as such so nobody mistakes it for the Windows path.
            : Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(key)));

    private static string Unprotect(byte[] stored)
        => OperatingSystem.IsWindows()
            ? Encoding.UTF8.GetString(ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.LocalMachine))
            : Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(stored)));
}
