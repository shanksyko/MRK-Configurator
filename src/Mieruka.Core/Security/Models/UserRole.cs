namespace Mieruka.Core.Security.Models;

public enum UserRole
{
    Admin,      // Full access to all features
    Operator,   // Can manage dashboards and credentials
    Viewer      // Read-only access
}
