using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AccountsController : Controller
    {
        private readonly PickleballContext _context;

        public AccountsController(PickleballContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });

            var accounts = await _context.TbAccounts
                .Include(a => a.Role)
                .Include(a => a.TbBookings)
                .OrderBy(a => a.RoleId)
                .ThenByDescending(a => a.AccountId)
                .ToListAsync();

            var allBookings = await _context.TbBookings
                .Where(b => b.Status != "rejected")
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
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone))
            {
                TempData["AdminAccountMsg"] = "Vui lòng nhập đầy đủ Họ tên và Số điện thoại hội viên.";
                return RedirectToAction(nameof(Index));
            }

            string cleanPhone = phone.Trim();
            string cleanEmail = (email ?? "").Trim().ToLower();

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
                Email = cleanEmail,
                Password = HashMD5.GetMD5(rawPass),
                SkillLevel = string.IsNullOrWhiteSpace(skillLevel) ? "Giao lưu (DUPR 3.0)" : skillLevel.Trim(),
                MemberTier = string.IsNullOrWhiteSpace(memberTier) ? "Hội viên Tiêu chuẩn" : memberTier.Trim(),
                DominantHand = "Tay phải",
                RoleId = roleId == 1 ? 1 : 2,
                IsActive = true
            };

            _context.TbAccounts.Add(acc);
            await _context.SaveChangesAsync();

            TempData["AdminAccountSuccess"] = $"Đã tạo tài khoản hội viên {acc.FullName} (#SPK-{acc.AccountId:D4}) thành công!";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMemberTier(int id, string memberTier, string? skillLevel, int roleId = 2)
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });

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
                    acc.RoleId = roleId == 1 ? 1 : 2;
                }
                await _context.SaveChangesAsync();
                TempData["AdminAccountSuccess"] = $"Đã cập nhật hạng thẻ & thông tin cho {acc.FullName}.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });

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
}
