namespace CourtBookingSystem.Services
{
    public class PayMongoQrPhExpiryBackgroundService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PayMongoQrPhExpiryBackgroundService> _logger;

        public PayMongoQrPhExpiryBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<PayMongoQrPhExpiryBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var expiry = scope.ServiceProvider.GetRequiredService<PayMongoQrPhExpiryService>();
                    var cancelled = await expiry.CancelExpiredPaymentsAsync(stoppingToken);
                    if (cancelled > 0)
                    {
                        _logger.LogInformation("Cancelled {Count} expired PayMongo QR Ph booking(s).", cancelled);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PayMongo QR Ph expiry check failed.");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }
    }
}
