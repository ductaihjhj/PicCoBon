using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers;

[Authorize]
public class SanGiaoLuuController : Controller
{
    private readonly SocialCourtService _socialService;

    public SanGiaoLuuController(SocialCourtService socialService)
    {
        _socialService = socialService;
    }

    [HttpGet("/SanGiaoLuu")]
    public async Task<IActionResult> Index()
    {
        int accountId = Function._AccountId;
        if (accountId <= 0)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = "/SanGiaoLuu" });
        }

        var snapshot = await _socialService.GetCourtSnapshotAsync(accountId);
        if (snapshot == null)
        {
            return RedirectToAction("Login", "Account");
        }

        return View(snapshot);
    }

    [HttpGet("/api/social/court")]
    public async Task<IActionResult> GetCourt()
    {
        int accountId = Function._AccountId;
        if (accountId <= 0) return Unauthorized(new { ok = false, message = "Vui lòng đăng nhập." });

        var snapshot = await _socialService.GetCourtSnapshotAsync(accountId);
        if (snapshot == null) return Unauthorized(new { ok = false, message = "Tài khoản không hợp lệ." });

        return Ok(new { ok = true, myProfile = snapshot.MyProfile, players = snapshot.Players });
    }

    [HttpPut("/api/social/profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateSocialProfileRequest? req)
    {
        int accountId = Function._AccountId;
        if (accountId <= 0) return Unauthorized(new { ok = false, message = "Vui lòng đăng nhập." });
        if (req == null) return BadRequest(new { ok = false, message = "Dữ liệu không hợp lệ." });

        var (ok, error, profile) = await _socialService.UpdateMyProfileAsync(accountId, req);
        if (!ok) return BadRequest(new { ok = false, message = error });

        return Ok(new { ok = true, profile });
    }

    [HttpGet("/api/social/matches")]
    public async Task<IActionResult> GetMatches()
    {
        int accountId = Function._AccountId;
        if (accountId <= 0) return Unauthorized(new { ok = false, message = "Vui lòng đăng nhập." });

        var suggestions = await _socialService.GetMatchSuggestionsAsync(accountId);
        return Ok(new { ok = true, suggestions });
    }

    [HttpGet("/api/social/invites")]
    public async Task<IActionResult> GetInvites()
    {
        int accountId = Function._AccountId;
        if (accountId <= 0) return Unauthorized(new { ok = false, message = "Vui lòng đăng nhập." });

        var invites = await _socialService.GetInvitesAsync(accountId);
        return Ok(new { ok = true, sent = invites.Sent, received = invites.Received });
    }

    [HttpPost("/api/social/invites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInvite([FromBody] CreateSocialInviteRequest? req)
    {
        int accountId = Function._AccountId;
        if (accountId <= 0) return Unauthorized(new { ok = false, message = "Vui lòng đăng nhập." });
        if (req == null) return BadRequest(new { ok = false, message = "Dữ liệu lời mời không hợp lệ." });

        var (ok, error, invite) = await _socialService.CreateInviteAsync(accountId, req);
        if (!ok) return BadRequest(new { ok = false, message = error });

        return Ok(new { ok = true, invite });
    }

    [HttpPatch("/api/social/invites/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondInvite(int id, [FromBody] RespondSocialInviteRequest? req)
    {
        int accountId = Function._AccountId;
        if (accountId <= 0) return Unauthorized(new { ok = false, message = "Vui lòng đăng nhập." });
        if (req == null) return BadRequest(new { ok = false, message = "Dữ liệu phản hồi không hợp lệ." });

        var (ok, error, status) = await _socialService.RespondInviteAsync(accountId, id, req);
        if (!ok) return BadRequest(new { ok = false, message = error });

        return Ok(new { ok = true, status });
    }
}
