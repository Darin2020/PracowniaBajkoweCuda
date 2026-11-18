namespace BajkoweCuda.Application.Audit;

/// <summary>Action names of the audit log. Stable: they are stored and shown to admins.</summary>
public static class AuditActions
{
    public const string Registered = "auth.registered";
    public const string EmailConfirmed = "auth.email_confirmed";
    public const string LoginSucceeded = "auth.login_succeeded";
    public const string LoginFailed = "auth.login_failed";
    public const string TwoFactorLoginFailed = "auth.2fa_login_failed";
    public const string Logout = "auth.logout";
    public const string PasswordReset = "auth.password_reset";
    public const string PasswordChanged = "auth.password_changed";
    public const string ReauthenticationFailed = "auth.reauthentication_failed";
    public const string TwoFactorEnabled = "auth.2fa_enabled";
    public const string RecoveryCodesGenerated = "auth.2fa_recovery_codes_generated";
    public const string AccountExported = "account.exported";
    public const string AccountDeleted = "account.deleted";
    public const string AdminRoleGranted = "admin.role_granted";
    public const string AdminRoleRevoked = "admin.role_revoked";
}
