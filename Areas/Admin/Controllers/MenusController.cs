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
public class MenusController : Controller
{
    private readonly PickleballContext _context;

    public MenusController(PickleballContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _context.TbMenus.OrderBy(m => m.Position).ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var tbMenu = await _context.TbMenus.FirstOrDefaultAsync(m => m.MenuId == id);
        if (tbMenu == null) return NotFound();
        return View(tbMenu);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("MenuId,Title,Alias,Description,Levels,ParentId,Position,IsActive")] TbMenu tbMenu)
    {
        if (string.IsNullOrWhiteSpace(tbMenu.Title))
        {
            ModelState.AddModelError("Title", "Tiêu đề menu không được để trống.");
        }

        if (ModelState.IsValid)
        {
            tbMenu.Title = tbMenu.Title!.Trim();
            if (string.IsNullOrWhiteSpace(tbMenu.Alias))
            {
                tbMenu.Alias = Function.TitleSlugGenerationAlias(tbMenu.Title);
            }
            tbMenu.CreatedDate = BookingService.GetVietnamNow();
            _context.Add(tbMenu);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(tbMenu);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var tbMenu = await _context.TbMenus.FindAsync(id);
        if (tbMenu == null) return NotFound();
        return View(tbMenu);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("MenuId,Title,Alias,Description,Levels,ParentId,Position,IsActive")] TbMenu tbMenu)
    {
        if (id != tbMenu.MenuId) return NotFound();

        if (string.IsNullOrWhiteSpace(tbMenu.Title))
        {
            ModelState.AddModelError("Title", "Tiêu đề menu không được để trống.");
        }

        if (ModelState.IsValid)
        {
            tbMenu.Title = tbMenu.Title!.Trim();
            tbMenu.ModifiedDate = BookingService.GetVietnamNow();
            _context.Update(tbMenu);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(tbMenu);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var tbMenu = await _context.TbMenus.FirstOrDefaultAsync(m => m.MenuId == id);
        if (tbMenu == null) return NotFound();
        return View(tbMenu);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var tbMenu = await _context.TbMenus.FindAsync(id);
        if (tbMenu != null)
        {
            _context.TbMenus.Remove(tbMenu);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
