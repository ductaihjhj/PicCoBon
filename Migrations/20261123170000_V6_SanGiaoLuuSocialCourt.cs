using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PickleballClub.Models;

#nullable disable

namespace PickleballClub.Migrations;

[DbContext(typeof(PickleballContext))]
[Migration("20261123170000_V6_SanGiaoLuuSocialCourt")]
public partial class V6_SanGiaoLuuSocialCourt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_SocialProfile', 'U') IS NULL
BEGIN
    CREATE TABLE tb_SocialProfile (
        AccountId INT NOT NULL PRIMARY KEY,
        IsVisibleOnCourt BIT NOT NULL CONSTRAINT DF_tb_SocialProfile_IsVisibleOnCourt DEFAULT (0),
        IsReadyToPlay BIT NOT NULL CONSTRAINT DF_tb_SocialProfile_IsReadyToPlay DEFAULT (0),
        AllowMatchSuggestion BIT NOT NULL CONSTRAINT DF_tb_SocialProfile_AllowMatchSuggestion DEFAULT (0),
        SpriteId NVARCHAR(24) NULL,
        SkillConfirmed BIT NOT NULL CONSTRAINT DF_tb_SocialProfile_SkillConfirmed DEFAULT (0),
        ConfirmedSkillLevel NVARCHAR(40) NULL,
        PreferredFreeSlotsJson NVARCHAR(400) NOT NULL CONSTRAINT DF_tb_SocialProfile_PreferredFreeSlotsJson DEFAULT (N'[]'),
        UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_SocialProfile_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_tb_SocialProfile_tb_Account FOREIGN KEY (AccountId) REFERENCES tb_Account(AccountId) ON DELETE CASCADE
    );
END
");

        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_SocialInvite', 'U') IS NULL
BEGIN
    CREATE TABLE tb_SocialInvite (
        InviteId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SenderAccountId INT NOT NULL,
        ReceiverAccountId INT NOT NULL,
        ProposedDate DATE NOT NULL,
        ProposedStartTime NVARCHAR(5) NOT NULL,
        DurationMinutes INT NOT NULL CONSTRAINT DF_tb_SocialInvite_DurationMinutes DEFAULT (60),
        Note NVARCHAR(200) NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_SocialInvite_Status DEFAULT (N'pending'),
        CounterDate DATE NULL,
        CounterStartTime NVARCHAR(5) NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_SocialInvite_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        RespondedAtUtc DATETIME2 NULL,
        CONSTRAINT FK_tb_SocialInvite_SenderAccount FOREIGN KEY (SenderAccountId) REFERENCES tb_Account(AccountId),
        CONSTRAINT FK_tb_SocialInvite_ReceiverAccount FOREIGN KEY (ReceiverAccountId) REFERENCES tb_Account(AccountId)
    );

    CREATE INDEX IX_tb_SocialInvite_SenderAccountId_Status ON tb_SocialInvite(SenderAccountId, Status);
    CREATE INDEX IX_tb_SocialInvite_ReceiverAccountId_Status ON tb_SocialInvite(ReceiverAccountId, Status);
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_SocialInvite', 'U') IS NOT NULL
    DROP TABLE tb_SocialInvite;
IF OBJECT_ID('tb_SocialProfile', 'U') IS NOT NULL
    DROP TABLE tb_SocialProfile;
");
    }
}
