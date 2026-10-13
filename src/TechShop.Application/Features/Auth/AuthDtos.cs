using TechShop.Domain.Enums;

namespace TechShop.Application.Features.Auth;

public sealed record LoginRequest(string Username, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ShopRef(long Id, string Code, string Name);

public sealed record ModulePermission(Module Module, bool CanRead, bool CanWrite);

public sealed record UserProfile(
    long Id,
    string Username,
    string FullName,
    UserRole Role,
    IReadOnlyList<ShopRef> Shops,
    IReadOnlyList<ModulePermission> Permissions);
