using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Interfaces;
using TechShop.Application.Features.Auth;
using TechShop.Domain.Enums;
using TechShop.Infrastructure;
using TechShop.Infrastructure.Persistence;
using TechShop.Infrastructure.Services;
using TechShop.WinForms;
using TechShop.WinForms.Theming;

namespace TechShop.QaHarness;

/// <summary>
/// Drives the real application headlessly: builds the same container the app builds,
/// signs in, then constructs, paints and operates every screen and dialog.
///
/// This exists because a WinForms app will happily compile with a screen that throws the
/// moment it is painted. Running the app by hand finds those one at a time; this finds
/// them all in one pass and can be re-run after every change.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // --shots <dir> writes a PNG of every screen, so the UI can be reviewed without
        // someone sitting in front of the running app.
        var shots = Array.IndexOf(args, "--shots");
        if (shots >= 0 && shots + 1 < args.Length)
        {
            ScreenTests.ShotDirectory = args[shots + 1];
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        Theme.Apply();

        var databasePath = Path.Combine(Path.GetTempPath(), "techshop-qa", $"qa-{Guid.NewGuid():N}.db");
        var harness = new Harness();

        try
        {
            var services = new ServiceCollection();
            services.AddTechShop(databasePath);
            using var provider = services.BuildServiceProvider();
            AppHost.Use(provider);

            Console.WriteLine($"TechShop QA harness");
            Console.WriteLine($"database: {databasePath}");

            Seed(provider, harness);
            SignIn(provider, harness);

            DataIntegrityTests.RunAsync(harness, provider).GetAwaiter().GetResult();
            ScreenTests.Run(harness);
            InsertTests.RunAsync(harness).GetAwaiter().GetResult();
            TillTests.RunAsync(harness).GetAwaiter().GetResult();
            PrintTests.RunAsync(harness, ScreenTests.ShotDirectory).GetAwaiter().GetResult();
            PermissionTests.RunAsync(harness, provider).GetAwaiter().GetResult();
            LoadTests.RunAsync(harness).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            harness.Fail("harness", ex.ToString());
        }

        var passed = harness.Report();

        Cleanup(databasePath);
        return passed ? 0 : 1;
    }

    private static void Seed(IServiceProvider provider, Harness harness)
    {
        harness.Section("bootstrap");

        harness.Check("database migrates and seeds", () =>
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            new DatabaseSeeder(db, hasher).SeedAsync().GetAwaiter().GetResult();
        });
    }

    private static void SignIn(IServiceProvider provider, Harness harness)
    {
        harness.Check("admin can sign in", () =>
        {
            using var scope = provider.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

            var profile = auth
                .LoginAsync(new LoginRequest(DatabaseSeeder.AdminUsername, DatabaseSeeder.AdminPassword))
                .GetAwaiter().GetResult();

            var session = provider.GetRequiredService<UserSession>();
            session.SignIn(
                profile.Id,
                profile.Username,
                profile.FullName,
                profile.Role,
                profile.Shops.Select(s => s.Id).ToList());

            if (profile.Role != UserRole.Admin)
            {
                throw new InvalidOperationException("the seeded admin is not an administrator");
            }
        });

        harness.Check("a wrong password is refused", () =>
        {
            using var scope = provider.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

            try
            {
                auth.LoginAsync(new LoginRequest(DatabaseSeeder.AdminUsername, "not-the-password"))
                    .GetAwaiter().GetResult();
            }
            catch (Application.Common.Exceptions.ForbiddenException)
            {
                return;
            }

            throw new InvalidOperationException("a wrong password was accepted");
        });

        harness.Check("a standard account signs in with username as password", () =>
        {
            using var scope = provider.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            auth.LoginAsync(new LoginRequest("sara", "sara")).GetAwaiter().GetResult();
        });

        // Signing in as sara above replaced the audit identity; put admin back so the
        // screen tests run with full access.
        using var restore = provider.CreateScope();
        var restoreAuth = restore.ServiceProvider.GetRequiredService<AuthService>();
        var adminProfile = restoreAuth
            .LoginAsync(new LoginRequest(DatabaseSeeder.AdminUsername, DatabaseSeeder.AdminPassword))
            .GetAwaiter().GetResult();

        provider.GetRequiredService<UserSession>().SignIn(
            adminProfile.Id,
            adminProfile.Username,
            adminProfile.FullName,
            adminProfile.Role,
            adminProfile.Shops.Select(s => s.Id).ToList());
    }

    private static void Cleanup(string databasePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked temp database is not worth failing the run over.
        }
    }
}
