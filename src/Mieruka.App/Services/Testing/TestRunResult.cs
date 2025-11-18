using Mieruka.App.Services;
using Mieruka.Core.Monitors;

namespace Mieruka.App.Services.Testing;

internal sealed record class TestRunResult(
    bool Success,
    bool WindowFound,
    MonitorInfo Monitor,
    WindowPlacementHelper.ZoneRect Zone,
    string? ErrorMessage);
