using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Controllers;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class CourtsController : Controller
{
    private readonly PickleballContext _context;

    public CourtsController(PickleballContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _context.TbCourts.AsNoTracking().OrderBy(c => c.CourtId).ToListAsync());
    }

    public IActionResult Create()
    {
        return View(new TbCourt
        {
            CourtType = "Indoor",
            TypeLabel = "Trong nhà · Mái vòm & Đèn LED chống chói",
            Surface = "Acrylic Cushion 8 lớp tiêu chuẩn thi đấu",
            Image = "/assets/court-vip-arena.png",
            BasePrice = 120000,
            Surcharge = 20000,
            IsActive = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("CourtId,CourtName,Alias,CourtType,TypeLabel,Surface,Image,BasePrice,Surcharge,Description,IsActive")] TbCourt tbCourt)
    {
        if (string.IsNullOrWhiteSpace(tbCourt.CourtName))
        {
            ModelState.AddModelError("CourtName", "Vui lòng nhập tên sân thi đấu.");
        }
        if (tbCourt.BasePrice <= 0)
        {
            ModelState.AddModelError("BasePrice", "Giá cơ bản mỗi giờ phải lớn hơn 0đ.");
        }
        if (tbCourt.Surcharge < 0)
        {
            ModelState.AddModelError("Surcharge", "Phụ thu không được âm.");
        }

        if (ModelState.IsValid)
        {
            tbCourt.CourtName = tbCourt.CourtName!.Trim();
            tbCourt.Alias = Function.TitleSlugGenerationAlias(tbCourt.CourtName);
            tbCourt.Image = ApiDbController.NormalizeCourtImageUrl(tbCourt.Image, tbCourt.CourtType);
            _context.Add(tbCourt);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(tbCourt);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var court = await _context.TbCourts.FindAsync(id);
        if (court == null) return NotFound();
        return View(court);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("CourtId,CourtName,Alias,CourtType,TypeLabel,Surface,Image,BasePrice,Surcharge,Description,IsActive")] TbCourt tbCourt)
    {
        if (id != tbCourt.CourtId) return NotFound();

        if (string.IsNullOrWhiteSpace(tbCourt.CourtName))
        {
            ModelState.AddModelError("CourtName", "Vui lòng nhập tên sân thi đấu.");
        }
        if (tbCourt.BasePrice <= 0)
        {
            ModelState.AddModelError("BasePrice", "Giá cơ bản mỗi giờ phải lớn hơn 0đ.");
        }
        if (tbCourt.Surcharge < 0)
        {
            ModelState.AddModelError("Surcharge", "Phụ thu không được âm.");
        }

        if (ModelState.IsValid)
        {
            var existing = await _context.TbCourts.FindAsync(id);
            if (existing == null) return NotFound();

            existing.CourtName = tbCourt.CourtName!.Trim();
            existing.Alias = Function.TitleSlugGenerationAlias(existing.CourtName);
            existing.CourtType = tbCourt.CourtType;
            existing.TypeLabel = tbCourt.TypeLabel;
            existing.Surface = tbCourt.Surface;
            existing.Image = ApiDbController.NormalizeCourtImageUrl(tbCourt.Image, tbCourt.CourtType);
            existing.BasePrice = tbCourt.BasePrice;
            existing.Surcharge = tbCourt.Surcharge;
            existing.Description = tbCourt.Description;
            existing.IsActive = tbCourt.IsActive;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(tbCourt);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var court = await _context.TbCourts.FindAsync(id);
        if (court != null)
        {
            court.IsActive = !court.IsActive;
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
