namespace SBB.EasyRide.TaxReport.Web.Services;

/// <summary>
/// Service to track server startup and force logout on first request
/// </summary>
public class StartupService
{
    private bool _hasProcessedStartup = false;
    private readonly object _lock = new object();

    public bool IsFirstRequestAfterStartup()
    {
        lock (_lock)
        {
            if (!_hasProcessedStartup)
            {
                _hasProcessedStartup = true;
                return true;
            }
            return false;
        }
    }
}
