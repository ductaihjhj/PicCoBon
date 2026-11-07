using Microsoft.AspNetCore.Mvc;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class RegisterController : Controller
    {
        private readonly PickleballContext _context;

        public RegisterController(PickleballContext context)
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
            if (account == null) return NotFound();

            var check = _context.TbAccounts.FirstOrDefault(m => m.Username == account.Username || m.Email == account.Email);
            if (check != null)
            {
                Function._Message = "Trùng tài khoản hoặc Email đã tồn tại!";
                return RedirectToAction("Index", "Register", new { area = "Admin" });
            }

            Function._Message = string.Empty;
            account.Password = HashMD5.GetMD5(account.Password ?? string.Empty);
            account.RoleId = account.RoleId ?? 1;
            account.IsActive = true;
            _context.Add(account);
            _context.SaveChanges();
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }
    }
}
