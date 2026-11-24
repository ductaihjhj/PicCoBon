using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        var roleMember = new TbRole { RoleName = "Member", Description = "Hội viên" };
        db.TbRoles.Add(roleMember);
        await db.SaveChangesAsync();

        var pw = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var userA = new TbAccount
        {
            Username = "sgl_a",
            FullName = "Nguyễn Minh Anh",
            Email = "minhanh_private@example.com",
            Phone = "0901111111",
            SkillLevel = "Giao lưu (DUPR 3.0)",
            PlayStyle = "Đánh đôi phối hợp (All-Court)",
            RoleId = roleMember.RoleId,
            IsActive = true
        };
        userA.Password = pw.HashPassword(userA, "Pass@123");
        var userB = new TbAccount
        {
            Username = "sgl_b",
            FullName = "Trần Hoàng Bảo",
            Email = "bao_private@example.com",
            Phone = "0902222222",
            SkillLevel = "Giao lưu (DUPR 3.0)",
            PlayStyle = "Đánh đôi phối hợp (All-Court)",
            RoleId = roleMember.RoleId,
            IsActive = true
        };
        userB.Password = pw.HashPassword(userB, "Pass@123");
        var userHidden = new TbAccount
        {
            Username = "sgl_hidden",
            FullName = "Lê Ẩn Danh",
            SkillLevel = "Giao lưu (DUPR 3.0)",
            RoleId = roleMember.RoleId,
            IsActive = true
        };
        userHidden.Password = pw.HashPassword(userHidden, "Pass@123");

        db.TbAccounts.AddRange(userA, userB, userHidden);
        await db.SaveChangesAsync();

        _userAId = userA.AccountId;
        _userBId = userB.AccountId;
        _userHiddenId = userHidden.AccountId;
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
    public async Task Unauthenticated_SanGiaoLuu_RedirectsToLogin()
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
    public async Task SocialCourt_RespectsOptInPrivacy_And_SkillConfirmation()
    {
        using var scope = _factory!.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SocialCourtService>();

        // Initially no one opted in -> court list empty
        var initial = await service.GetCourtSnapshotAsync(_userAId);
        Assert.NotNull(initial);
        Assert.False(initial!.MyProfile.IsVisibleOnCourt);
        Assert.Empty(initial.Players);

        // User B opts in WITHOUT confirming skill -> must display "Chưa xác nhận trình độ"
        var (okB, errB, _) = await service.UpdateMyProfileAsync(_userBId, new UpdateSocialProfileRequest
        {
            IsVisibleOnCourt = true,
            IsReadyToPlay = true,
            AllowMatchSuggestion = true,
            SpriteId = "coco",
            SkillConfirmed = false,
            FreeSlots = new List<string> { "Tối T2–T6", "Sáng T7–CN" }
        });
        Assert.True(okB, errB);

        var afterB = await service.GetCourtSnapshotAsync(_userAId);
        Assert.Single(afterB!.Players);
        Assert.Equal("Trần Hoàng Bảo", afterB.Players[0].DisplayName);
        Assert.Equal("Chưa xác nhận trình độ", afterB.Players[0].SkillLabel);
        Assert.False(afterB.Players[0].SkillConfirmed);
        Assert.Equal("coco", afterB.Players[0].SpriteId);
        // Hidden user must not appear
        Assert.DoesNotContain(afterB.Players, p => p.AccountId == _userHiddenId);
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
            SpriteId = "maya",
            SkillConfirmed = true,
            ConfirmedSkillLevel = "Khá (DUPR 3.5+)",
            FreeSlots = new List<string> { "Tối T2–T6" }
        });

        await service.UpdateMyProfileAsync(_userBId, new UpdateSocialProfileRequest
        {
            IsVisibleOnCourt = true,
            IsReadyToPlay = true,
            AllowMatchSuggestion = true,
            SpriteId = "ace",
            SkillConfirmed = true,
            ConfirmedSkillLevel = "Khá (DUPR 3.5+)",
            FreeSlots = new List<string> { "Tối T2–T6", "Sáng T7–CN" }
        });

        // Match suggestion returns User B with reason from real data
        var matches = await service.GetMatchSuggestionsAsync(_userAId);
        Assert.Single(matches);
        Assert.Equal(_userBId, matches[0].AccountId);
        Assert.Contains("Cùng trình độ", matches[0].Reason);

        var tomorrow = SocialCourtService.GetVietnamNow().Date.AddDays(1).ToString("yyyy-MM-dd");

        // Reject self-invite
        var selfRes = await service.CreateInviteAsync(_userAId, new CreateSocialInviteRequest
        {
            ReceiverAccountId = _userAId,
            ProposedDate = tomorrow,
            ProposedStartTime = "19:00",
            DurationMinutes = 60
        });
        Assert.False(selfRes.Ok);

        // Reject hidden receiver
        var hiddenRes = await service.CreateInviteAsync(_userAId, new CreateSocialInviteRequest
        {
            ReceiverAccountId = _userHiddenId,
            ProposedDate = tomorrow,
            ProposedStartTime = "19:00",
            DurationMinutes = 60
        });
        Assert.False(hiddenRes.Ok);

        // Reject past date
        var pastRes = await service.CreateInviteAsync(_userAId, new CreateSocialInviteRequest
        {
            ReceiverAccountId = _userBId,
            ProposedDate = "2020-01-01",
            ProposedStartTime = "19:00",
            DurationMinutes = 60
        });
        Assert.False(pastRes.Ok);

        // Create valid invite (strips HTML tags)
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

        // Duplicate pending invite blocked
        var dup = await service.CreateInviteAsync(_userAId, new CreateSocialInviteRequest
        {
            ReceiverAccountId = _userBId,
            ProposedDate = tomorrow,
            ProposedStartTime = "20:00",
            DurationMinutes = 60
        });
        Assert.False(dup.Ok);

        // User B counters with another time
        var dayAfter = SocialCourtService.GetVietnamNow().Date.AddDays(2).ToString("yyyy-MM-dd");
        var counterRes = await service.RespondInviteAsync(_userBId, created.Invite.InviteId, new RespondSocialInviteRequest
        {
            Action = "counter",
            CounterDate = dayAfter,
            CounterStartTime = "20:00"
        });
        Assert.True(counterRes.Ok, counterRes.Error);
        Assert.Equal("countered", counterRes.Status);

        // User A accepts User B's counter time
        var acceptRes = await service.RespondInviteAsync(_userAId, created.Invite.InviteId, new RespondSocialInviteRequest
        {
            Action = "accept"
        });
        Assert.True(acceptRes.Ok, acceptRes.Error);
        Assert.Equal("accepted", acceptRes.Status);

        var listA = await service.GetInvitesAsync(_userAId);
        Assert.Single(listA.Sent);
        Assert.Equal(dayAfter, listA.Sent[0].ProposedDate);
        Assert.Equal("20:00", listA.Sent[0].ProposedStartTime);
    }
}
