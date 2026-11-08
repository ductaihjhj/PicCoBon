using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers;

[Area("Admin")]
public class LoginController : Controller
{
    private readonly PickleballContext _context;
    private readonly PasswordService _passwordService;

    public LoginController(PickleballContext context, PasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    public IActionResult Index()
    {
        if (Function.IsStaffOrAdmin())
        {
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }
        return View();
    }

    [HttpPost]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> Index(TbAccount account)
    {
        if (account == null || string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrEmpty(account.Password))
        {
            Function._Message = "Vui lòng nhập đầy đủ tài khoản và mật khẩu quản trị!";
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }

        string inputUser = account.Username.Trim();
        string rawPass = account.Password;

        var check = await _context.TbAccounts.FirstOrDefaultAsync(m =>
            m.IsActive &&
            (m.Username == inputUser || m.Email == inputUser || m.Phone == inputUser));

        if (check == null)
        {
            Function._Message = "Tài khoản hoặc mật khẩu không chính xác!";
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }

        var verify = _passwordService.VerifyPassword(check, check.Password, rawPass, out string? upgradedHash);
        if (verify == PasswordVerificationResult.Failed)
        {
            Function._Message = "Tài khoản hoặc mật khẩu không chính xác!";
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }

        if (check.RoleId != 1 && check.RoleId != 3)
        {
            Function._Message = "Tài khoản này không có quyền truy cập khu vực Quản trị / Điều hành!";
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }

        if (verify == PasswordVerificationResult.SuccessRehashNeeded && !string.IsNullOrEmpty(upgradedHash))
        {
            check.Password = upgradedHash;
        }

        check.LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");
        await _context.SaveChangesAsync();

        Function._Message = string.Empty;
        await Function.SignInAccountAsync(HttpContext, check);

        return RedirectToAction("Index", "Home", new { area = "Admin" });
    }
}
