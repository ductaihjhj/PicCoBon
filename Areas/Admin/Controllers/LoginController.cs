using Microsoft.AspNetCore.Mvc;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class LoginController : Controller
    {
        private readonly PickleballContext _context;

        public LoginController(PickleballContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Index(TbAccount account)
        {
            if (account == null)
            {
                return NotFound();
            }

            string inputUser = (account.Username ?? string.Empty).Trim();
            string rawPass = account.Password ?? string.Empty;
            string md5Pass = HashMD5.GetMD5(rawPass);

            var check = _context.TbAccounts.FirstOrDefault(m =>
                (m.Username == inputUser || m.Email == inputUser || m.Phone == inputUser) &&
                (m.Password == md5Pass || m.Password == rawPass));

            if (check == null)
            {
                Function._Message = "Tài khoản hoặc mật khẩu không chính xác!";
                return RedirectToAction("Index", "Login", new { area = "Admin" });
            }

            Function._Message = string.Empty;
            Function._AccountId = check.AccountId;
            Function._Username = check.Username ?? check.Email ?? string.Empty;
            Function._FullName = check.FullName ?? "Ban Quản Trị CLB";
            Function._Email = check.Email ?? string.Empty;
            Function._Phone = check.Phone ?? string.Empty;
            Function._RoleId = check.RoleId ?? 1;

            Response.Cookies.Append("pb_admin_uid", check.AccountId.ToString(), new CookieOptions
            {
                HttpOnly = false,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/"
            });

            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }
    }
}
