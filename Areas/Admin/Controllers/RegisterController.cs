using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers;

/// <summary>
/// P0-03: Restricted strictly to authenticated Admin role so anonymous visitors cannot self-register as Admin.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
public class RegisterController : Controller
{
    private readonly PickleballContext _context;
    private readonly PasswordService _passwordService;

    public RegisterController(PickleballContext context, PasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(TbAccount account)
    {
        if (account == null || string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrWhiteSpace(account.Password))
        {
            TempData["AuthError"] = "Vui lòng nhập đầy đủ thông tin tài khoản!";
            return RedirectToAction("Index", "Register", new { area = "Admin" });
        }

        if (account.Password.Trim().Length < 8)
        {
            TempData["AuthError"] = "Mật khẩu tài khoản nội bộ phải có tối thiểu 8 ký tự!";
            return RedirectToAction("Index", "Register", new { area = "Admin" });
        }

        var check = await _context.TbAccounts.FirstOrDefaultAsync(m =>
            m.Username == account.Username ||
            (!string.IsNullOrEmpty(account.Email) && m.Email == account.Email));

        if (check != null)
        {
            TempData["AuthError"] = "Trùng tài khoản hoặc Email đã tồn tại!";
            return RedirectToAction("Index", "Register", new { area = "Admin" });
        }

        account.Username = account.Username.Trim();
        account.Password = _passwordService.HashPassword(account, account.Password.Trim());
        account.RoleId = account.RoleId is 1 or 2 or 3 ? account.RoleId : 3;
        account.IsActive = true;
        account.MustChangePassword = false;
        account.SecurityStamp = Function.GenerateSecurityStamp();
        account.LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");

        _context.Add(account);
        await _context.SaveChangesAsync();
        return RedirectToAction("Index", "Accounts", new { area = "Admin" });
    }
}
