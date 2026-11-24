using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickleballClub.Models;
using PickleballClub.Services;
using Xunit;

namespace PickleballClub.Tests;

public class SanGiaoLuuTests : IAsyncLifetime
{
    private const string MasterConnectionString =
        "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private readonly string _dbName = "PickleballClub_SanGiaoLuuTest_" + Guid.NewGuid().ToString("N")[..8];
    private string ConnectionString =>
        $"Server=.\\SQLEXPRESS;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private WebApplicationFactory<Program>? _factory;
    private int _userAId;
    private int _userBId;
    private int _userHiddenId;
    private int _staffId;
    private int _adminId;

    public async Task InitializeAsync()
    {
        await using (var conn = new SqlConnection(MasterConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{_dbName}]";
            await cmd.ExecuteNonQueryAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                    ["Database:AutoMigrateInDevelopment"] = "false",
                    ["BackgroundWorkers:Enabled"] = "false"
                });
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        await db.Database.MigrateAsync();

        var roleAdmin = new TbRole { RoleName = "Admin", Description = "Quản trị" };
        var roleMember = new TbRole { RoleName = "Customer", Description = "Hội viên" };
        var roleStaff = new TbRole { RoleName = "Staff", Description = "Nhân viên" };
        db.TbRoles.AddRange(roleAdmin, roleMember, roleStaff);
        await db.SaveChangesAsync();

        var pw = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var userA = new TbAccount
        {
            Username = "sgl_user_a_secret",
            FullName = "Nguyễn Minh Anh",
            Email = "minhanh_private@example.com",
            Phone = "0901111111",
            Address = "123 Đường Riêng Tư",
            BirthDate = "1995-05-05",
            EmergencyContact = "0908888888",
            Bio = "Giới thiệu riêng tư không được lộ lên thẻ Sân giao lưu",
            MemberTier = "VIP Gold",
            Avatar = "/images/avatars/mochi.png",
            DominantHand = "Tay phải",
            PaddleBrand = "Joola Perseus 16mm",
            SkillLevel = "Giao lưu (DUPR 3.0)",
            PlayStyle = "Đánh đôi phối hợp (All-Court)",
            RoleId = 2,
            IsActive = true
        };
        userA.Password = pw.HashPassword(userA, "Pass@123");

        var userB = new TbAccount
        {
            Username = "sgl_user_b_secret",
            FullName = "Trần Hoàng Bảo",
            Email = "bao_private@example.com",
            Phone = "0902222222",
            Avatar = "/images/avatars/mochi.png",
            SkillLevel = "Giao lưu (DUPR 3.0)",
            PlayStyle = "Đánh đôi phối hợp (All-Court)",
            RoleId = 2,
            IsActive = true
        };
        userB.Password = pw.HashPassword(userB, "Pass@123");

        var userHidden = new TbAccount
        {
            Username = "sgl_hidden",
            FullName = "Lê Ẩn Danh",
            SkillLevel = "Giao lưu (DUPR 3.0)",
            RoleId = 2,
            IsActive = true
        };
        userHidden.Password = pw.HashPassword(userHidden, "Pass@123");

        var staff = new TbAccount
        {
            Username = "sgl_staff",
            FullName = "Nhân Viên Vận Hành",
            Avatar = "/images/avatars/ace.png",
            RoleId = 3,
            IsActive = true
        };
        staff.Password = pw.HashPassword(staff, "Pass@123");

        var admin = new TbAccount
        {
            Username = "sgl_admin",
            FullName = "Ban Quản Trị",
            Avatar = "/images/avatars/leo.png",
            RoleId = 1,
            IsActive = true
        };
        admin.Password = pw.HashPassword(admin, "Pass@123");

        db.TbAccounts.AddRange(userA, userB, userHidden, staff, admin);
        await db.SaveChangesAsync();

        _userAId = userA.AccountId;
        _userBId = userB.AccountId;
        _userHiddenId = userHidden.AccountId;
        _staffId = staff.AccountId;
        _adminId = admin.AccountId;
    }

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }

        try
        {
            await using var conn = new SqlConnection(MasterConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
IF DB_ID(N'{_dbName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{_dbName}];
END";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
        }
    }

    [Fact]
    public async Task Unauthenticated_SanGiaoLuu_RedirectsToLogin_And_ApiReturns401()
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var res = await client.GetAsync("/SanGiaoLuu");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Contains("/Account/Login", res.Headers.Location?.ToString() ?? string.Empty);

        var apiRes = await client.GetAsync("/api/social/court");
        Assert.Equal(HttpStatusCode.Unauthorized, apiRes.StatusCode);
    }

    [Fact]
    public async Task ProfileAvatar_SyncsSpritePriority_HandlesUpload_And_TwoCustomersShareSameAvatar()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var service = scope.ServiceProvider.GetRequiredService<SocialCourtService>();

        // User A has Avatar = mochi.png in Profile, even if old SpriteId was "ace" -> MUST resolve to "mochi"
        await service.UpdateMyProfileAsync(_userAId, new UpdateSocialProfileRequest
        {
            IsVisibleOnCourt = true,
            IsReadyToPlay = true,
            AllowMatchSuggestion = true,
            SpriteId = "ace",
            SkillConfirmed = true,
            ConfirmedSkillLevel = "Khá (DUPR 3.5+)"
        });

        // User B ALSO has Avatar = mochi.png -> two distinct actors sharing same sprite
        await service.UpdateMyProfileAsync(_userBId, new UpdateSocialProfileRequest
        {
            IsVisibleOnCourt = true,
            IsReadyToPlay = false,
            AllowMatchSuggestion = true,
            SpriteId = "leo",
            SkillConfirmed = false
        });

        var snap1 = await service.GetCourtSnapshotAsync(_userAId);
        Assert.NotNull(snap1);
        Assert.Equal(2, snap1!.Players.Count);

        var actorA = snap1.Players.Single(p => p.AccountId == _userAId);
        var actorB = snap1.Players.Single(p => p.AccountId == _userBId);
        Assert.Equal("mochi", actorA.SpriteId);
        Assert.Equal("preset", actorA.AvatarMode);
        Assert.Equal("mochi", actorB.SpriteId);
        Assert.Equal("preset", actorB.AvatarMode);
        Assert.NotEqual(actorA.DisplayName, actorB.DisplayName);

        // User A changes Profile Avatar from Mochi to Kai -> court updates automatically
        var accA = await db.TbAccounts.FirstAsync(a => a.AccountId == _userAId);
        accA.Avatar = "/images/avatars/kai.png";
        await db.SaveChangesAsync();

        var snap2 = await service.GetCourtSnapshotAsync(_userAId);
        var updatedA = snap2!.Players.Single(p => p.AccountId == _userAId);
        Assert.Equal("kai", updatedA.SpriteId);
        Assert.Equal("preset", updatedA.AvatarMode);

        // User A uploads a custom photo -> avatarMode == "custom-upload"
        accA.Avatar = "/uploads/avatars/custom-member-a.webp";
        await db.SaveChangesAsync();

        var snap3 = await service.GetCourtSnapshotAsync(_userAId);
        var uploadA = snap3!.Players.Single(p => p.AccountId == _userAId);
        Assert.Equal("custom-upload", uploadA.AvatarMode);
        Assert.Equal("/uploads/avatars/custom-member-a.webp", uploadA.Avatar);
    }

    [Fact]
    public async Task StaffAdminAndHiddenAccounts_AreExcluded_And_PrivateFieldsNeverLeak()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var service = scope.ServiceProvider.GetRequiredService<SocialCourtService>();

        // Staff/Admin cannot opt-in via service
        var staffRes = await service.UpdateMyProfileAsync(_staffId, new UpdateSocialProfileRequest { IsVisibleOnCourt = true });
        Assert.False(staffRes.Ok);
        var adminRes = await service.UpdateMyProfileAsync(_adminId, new UpdateSocialProfileRequest { IsVisibleOnCourt = true });
        Assert.False(adminRes.Ok);

        // Even if Staff had a row directly in DB, Staff must NEVER appear on court or in match suggestions
        db.TbSocialProfiles.Add(new TbSocialProfile
        {
            AccountId = _staffId,
            IsVisibleOnCourt = true,
            IsReadyToPlay = true,
            AllowMatchSuggestion = true,
            SpriteId = "ace"
        });
        await db.SaveChangesAsync();

        Assert.Null(await service.GetCourtSnapshotAsync(_staffId));
        Assert.Null(await service.GetCourtSnapshotAsync(_adminId));
        Assert.Empty(await service.GetMatchSuggestionsAsync(_staffId));

        // User A has private data + contact-like text check; User B inspects court JSON
        var accA = await db.TbAccounts.FirstAsync(a => a.AccountId == _userAId);
        accA.FullName = "minhanh_private@example.com"; // accidental email in FullName
        accA.PaddleBrand = "0901111111"; // accidental phone in PaddleBrand
        await db.SaveChangesAsync();

        await service.UpdateMyProfileAsync(_userAId, new UpdateSocialProfileRequest
        {
            IsVisibleOnCourt = true,
            IsReadyToPlay = true,
            AllowMatchSuggestion = true,
            SkillConfirmed = true,
            ConfirmedSkillLevel = "Khá (DUPR 3.5+)"
        });
        await service.UpdateMyProfileAsync(_userBId, new UpdateSocialProfileRequest
        {
            IsVisibleOnCourt = true,
            IsReadyToPlay = true,
            AllowMatchSuggestion = true,
            SkillConfirmed = false
        });

        var snap = await service.GetCourtSnapshotAsync(_userBId);
        Assert.NotNull(snap);
        Assert.DoesNotContain(snap!.Players, p => p.AccountId == _staffId || p.AccountId == _adminId || p.AccountId == _userHiddenId);

        var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.DoesNotContain("minhanh_private@example.com", json);
        Assert.DoesNotContain("0901111111", json);
        Assert.DoesNotContain("123 Đường Riêng Tư", json);
        Assert.DoesNotContain("1995-05-05", json);
        Assert.DoesNotContain("0908888888", json);
        Assert.DoesNotContain("sgl_user_a_secret", json);
        Assert.DoesNotContain("Giới thiệu riêng tư", json);
        Assert.DoesNotContain("VIP Gold", json);

        var dtoA = snap.Players.Single(p => p.AccountId == _userAId);
        Assert.Equal("Hội viên Spike", dtoA.DisplayName);
        Assert.Equal("Chưa cập nhật", dtoA.PaddleBrand);
        Assert.Equal("Tay phải", dtoA.DominantHand);
    }

    [Fact]
    public async Task SocialMatchAndInviteLifecycle_WorksWithServerValidation()
    {
        using var scope = _factory!.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SocialCourtService>();

        await service.UpdateMyProfileAsync(_userAId, new UpdateSocialProfileRequest
        {
            IsVisibleOnCourt = true,
            IsReadyToPlay = true,
            AllowMatchSuggestion = true,
            SkillConfirmed = true,
            ConfirmedSkillLevel = "Khá (DUPR 3.5+)",
            FreeSlots = new List<string> { "Tối T2–T6" }
        });

        await service.UpdateMyProfileAsync(_userBId, new UpdateSocialProfileRequest
        {
            IsVisibleOnCourt = true,
            IsReadyToPlay = true,
            AllowMatchSuggestion = true,
            SkillConfirmed = true,
            ConfirmedSkillLevel = "Khá (DUPR 3.5+)",
            FreeSlots = new List<string> { "Tối T2–T6", "Sáng T7–CN" }
        });

        var matches = await service.GetMatchSuggestionsAsync(_userAId);
        Assert.Single(matches);
        Assert.Equal(_userBId, matches[0].AccountId);
        Assert.Contains("Cùng trình độ", matches[0].Reason);

        var tomorrow = SocialCourtService.GetVietnamNow().Date.AddDays(1).ToString("yyyy-MM-dd");

        Assert.False((await service.CreateInviteAsync(_userAId, new CreateSocialInviteRequest
        {
            ReceiverAccountId = _userAId,
            ProposedDate = tomorrow,
            ProposedStartTime = "19:00",
            DurationMinutes = 60
        })).Ok);

        Assert.False((await service.CreateInviteAsync(_userAId, new CreateSocialInviteRequest
        {
            ReceiverAccountId = _userHiddenId,
            ProposedDate = tomorrow,
            ProposedStartTime = "19:00",
            DurationMinutes = 60
        })).Ok);

        Assert.False((await service.CreateInviteAsync(_userAId, new CreateSocialInviteRequest
        {
            ReceiverAccountId = _staffId,
            ProposedDate = tomorrow,
            ProposedStartTime = "19:00",
            DurationMinutes = 60
        })).Ok);

        var created = await service.CreateInviteAsync(_userAId, new CreateSocialInviteRequest
        {
            ReceiverAccountId = _userBId,
            ProposedDate = tomorrow,
            ProposedStartTime = "19:00",
            DurationMinutes = 60,
            Note = "<b>Đánh đôi</b> tối mai nhé!"
        });
        Assert.True(created.Ok, created.Error);
        Assert.Equal("Đánh đôi tối mai nhé!", created.Invite!.Note);

        var dayAfter = SocialCourtService.GetVietnamNow().Date.AddDays(2).ToString("yyyy-MM-dd");
        var counterRes = await service.RespondInviteAsync(_userBId, created.Invite.InviteId, new RespondSocialInviteRequest
        {
            Action = "counter",
            CounterDate = dayAfter,
            CounterStartTime = "20:00"
        });
        Assert.True(counterRes.Ok, counterRes.Error);
        Assert.Equal("countered", counterRes.Status);

        var acceptRes = await service.RespondInviteAsync(_userAId, created.Invite.InviteId, new RespondSocialInviteRequest
        {
            Action = "accept"
        });
        Assert.True(acceptRes.Ok, acceptRes.Error);
        Assert.Equal("accepted", acceptRes.Status);
    }
}
