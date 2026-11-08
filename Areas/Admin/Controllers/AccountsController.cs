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
[Authorize(Roles = "Admin")]
public class AccountsController : Controller
{
    private readonly PickleballContext _context;
    private readonly PasswordService _passwordService;

    public AccountsController(PickleballContext context, PasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    public async Task<IActionResult> Index()
    {
        var accounts = await _context.TbAccounts
            .Include(a => a.Role)
            .Include(a => a.TbBookings)
            .OrderBy(a => a.RoleId)
            .ThenByDescending(a => a.AccountId)
            .ToListAsync();

        var allBookings = await _context.TbBookings
            .Where(b => b.Status != "rejected" && b.Status != "cancelled")
            .ToListAsync();

        ViewBag.AllBookings = allBookings;
        return View(accounts);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMember(
        string fullName,
        string phone,
        string? email,
        string? password,
        string? skillLevel,
        string? memberTier,
        int roleId = 2)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2)
        {
            TempData["AdminAccountMsg"] = "Vui lòng nhập Họ tên hợp lệ (tối thiểu 2 ký tự).";
            return RedirectToAction(nameof(Index));
        }

        if (!BookingService.IsValidPhone(phone))
        {
            TempData["AdminAccountMsg"] = "Số điện thoại không hợp lệ (10 chữ số Việt Nam).";
            return RedirectToAction(nameof(Index));
        }

        string cleanPhone = BookingService.NormalizePhone(phone);
        string cleanEmail = (email ?? "").Trim().ToLowerInvariant();

        bool exists = await _context.TbAccounts.AnyAsync(a =>
            (a.Phone != null && a.Phone == cleanPhone) ||
            (!string.IsNullOrEmpty(cleanEmail) && a.Email != null && a.Email.ToLower() == cleanEmail));

        if (exists)
        {
            TempData["AdminAccountMsg"] = "Số điện thoại hoặc Email này đã tồn tại trong hệ thống.";
            return RedirectToAction(nameof(Index));
        }

        string rawPass = string.IsNullOrWhiteSpace(password) ? "123456" : password.Trim();

        var acc = new TbAccount
        {
            Username = !string.IsNullOrEmpty(cleanEmail) ? cleanEmail.Split('@')[0] : cleanPhone,
            FullName = fullName.Trim(),
            Phone = cleanPhone,
            Email = string.IsNullOrEmpty(cleanEmail) ? null : cleanEmail,
            SkillLevel = string.IsNullOrWhiteSpace(skillLevel) ? "Giao lưu (DUPR 3.0)" : skillLevel.Trim(),
            MemberTier = string.IsNullOrWhiteSpace(memberTier) ? "Hội viên Tiêu chuẩn" : memberTier.Trim(),
            DominantHand = "Tay phải",
            RoleId = roleId is 1 or 2 or 3 ? roleId : 2,
            IsActive = true,
            LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd")
        };

        acc.Password = _passwordService.HashPassword(acc, rawPass);

        _context.TbAccounts.Add(acc);
        await _context.SaveChangesAsync();

        TempData["AdminAccountSuccess"] = $"Đã tạo tài khoản hội viên {acc.FullName} (#SPK-{acc.AccountId:D4}) thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMemberTier(int id, string memberTier, string? skillLevel, int roleId = 2)
    {
        var acc = await _context.TbAccounts.FindAsync(id);
        if (acc != null)
        {
            acc.MemberTier = memberTier;
            if (!string.IsNullOrWhiteSpace(skillLevel))
            {
                acc.SkillLevel = skillLevel;
            }
            if (acc.AccountId != Function._AccountId)
            {
                acc.RoleId = roleId is 1 or 2 or 3 ? roleId : 2;
            }
            await _context.SaveChangesAsync();
            TempData["AdminAccountSuccess"] = $"Đã cập nhật hạng thẻ & phân quyền cho {acc.FullName}.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var acc = await _context.TbAccounts.FindAsync(id);
        if (acc != null && acc.AccountId != Function._AccountId)
        {
            acc.IsActive = !acc.IsActive;
            await _context.SaveChangesAsync();
            TempData["AdminAccountSuccess"] = $"Đã {(acc.IsActive ? "mở khóa" : "tạm khóa")} tài khoản {acc.FullName}.";
        }
        return RedirectToAction(nameof(Index));
    }
}
