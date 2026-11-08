using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
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
    private const string TempPasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

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
            .AsNoTracking()
            .Include(a => a.Role)
            .OrderBy(a => a.RoleId)
            .ThenByDescending(a => a.AccountId)
            .ToListAsync();

        // P1-16: Compute verified net paid spend strictly from ledger transactions owned by AccountId
        var bookingTx = await (
            from pt in _context.TbPaymentTransactions.AsNoTracking()
            join b in _context.TbBookings.AsNoTracking() on pt.BookingId equals b.BookingId
            where b.AccountId != null && pt.Status == "completed"
            select new { AccountId = b.AccountId!.Value, pt.TransactionType, pt.Amount }
        ).ToListAsync();

        var orderTx = await (
            from pt in _context.TbPaymentTransactions.AsNoTracking()
            join o in _context.TbOrders.AsNoTracking() on pt.OrderId equals o.OrderId
            where o.BookingId == null && o.AccountId != null && pt.Status == "completed"
            select new { AccountId = o.AccountId!.Value, pt.TransactionType, pt.Amount }
        ).ToListAsync();

        var spendByAccount = new Dictionary<int, int>();
        foreach (var tx in bookingTx.Concat(orderTx))
        {
            int delta = string.Equals(tx.TransactionType, "refund", StringComparison.OrdinalIgnoreCase)
                ? -Math.Abs(tx.Amount)
                : Math.Max(0, tx.Amount);
            spendByAccount[tx.AccountId] = spendByAccount.GetValueOrDefault(tx.AccountId) + delta;
        }

        var bookingCountByAccount = await _context.TbBookings
            .AsNoTracking()
            .Where(b => b.AccountId != null && b.Status != "rejected" && b.Status != "cancelled" && b.Status != "expired")
            .GroupBy(b => b.AccountId!.Value)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count);

        ViewBag.VerifiedSpendByAccount = spendByAccount;
        ViewBag.BookingCountByAccount = bookingCountByAccount;

        return View(accounts);
    }

    private static string GenerateStrongTemporaryPassword()
    {
        Span<char> chars = stackalloc char[10];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = TempPasswordAlphabet[RandomNumberGenerator.GetInt32(TempPasswordAlphabet.Length)];
        }
        return $"Spk!{new string(chars)}";
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
        string cleanEmail = (email ?? string.Empty).Trim().ToLowerInvariant();

        bool exists = await _context.TbAccounts.AnyAsync(a =>
            (a.Phone != null && a.Phone == cleanPhone) ||
            (!string.IsNullOrEmpty(cleanEmail) && a.Email != null && a.Email.ToLower() == cleanEmail));

        if (exists)
        {
            TempData["AdminAccountMsg"] = "Số điện thoại hoặc Email này đã tồn tại trong hệ thống.";
            return RedirectToAction(nameof(Index));
        }

        // P0-09: NEVER assign weak default password "123456"!
        bool isGeneratedTempPassword = string.IsNullOrWhiteSpace(password);
        string rawPass;
        if (isGeneratedTempPassword)
        {
            rawPass = GenerateStrongTemporaryPassword();
        }
        else
        {
            rawPass = password!.Trim();
            if (rawPass.Length < 8)
            {
                TempData["AdminAccountMsg"] = "Mật khẩu chỉ định thủ công phải có tối thiểu 8 ký tự (hoặc để trống để hệ thống sinh mật khẩu tạm ngẫu nhiên).";
                return RedirectToAction(nameof(Index));
            }
        }

        string username = !string.IsNullOrEmpty(cleanEmail) ? cleanEmail.Split('@')[0] : cleanPhone;
        if (await _context.TbAccounts.AnyAsync(a => a.Username == username))
        {
            username = $"{username}_{RandomNumberGenerator.GetInt32(100, 999)}";
        }

        var acc = new TbAccount
        {
            Username = username,
            FullName = fullName.Trim(),
            Phone = cleanPhone,
            Email = string.IsNullOrEmpty(cleanEmail) ? null : cleanEmail,
            SkillLevel = string.IsNullOrWhiteSpace(skillLevel) ? "Giao lưu (DUPR 3.0)" : skillLevel.Trim(),
            MemberTier = string.IsNullOrWhiteSpace(memberTier) ? "Hội viên Tiêu chuẩn" : memberTier.Trim(),
            DominantHand = "Tay phải",
            RoleId = roleId is 1 or 2 or 3 ? roleId : 2,
            IsActive = true,
            MustChangePassword = isGeneratedTempPassword,
            SecurityStamp = Function.GenerateSecurityStamp(),
            LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd")
        };

        acc.Password = _passwordService.HashPassword(acc, rawPass);

        _context.TbAccounts.Add(acc);
        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "ADMIN_ACCOUNT_CREATED",
            EntityType = "Account",
            EntityId = acc.Username,
            ActorAccountId = Function._AccountId,
            ActorUsername = Function._Username,
            Details = $"RoleId={acc.RoleId}, TempPassword={isGeneratedTempPassword}",
            CreatedAt = BookingService.GetVietnamNow()
        });
        await _context.SaveChangesAsync();

        if (isGeneratedTempPassword)
        {
            TempData["AdminAccountSuccess"] =
                $"Đã tạo tài khoản {acc.FullName} (#SPK-{acc.AccountId:D4}). Mật khẩu tạm thời dùng 1 lần: {rawPass} (Vui lòng giao trực tiếp cho chủ tài khoản và yêu cầu đổi mật khẩu ngay khi đăng nhập).";
        }
        else
        {
            TempData["AdminAccountSuccess"] = $"Đã tạo tài khoản {acc.FullName} (#SPK-{acc.AccountId:D4}) thành công!";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMemberTier(int id, string memberTier, string? skillLevel, int roleId = 2)
    {
        var acc = await _context.TbAccounts.FindAsync(id);
        if (acc != null)
        {
            int previousRole = acc.RoleId ?? 2;
            acc.MemberTier = memberTier;
            if (!string.IsNullOrWhiteSpace(skillLevel))
            {
                acc.SkillLevel = skillLevel;
            }
            if (acc.AccountId != Function._AccountId)
            {
                acc.RoleId = roleId is 1 or 2 or 3 ? roleId : 2;
                if (acc.RoleId != previousRole)
                {
                    // Rotate SecurityStamp so changed privileges take effect immediately (P0-02)
                    acc.SecurityStamp = Function.GenerateSecurityStamp();
                }
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
            acc.SecurityStamp = Function.GenerateSecurityStamp();
            await _context.SaveChangesAsync();
            TempData["AdminAccountSuccess"] = $"Đã {(acc.IsActive ? "mở khóa" : "tạm khóa & thu hồi phiên đăng nhập của")} tài khoản {acc.FullName}.";
        }
        return RedirectToAction(nameof(Index));
    }
}
