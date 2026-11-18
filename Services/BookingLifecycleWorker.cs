using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickleballClub.Models;

namespace PickleballClub.Services;

/// <summary>
/// P1-07 & P2-04: Background worker that idempotently reconciles expired pending bookings and no-show approved bookings
/// without mutating database state inside GET requests.
/// </summary>
public class BookingLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingLifecycleWorker> _logger;

    public BookingLifecycleWorker(IServiceScopeFactory scopeFactory, ILogger<BookingLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial short delay after host boot
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
                var bookingService = scope.ServiceProvider.GetRequiredService<BookingService>();

                int reconciled = await bookingService.ReconcileBookingLifecycleAsync(db);
                if (reconciled > 0)
                {
                    _logger.LogInformation("BookingLifecycleWorker reconciled {Count} expired/no_show bookings.", reconciled);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "BookingLifecycleWorker cycle encountered a transient error.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
