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
        ViewBag.Courts = await _context.TbCourts
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CourtId)
            .ToListAsync();

        var bookings = await _context.TbBookings
            .AsNoTracking()
            .Include(b => b.Court)
            .OrderByDescending(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .Take(400)
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
        var input = new BookingCreateInput
        {
            CourtId = courtId,
            CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Khách tại quầy" : customerName.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? string.Empty : phone.Trim(),
            BookingDate = string.IsNullOrWhiteSpace(bookingDate) ? BookingService.GetVietnamNow().ToString("yyyy-MM-dd") : bookingDate,
            StartTime = string.IsNullOrWhiteSpace(startTime) ? "17:00" : startTime,
            Duration = duration > 0 ? duration : 60,
            Players = 4,
            SkillLevel = "Giao lưu",
            PaymentMethod = "cash",
            AccountId = Function._AccountId > 0 ? Function._AccountId : null,
            ActorRoleId = Function._RoleId,
            AutoApproveWalkIn = true,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        };

        var result = await _bookingService.CreateBookingAsync(_context, input);

        if (!result.Success || result.Booking == null)
        {
            TempData["AdminBookingError"] = result.ErrorMessage;
        }
        else
        {
            TempData["AdminBookingSuccess"] =
                $"Đã tạo ca đặt sân #{result.Booking.BookingCode} thành công (Mã bảo mật vé khách lẻ: {result.GuestAccessToken}).";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status, string? returnUrl = null)
    {
        var booking = await _context.TbBookings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookingId == id);

        if (booking != null)
        {
            string actor = Function._Username;
            int actorId = Function._AccountId;
            string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            if (status == "approved")
            {
                var res = await _bookingService.ApproveBookingAsync(_context, booking.BookingCode, actorId, actor, ip);
                if (!res.Success) TempData["AdminBookingError"] = res.ErrorMessage;
                else TempData["AdminBookingSuccess"] = $"Đã duyệt vé #{booking.BookingCode}.";
            }
            else if (status == "playing")
            {
                var res = await _bookingService.CheckInBookingAsync(_context, booking.BookingCode, actorId, actor, ip);
                if (!res.Success) TempData["AdminBookingError"] = res.ErrorMessage;
                else TempData["AdminBookingSuccess"] = $"Đã Check-in nhận sân vé #{booking.BookingCode}.";
            }
            else if (status == "completed")
            {
                // P1-04 & P1-05: Finishing a court session ONLY closes the session; PaidAmount stays untouched!
                var res = await _bookingService.FinishBookingSessionAsync(_context, booking.BookingCode, actorId, actor, ip);
                if (!res.Success) TempData["AdminBookingError"] = res.ErrorMessage;
                else TempData["AdminBookingSuccess"] = $"Đã đóng ca #{booking.BookingCode}. (Lưu ý: Vui lòng bấm 'Thu tiền' nếu khách thanh toán tại quầy).";
            }
            else if (status == "rejected" || status == "cancelled")
            {
                var res = await _bookingService.CancelBookingAsync(
                    _context,
                    booking.BookingCode,
                    actorId,
                    Function._RoleId,
                    guestAccessToken: null,
                    reason: status == "rejected" ? "rejected" : "Hủy bởi quản trị viên",
                    ipAddress: ip);
                if (!res.Success) TempData["AdminBookingError"] = res.ErrorMessage;
                else TempData["AdminBookingSuccess"] = $"Đã hủy đơn #{booking.BookingCode}.";
            }
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(
        int id,
        int amount,
        string paymentMethod = "cash",
        string? referenceCode = null,
        string? note = null,
        string? idempotencyKey = null,
        string? returnUrl = null)
    {
        var booking = await _context.TbBookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == id);
        if (booking == null)
        {
            TempData["AdminBookingError"] = "Không tìm thấy đơn đặt sân.";
            return RedirectToAction(nameof(Index));
        }

        string idem = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"ADM-PAY-{id}-{Guid.NewGuid():N}"
            : idempotencyKey;

        var res = await _bookingService.RecordBookingPaymentAsync(
            _context,
            booking.BookingCode,
            amount,
            paymentMethod,
            referenceCode,
            note,
            idem,
            Function._AccountId,
            Function._Username,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!res.Success)
        {
            TempData["AdminBookingError"] = res.ErrorMessage;
        }
        else
        {
            TempData["AdminBookingSuccess"] =
                $"Đã ghi sổ quỹ thu {amount:N0} đ ({paymentMethod}) cho đơn #{booking.BookingCode}.";
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundPayment(
        int id,
        int refundAmount,
        string paymentMethod = "cash",
        string? referenceCode = null,
        string? reason = null,
        string? idempotencyKey = null,
        string? returnUrl = null)
    {
        var booking = await _context.TbBookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == id);
        if (booking == null)
        {
            TempData["AdminBookingError"] = "Không tìm thấy đơn đặt sân.";
            return RedirectToAction(nameof(Index));
        }

        string idem = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"ADM-RFD-{id}-{Guid.NewGuid():N}"
            : idempotencyKey;

        var res = await _bookingService.RefundBookingPaymentAsync(
            _context,
            booking.BookingCode,
            refundAmount,
            paymentMethod,
            referenceCode,
            reason,
            idem,
            Function._AccountId,
            Function._Username,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!res.Success)
        {
            TempData["AdminBookingError"] = res.ErrorMessage;
        }
        else
        {
            TempData["AdminBookingSuccess"] =
                $"Đã ghi nhận giao dịch hoàn tiền {refundAmount:N0} đ cho đơn #{booking.BookingCode}.";
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
            .Include(b => b.TbInventoryMovements)
            .FirstOrDefaultAsync(b => b.BookingId == id);

        if (booking != null)
        {
            if (booking.PaidAmount > 0 || booking.TbPaymentTransactions.Any() || booking.TbOrders.Any() || booking.TbInventoryMovements.Any())
            {
                if (booking.PaidAmount > 0)
                {
                    TempData["AdminBookingError"] = $"Đơn {booking.BookingCode} đang có số dư thực thu ({booking.PaidAmount:N0} đ), không được phép xóa hoặc hủy trước khi hoàn tiền.";
                }
                else
                {
                    booking.Status = "cancelled";
                    booking.CancelReason = "Đã lưu trữ / hủy (có lịch sử giao dịch sổ quỹ hoặc kho)";
                    await _context.SaveChangesAsync();
                    TempData["AdminBookingSuccess"] = $"Đơn {booking.BookingCode} có lịch sử chứng từ nên được chuyển sang trạng thái Đã hủy để bảo toàn đối soát.";
                }
            }
            else
            {
                _context.TbBookings.Remove(booking);
                await _context.SaveChangesAsync();
                TempData["AdminBookingSuccess"] = $"Đã xóa đơn nháp {booking.BookingCode}.";
            }
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }
}
