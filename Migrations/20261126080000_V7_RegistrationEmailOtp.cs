using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PickleballClub.Models;

#nullable disable

namespace PickleballClub.Migrations;

[DbContext(typeof(PickleballContext))]
[Migration("20261126080000_V7_RegistrationEmailOtp")]
public partial class V7_RegistrationEmailOtp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF COL_LENGTH('tb_Account', 'EmailVerifiedAtUtc') IS NULL
BEGIN
    ALTER TABLE tb_Account ADD EmailVerifiedAtUtc DATETIME2 NULL;
END
");

        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_RegistrationVerification', 'U') IS NULL
BEGIN
    CREATE TABLE tb_RegistrationVerification (
        VerificationId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RequestToken NVARCHAR(64) NOT NULL,
        SessionBindingHash NVARCHAR(64) NOT NULL,
        FullName NVARCHAR(150) NOT NULL,
        Username NVARCHAR(50) NOT NULL,
        Phone NVARCHAR(50) NOT NULL,
        Email NVARCHAR(150) NOT NULL,
        EmailHash NVARCHAR(64) NOT NULL,
        PasswordHash NVARCHAR(256) NULL,
        OtpHmac NVARCHAR(128) NULL,
        OtpVersion INT NOT NULL CONSTRAINT DF_tb_RegistrationVerification_OtpVersion DEFAULT (1),
        AttemptCount INT NOT NULL CONSTRAINT DF_tb_RegistrationVerification_AttemptCount DEFAULT (0),
        SendCount INT NOT NULL CONSTRAINT DF_tb_RegistrationVerification_SendCount DEFAULT (0),
        Status NVARCHAR(24) NOT NULL CONSTRAINT DF_tb_RegistrationVerification_Status DEFAULT (N'Pending'),
        LastDeliveryStatus NVARCHAR(24) NOT NULL CONSTRAINT DF_tb_RegistrationVerification_LastDeliveryStatus DEFAULT (N'PendingSend'),
        ClientIpHash NVARCHAR(64) NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_RegistrationVerification_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_RegistrationVerification_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        LastSentAtUtc DATETIME2 NULL,
        OtpExpiresAtUtc DATETIME2 NOT NULL,
        RequestExpiresAtUtc DATETIME2 NOT NULL,
        ConsumedAtUtc DATETIME2 NULL,
        CreatedAccountId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_tb_RegistrationVerification_tb_Account FOREIGN KEY (CreatedAccountId) REFERENCES tb_Account(AccountId) ON DELETE SET NULL
    );
END
");

        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_RegistrationVerification_RequestToken' AND object_id = OBJECT_ID('tb_RegistrationVerification'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_RegistrationVerification_RequestToken ON tb_RegistrationVerification(RequestToken);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_RegistrationVerification_SessionStatus' AND object_id = OBJECT_ID('tb_RegistrationVerification'))
BEGIN
    CREATE INDEX IX_tb_RegistrationVerification_SessionStatus ON tb_RegistrationVerification(SessionBindingHash, Status);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_RegistrationVerification_StatusExpiry' AND object_id = OBJECT_ID('tb_RegistrationVerification'))
BEGIN
    CREATE INDEX IX_tb_RegistrationVerification_StatusExpiry ON tb_RegistrationVerification(Status, RequestExpiresAtUtc);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_RegistrationVerification_CreatedAccountId' AND object_id = OBJECT_ID('tb_RegistrationVerification'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_RegistrationVerification_CreatedAccountId ON tb_RegistrationVerification(CreatedAccountId) WHERE [CreatedAccountId] IS NOT NULL;
END
");

        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_RegistrationEmailLog', 'U') IS NULL
BEGIN
    CREATE TABLE tb_RegistrationEmailLog (
        LogId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        VerificationId INT NULL,
        SessionBindingHash NVARCHAR(64) NOT NULL,
        EmailHash NVARCHAR(64) NOT NULL,
        ClientIpHash NVARCHAR(64) NULL,
        Outcome NVARCHAR(24) NOT NULL CONSTRAINT DF_tb_RegistrationEmailLog_Outcome DEFAULT (N'Sent'),
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_RegistrationEmailLog_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_tb_RegistrationEmailLog_CreatedAtUtc ON tb_RegistrationEmailLog(CreatedAtUtc);
    CREATE INDEX IX_tb_RegistrationEmailLog_SessionCreated ON tb_RegistrationEmailLog(SessionBindingHash, CreatedAtUtc);
    CREATE INDEX IX_tb_RegistrationEmailLog_EmailCreated ON tb_RegistrationEmailLog(EmailHash, CreatedAtUtc);
    CREATE INDEX IX_tb_RegistrationEmailLog_IpCreated ON tb_RegistrationEmailLog(ClientIpHash, CreatedAtUtc);
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_RegistrationEmailLog', 'U') IS NOT NULL
    DROP TABLE tb_RegistrationEmailLog;

IF OBJECT_ID('tb_RegistrationVerification', 'U') IS NOT NULL
    DROP TABLE tb_RegistrationVerification;

IF COL_LENGTH('tb_Account', 'EmailVerifiedAtUtc') IS NOT NULL
    ALTER TABLE tb_Account DROP COLUMN EmailVerifiedAtUtc;
");
    }
}
