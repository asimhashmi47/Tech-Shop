using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Interfaces;
using TechShop.Infrastructure;
using TechShop.Infrastructure.Persistence;
using TechShop.WinForms.Shell;
using TechShop.WinForms.Theming;

namespace TechShop.WinForms;

internal static class Program
{
    /// <summary>
    /// The database lives under the user's local app data rather than beside the
    /// executable, so the app works when installed into Program Files and so a Windows
    /// profile keeps its own books.
    /// </summary>
    private static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TechShop",
        "techshop.db");

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        WinFormsApp.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        CrashLog.Install();
        Theme.Apply();

        try
        {
            var services = new ServiceCollection();
            services.AddTechShop(DatabasePath);
            using var provider = services.BuildServiceProvider();
            AppHost.Use(provider);

            if (!TryInitialiseDatabase(provider))
            {
                return;
            }

            // The shell owns the sign-in loop: signing out returns here rather than
            // ending the process, so the next operator does not wait for a cold start.
            while (true)
            {
                using var login = new LoginForm();
                if (login.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                using var shell = new MainForm();
                WinFormsApp.Run(shell);

                if (!shell.SignOutRequested)
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Startup", ex);
            MessageBox.Show(
                $"TechShop could not start.\n\n{ex.Message}\n\nDetails: {CrashLog.Path}",
                "TechShop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool TryInitialiseDatabase(IServiceProvider provider)
    {
        try
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            new DatabaseSeeder(db, hasher).SeedAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"TechShop could not open its database.\n\n{DatabasePath}\n\n{ex.Message}",
                "TechShop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }
}
