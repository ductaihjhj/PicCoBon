using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Staff")]
public class BookingsController : Controller
{
    private readonly PickleballContext _context;
    private readonly BookingService _bookingService;

    public BookingsController(PickleballContext context, BookingService bookingService)
    {
        _context = context;
        _bookingService = bookingService;
    }

    public async Task<IActionResult> Index()
    {
        await _bookingService.ReconcileBookingStatusesAsync();

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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWalkIn(
        int courtId,
        string customerName,
        string phone,
        string bookingDate,
        string startTime,
        int duration = 60)
    {
        var req = new CreateBookingRequest
        {
            CourtId = courtId,
            CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Khách tại quầy" : customerName.Trim(),
            CustomerPhone = string.IsNullOrWhiteSpace(phone) ? "0900000000" : phone.Trim(),
            Date = string.IsNullOrWhiteSpace(bookingDate) ? BookingService.GetVietnamNow().ToString("yyyy-MM-dd") : bookingDate,
            Time = string.IsNullOrWhiteSpace(startTime) ? "17:00" : startTime,
            Duration = duration > 0 ? duration : 60,
            Players = 4,
            SkillLevel = "Giao lưu",
            PaymentMethod = "cash",
            AutoApprove = true
        };

        var result = await _bookingService.CreateBookingAsync(
            req,
            Function._AccountId > 0 ? Function._AccountId : null,
            Function._RoleId,
            isStaffOrAdmin: true);

        if (!result.Success)
        {
            TempData["AdminBookingError"] = result.ErrorMessage;
        }
        else
        {
            TempData["AdminBookingSuccess"] = $"Đã tạo lịch đặt sân #{result.Data!.BookingCode} thành công.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status, string? returnUrl = null)
    {
        var booking = await _context.TbBookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.BookingId == id);

        if (booking != null)
        {
            string actor = Function._Username;
            if (status == "approved")
            {
                var res = await _bookingService.ApproveBookingAsync(booking.BookingCode, actor);
                if (!res.Success) TempData["AdminBookingError"] = res.ErrorMessage;
            }
            else if (status == "playing")
            {
                var res = await _bookingService.CheckInBookingAsync(booking.BookingCode, actor, allowManualCode: true);
                if (!res.Success) TempData["AdminBookingError"] = res.ErrorMessage;
            }
            else if (status == "completed")
            {
                var res = await _bookingService.RecordBookingPaymentAsync(
                    booking.BookingCode,
                    amount: 0,
                    paymentMethod: booking.PaymentMethod ?? "cash",
                    markCompleted: true,
                    note: "Hoàn tất từ trang Quản trị Đơn",
                    actorUsername: actor);
                if (!res.Success) TempData["AdminBookingError"] = res.ErrorMessage;
            }
            else if (status == "rejected" || status == "cancelled")
            {
                var res = await _bookingService.CancelBookingAsync(
                    booking.BookingCode,
                    Function._AccountId,
                    null,
                    isStaffOrAdmin: true,
                    cancelReason: status == "rejected" ? "REJECT" : "Hủy bởi quản trị viên",
                    actorUsername: actor);
                if (!res.Success) TempData["AdminBookingError"] = res.ErrorMessage;
            }
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? returnUrl = null)
    {
        var booking = await _context.TbBookings
            .Include(b => b.TbOrders)
            .Include(b => b.TbPaymentTransactions)
            .FirstOrDefaultAsync(b => b.BookingId == id);

        if (booking != null)
        {
            // P1-17: Do not hard-delete bookings that have payment transactions or linked orders; cancel instead
            if (booking.PaidAmount > 0 || booking.TbPaymentTransactions.Any() || booking.TbOrders.Any())
            {
                booking.Status = "cancelled";
                booking.CancelReason = "Đã lưu trữ / hủy (có phát sinh giao dịch tài chính)";
                await _context.SaveChangesAsync();
                TempData["AdminBookingError"] = $"Đơn {booking.BookingCode} đã có giao dịch thanh toán nên được chuyển sang trạng thái Đã hủy để bảo toàn sổ quỹ.";
            }
            else
            {
                _context.TbBookings.Remove(booking);
                await _context.SaveChangesAsync();
            }
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }
}
