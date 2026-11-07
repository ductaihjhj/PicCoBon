using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class BookingsController : Controller
    {
        private readonly PickleballContext _context;

        public BookingsController(PickleballContext context)
        {
            _context = context;
        }

        private async Task ReconcileBookingsByRealTimeAsync()
        {
            var now = DateTime.Now;
            string todayIso = now.ToString("yyyy-MM-dd");
            int nowMins = now.Hour * 60 + now.Minute;

            var activeBookings = await _context.TbBookings
                .Where(b => b.Status == "approved" || b.Status == "playing")
                .ToListAsync();

            bool changed = false;
            foreach (var b in activeBookings)
            {
                string bDate = string.IsNullOrWhiteSpace(b.BookingDate) ? todayIso : b.BookingDate.Trim();
                int startMins = 17 * 60;
                if (!string.IsNullOrWhiteSpace(b.StartTime) && b.StartTime.Contains(':'))
                {
                    var parts = b.StartTime.Split(':');
                    if (parts.Length >= 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
                    {
                        startMins = h * 60 + m;
                    }
                }
                int dur = b.Duration > 0 ? b.Duration : 60;
                int endMins = startMins + dur;

                int dateCmp = string.CompareOrdinal(bDate, todayIso);
                if (dateCmp < 0)
                {
                    if (b.Status == "playing")
                    {
                        b.Status = "completed";
                        if (b.TotalPaid <= 0) b.TotalPaid = b.CourtFee;
                        changed = true;
                    }
                }
                else if (dateCmp > 0)
                {
                    if (b.Status == "playing")
                    {
                        b.Status = "approved";
                        changed = true;
                    }
                }
                else
                {
                    if (nowMins >= endMins)
                    {
                        if (b.Status == "playing")
                        {
                            b.Status = "completed";
                            if (b.TotalPaid <= 0) b.TotalPaid = b.CourtFee;
                            changed = true;
                        }
                    }
                    else if (nowMins >= startMins && nowMins < endMins)
                    {
                        if (b.Status == "approved")
                        {
                            b.Status = "playing";
                            changed = true;
                        }
                    }
                    else if (nowMins < startMins - 15)
                    {
                        if (b.Status == "playing")
                        {
                            b.Status = "approved";
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IActionResult> Index()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });

            await ReconcileBookingsByRealTimeAsync();

            ViewBag.Courts = await _context.TbCourts
                .Where(c => c.IsActive)
                .OrderBy(c => c.CourtId)
                .ToListAsync();

            var bookings = await _context.TbBookings
                .Include(b => b.Court)
                .OrderByDescending(b => b.BookingDate)
                .ThenBy(b => b.StartTime)
                .ToListAsync();

            return View(bookings);
        }

        [HttpPost]
        public async Task<IActionResult> CreateWalkIn(int courtId, string customerName, string phone, string bookingDate, string startTime, int duration = 60)
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });

            var court = await _context.TbCourts.FindAsync(courtId);
            int hourlyRate = (court?.BasePrice ?? 120000) + (court?.Surcharge ?? 0);
            int courtFee = (int)Math.Round((duration / 60.0) * hourlyRate);
            string code = "PB-" + Guid.NewGuid().ToString("N")[..6].ToUpper();

            _context.TbBookings.Add(new TbBooking
            {
                BookingCode = code,
                CourtId = courtId,
                CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Khách tại quầy" : customerName.Trim(),
                Phone = string.IsNullOrWhiteSpace(phone) ? "0900000000" : phone.Trim(),
                BookingDate = string.IsNullOrWhiteSpace(bookingDate) ? DateTime.Now.ToString("yyyy-MM-dd") : bookingDate,
                StartTime = string.IsNullOrWhiteSpace(startTime) ? "17:00" : startTime,
                Duration = duration > 0 ? duration : 60,
                Players = 4,
                SkillLevel = "Giao lưu",
                CourtFee = courtFee,
                TotalPaid = courtFee,
                PosItemsJson = "[]",
                Status = "approved",
                CreatedDate = DateTime.Now
            });

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, string status, string? returnUrl = null)
        {
            var booking = await _context.TbBookings.FindAsync(id);
            if (booking != null)
            {
                if (status == "playing")
                {
                    var now = DateTime.Now;
                    string todayIso = now.ToString("yyyy-MM-dd");
                    int nowMins = now.Hour * 60 + now.Minute;
                    string bDate = string.IsNullOrWhiteSpace(booking.BookingDate) ? todayIso : booking.BookingDate.Trim();
                    int startMins = 17 * 60;
                    if (!string.IsNullOrWhiteSpace(booking.StartTime) && booking.StartTime.Contains(':'))
                    {
                        var parts = booking.StartTime.Split(':');
                        if (parts.Length >= 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
                        {
                            startMins = h * 60 + m;
                        }
                    }

                    // Nếu chưa tới ngày hoặc còn hơn 15 phút nữa mới tới giờ bắt đầu -> giữ ở trạng thái approved (Chờ tới giờ)
                    if (string.CompareOrdinal(bDate, todayIso) > 0 || (bDate == todayIso && nowMins < startMins - 15))
                    {
                        status = "approved";
                    }
                }

                booking.Status = status;
                if (status == "completed" && booking.TotalPaid <= 0)
                {
                    booking.TotalPaid = booking.CourtFee;
                }
                await _context.SaveChangesAsync();
            }
            if (!string.IsNullOrEmpty(returnUrl))
                return LocalRedirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, string? returnUrl = null)
        {
            var booking = await _context.TbBookings.FindAsync(id);
            if (booking != null)
            {
                _context.TbBookings.Remove(booking);
                await _context.SaveChangesAsync();
            }
            if (!string.IsNullOrEmpty(returnUrl))
                return LocalRedirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }
    }
}
