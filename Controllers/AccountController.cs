using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers
{
    public class AccountController : Controller
    {
        private readonly PickleballContext _context;

        public AccountController(PickleballContext context)
        {
            _context = context;
        }

        [HttpGet("/Account/Login")]
        [HttpGet("/login.html")]
        public IActionResult Login()
        {
            return View();
        }

        [HttpGet("/Account/Profile")]
        [HttpGet("/Profile")]
        [HttpGet("/profile.html")]
        public async Task<IActionResult> Profile()
        {
            if (!Function.IsLogin())
            {
                return Redirect("/Account/Login?redirect=%2FAccount%2FProfile&notice=" + Uri.EscapeDataString("Vui lòng đăng nhập để xem và cập nhật hồ sơ cá nhân."));
            }

            var account = await _context.TbAccounts
                .Include(a => a.Role)
                .FirstOrDefaultAsync(a => a.AccountId == Function._AccountId);

            if (account == null)
            {
                return RedirectToAction("Logout");
            }

            var myBookings = await _context.TbBookings
                .Include(b => b.Court)
                .Where(b => b.AccountId == account.AccountId || (!string.IsNullOrEmpty(account.Phone) && b.Phone == account.Phone))
                .OrderByDescending(b => b.BookingDate)
                .ThenByDescending(b => b.StartTime)
                .ToListAsync();

            var validBookings = myBookings.Where(b => b.Status != "rejected").ToList();
            int totalMatches = validBookings.Count;
            double totalHours = Math.Round(validBookings.Sum(b => b.Duration) / 60.0, 1);
            int totalSpent = validBookings.Sum(b => b.TotalPaid > 0 ? b.TotalPaid : b.CourtFee);

            // Tự động tính hạng hội viên nếu chưa thiết lập
            if (string.IsNullOrWhiteSpace(account.MemberTier))
            {
                if (totalMatches >= 10 || totalSpent >= 2000000)
                    account.MemberTier = "Hội viên Vàng VIP";
                else if (totalMatches >= 3 || totalSpent >= 500000)
                    account.MemberTier = "Hội viên Bạc";
                else
                    account.MemberTier = "Hội viên Tiêu chuẩn";
            }

            ViewBag.MyBookings = myBookings;
            ViewBag.TotalMatches = totalMatches;
            ViewBag.TotalHours = totalHours;
            ViewBag.TotalSpent = totalSpent;

            return View(account);
        }

        [HttpPost("/Account/UpdateProfile")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(
            string fullName,
            string phone,
            string? email,
            string? gender,
            string? birthDate,
            string? address,
            string? avatar,
            string? skillLevel,
            string? dominantHand,
            string? paddleBrand,
            string? playStyle,
            string? emergencyContact,
            string? bio)
        {
            if (!Function.IsLogin())
            {
                return RedirectToAction("Login");
            }

            var account = await _context.TbAccounts.FirstOrDefaultAsync(a => a.AccountId == Function._AccountId);
            if (account == null)
            {
                return RedirectToAction("Login");
            }

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone))
            {
                TempData["ProfileError"] = "Họ và tên cùng Số điện thoại là thông tin bắt buộc.";
                return RedirectToAction("Profile");
            }

            string cleanPhone = phone.Trim();
            string cleanEmail = (email ?? "").Trim().ToLower();

            bool duplicatePhoneOrEmail = await _context.TbAccounts.AnyAsync(a =>
                a.AccountId != account.AccountId &&
                ((a.Phone != null && a.Phone == cleanPhone) ||
                 (!string.IsNullOrEmpty(cleanEmail) && a.Email != null && a.Email.ToLower() == cleanEmail)));

            if (duplicatePhoneOrEmail)
            {
                TempData["ProfileError"] = "Số điện thoại hoặc Email này đã được liên kết với một tài khoản khác.";
                return RedirectToAction("Profile");
            }

            account.FullName = fullName.Trim();
            account.Phone = cleanPhone;
            account.Email = cleanEmail;
            account.Gender = (gender ?? "").Trim();
            account.BirthDate = (birthDate ?? "").Trim();
            account.Address = (address ?? "").Trim();
            account.Avatar = (avatar ?? "").Trim();
            account.SkillLevel = (skillLevel ?? "").Trim();
            account.DominantHand = (dominantHand ?? "").Trim();
            account.PaddleBrand = (paddleBrand ?? "").Trim();
            account.PlayStyle = (playStyle ?? "").Trim();
            account.EmergencyContact = (emergencyContact ?? "").Trim();
            account.Bio = (bio ?? "").Trim();

            await _context.SaveChangesAsync();

            // Đồng bộ ngay biến phiên hiện tại
            Function._FullName = account.FullName;
            Function._Phone = account.Phone;
            Function._Email = account.Email ?? string.Empty;
            Function._Avatar = account.Avatar ?? string.Empty;
            Function._SkillLevel = account.SkillLevel ?? string.Empty;

            TempData["ProfileSuccess"] = "Đã lưu thông tin hồ sơ cá nhân và thông số vận động viên thành công!";
            return RedirectToAction("Profile");
        }

        [HttpPost("/Account/ChangePassword")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            if (!Function.IsLogin())
            {
                return RedirectToAction("Login");
            }

            var account = await _context.TbAccounts.FirstOrDefaultAsync(a => a.AccountId == Function._AccountId);
            if (account == null)
            {
                return RedirectToAction("Login");
            }

            if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            {
                TempData["ProfileError"] = "Vui lòng nhập đầy đủ mật khẩu hiện tại và mật khẩu mới.";
                return RedirectToAction("Profile");
            }

            if (newPassword.Trim().Length < 6)
            {
                TempData["ProfileError"] = "Mật khẩu mới cần có tối thiểu 6 ký tự.";
                return RedirectToAction("Profile");
            }

            if (newPassword.Trim() != (confirmPassword ?? "").Trim())
            {
                TempData["ProfileError"] = "Mật khẩu xác nhận không trùng khớp với mật khẩu mới.";
                return RedirectToAction("Profile");
            }

            string currentMd5 = HashMD5.GetMD5(currentPassword.Trim());
            if (account.Password != currentMd5 && account.Password != currentPassword.Trim())
            {
                TempData["ProfileError"] = "Mật khẩu hiện tại không chính xác.";
                return RedirectToAction("Profile");
            }

            account.Password = HashMD5.GetMD5(newPassword.Trim());
            await _context.SaveChangesAsync();

            TempData["ProfileSuccess"] = "Đổi mật khẩu bảo mật thành công!";
            return RedirectToAction("Profile");
        }

        [HttpGet("/Account/Logout")]
        public IActionResult Logout()
        {
            Function._AccountId = 0;
            Function._Username = string.Empty;
            Function._FullName = string.Empty;
            Function._Email = string.Empty;
            Function._Phone = string.Empty;
            Function._Avatar = string.Empty;
            Function._SkillLevel = string.Empty;
            Function._RoleId = 0;
            Function._Message = string.Empty;
            Response.Cookies.Delete("pb_admin_uid");
            return RedirectToAction("Index", "Home");
        }
    }
}
