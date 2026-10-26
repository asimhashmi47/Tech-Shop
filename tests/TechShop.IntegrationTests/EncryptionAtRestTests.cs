using System.Globalization;
using Microsoft.Data.Sqlite;
using TechShop.Infrastructure.Persistence;

namespace TechShop.IntegrationTests;

/// <summary>
/// S4: the database is a file on a shop PC, and the first thing anyone who wants the
/// customer list will do is copy it. These tests are about what that copy is worth.
/// </summary>
[Collection("shop")]
public class EncryptionAtRestTests(ShopFixture shop)
{
    /// <summary>Every plaintext SQLite file starts with this, header and all.</summary>
    private const string PlaintextMagic = "SQLite format 3\0";

    [Fact]
    public void The_database_file_is_not_a_readable_sqlite_file()
    {
        var header = new byte[16];

        // Shared, because the connection pool is still holding the database open.
        using (var file = new FileStream(
            shop.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.Equal(16, file.Read(header));
        }

        var text = System.Text.Encoding.ASCII.GetString(header);

        Assert.NotEqual(PlaintextMagic, text);
    }

    [Fact]
    public void Opening_the_file_without_the_key_fails()
    {
        using var connection = new SqliteConnection($"Data Source={shop.DatabasePath}");

        // SQLCipher cannot even read the page size without the key, so the failure
        // arrives as soon as anything is asked of the file.
        var thrown = Record.Exception(() =>
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM Customers";
            command.ExecuteScalar();
        });

        Assert.NotNull(thrown);
    }

    [Fact]
    public void Opening_the_file_with_the_key_works()
    {
        var key = DatabaseKey.ForDatabase(shop.DatabasePath);

        using var connection = new SqliteConnection(DatabaseKey.ConnectionString(shop.DatabasePath, key));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM Customers";

        Assert.True(Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public void The_key_is_not_stored_in_plain_sight_next_to_the_database()
    {
        var keyFile = DatabaseKey.KeyFilePath(shop.DatabasePath);

        Assert.True(File.Exists(keyFile), "the key file was never written");

        var onDisk = File.ReadAllBytes(keyFile);
        var key = DatabaseKey.ForDatabase(shop.DatabasePath);

        // The stored bytes must not simply be the key. On Windows this is DPAPI, which
        // ties the blob to this machine, so a copied pair of files is still useless.
        Assert.DoesNotContain(key, System.Text.Encoding.UTF8.GetString(onDisk));
    }
    [Fact]
    public void A_database_from_before_encryption_is_converted_and_the_original_kept()
    {
        // A shop upgrading to this build has a plaintext file full of their books. It
        // has to come across intact, and the original has to survive the attempt.
        var folder = Path.Combine(Path.GetTempPath(), "techshop-tests", $"legacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "legacy.db");

        try
        {
            using (var plain = new SqliteConnection($"Data Source={path}"))
            {
                plain.Open();
                using var create = plain.CreateCommand();
                create.CommandText =
                    "CREATE TABLE Takings (Day TEXT, Amount REAL);"
                    + "INSERT INTO Takings VALUES ('2026-01-02', 418.75)";
                create.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            Assert.True(DatabaseKey.LooksPlaintext(path), "the fixture did not start out plaintext");

            var key = DatabaseKey.ForDatabase(path);
            DatabaseKey.EncryptIfPlaintext(path, key);

            Assert.False(DatabaseKey.LooksPlaintext(path), "the file was left readable");
            Assert.True(File.Exists(path + ".plaintext"), "the original was discarded");

            using var reopened = new SqliteConnection(DatabaseKey.ConnectionString(path, key));
            reopened.Open();
            using var read = reopened.CreateCommand();
            read.CommandText = "SELECT Amount FROM Takings WHERE Day = '2026-01-02'";

            Assert.Equal(418.75m, Convert.ToDecimal(read.ExecuteScalar(), CultureInfo.InvariantCulture));
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A locked temp file is not worth failing a test run over.
            }
        }
    }
}
