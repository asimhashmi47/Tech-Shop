using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechShop.Application.Common.Interfaces;
using TechShop.Infrastructure.Persistence;
using TechShop.Infrastructure.Services;

namespace TechShop.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires persistence, the cross-cutting services and every application service.
    ///
    /// Lifetimes follow the shell's unit of work: the host opens one scope per screen, so
    /// the DbContext and anything that tracks entities is scoped to the screen that owns
    /// it, while the signed-in session and the permission cache outlive every screen.
    /// </summary>
    public static IServiceCollection AddTechShop(this IServiceCollection services, string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // The file is encrypted, so the key has to be in hand before anything opens it,
        // and a database from a build that predates this has to be converted first.
        var key = DatabaseKey.ForDatabase(databasePath);
        DatabaseKey.EncryptIfPlaintext(databasePath, key);

        services.AddDbContext<AppDbContext>(options => options
            .UseSqlite(DatabaseKey.ConnectionString(databasePath, key))
            .EnableSensitiveDataLogging(false));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // One identity and one permission cache for the whole process.
        services.AddSingleton<UserSession>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<UserSession>());
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();

        services.AddSingleton<IExcelExportService, ExcelExportService>();

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IShopScope, ShopScope>();
        services.AddScoped<IAccessGuard, AccessGuard>();
        services.AddScoped<IAuditService, AuditService>();

        services.AddApplicationServices();

        return services;
    }

    /// <summary>
    /// Registers every *Service in the application assembly by convention. The alternative
    /// is twenty near-identical lines that go stale the moment a screen is added.
    /// </summary>
    private static void AddApplicationServices(this IServiceCollection services)
    {
        var applicationAssembly = typeof(CacheKeys).Assembly;

        var serviceTypes = applicationAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => t.Name.EndsWith("Service", StringComparison.Ordinal));

        foreach (var type in serviceTypes)
        {
            services.AddScoped(type);
        }
    }
}
