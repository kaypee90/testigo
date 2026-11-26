using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Testigo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTestigo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "testigo");

            migrationBuilder.CreateTable(
                name: "case_counters",
                schema: "testigo",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Next = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_counters", x => x.OrganizationId);
                });

            migrationBuilder.CreateTable(
                name: "case_links",
                schema: "testigo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OtherCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    SuggestedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cases",
                schema: "testigo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ImmediateDanger = table.Column<bool>(type: "boolean", nullable: false),
                    DangerSource = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AssigneeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TaxonomyVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VerifiedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "classifications",
                schema: "testigo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Dimension = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Term = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ValueCipher = table.Column<byte[]>(type: "bytea", nullable: true),
                    QuoteCipher = table.Column<byte[]>(type: "bytea", nullable: true),
                    EvidenceStart = table.Column<int>(type: "integer", nullable: true),
                    EvidenceLength = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TaxonomyVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consents",
                schema: "testigo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NoticeVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Processing = table.Column<bool>(type: "boolean", nullable: false),
                    Contact = table.Column<bool>(type: "boolean", nullable: false),
                    Publication = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                schema: "testigo",
                columns: table => new
                {
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MethodCipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    ValueCipher = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.ReportId);
                });

            migrationBuilder.CreateTable(
                name: "data_keys",
                schema: "testigo",
                columns: table => new
                {
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WrappedKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    MasterKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_keys", x => x.KeyId);
                });

            migrationBuilder.CreateTable(
                name: "redacted_copies",
                schema: "testigo",
                columns: table => new
                {
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TextCipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    MapCipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    MethodVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Types = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_redacted_copies", x => x.ReportId);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                schema: "testigo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CaseCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PassphraseHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FormVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NoticeVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Country = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AnswersCipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    FreeTextCipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnalysisState = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AnalysisStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AnalysisFinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AnalysisNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "staff_roles",
                schema: "testigo",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GrantedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_roles", x => new { x.OrganizationId, x.UserId, x.Role });
                });

            migrationBuilder.CreateTable(
                name: "status_changes",
                schema: "testigo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    From = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    To = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReasonCipher = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_status_changes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "triage_cache",
                schema: "testigo",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CacheKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArgumentsCipher = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triage_cache", x => new { x.OrganizationId, x.CacheKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_case_links_CaseId_OtherCaseId",
                schema: "testigo",
                table: "case_links",
                columns: new[] { "CaseId", "OtherCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_case_links_OtherCaseId",
                schema: "testigo",
                table: "case_links",
                column: "OtherCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_cases_OrganizationId_Number",
                schema: "testigo",
                table: "cases",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cases_OrganizationId_Status_ImmediateDanger_CreatedAt",
                schema: "testigo",
                table: "cases",
                columns: new[] { "OrganizationId", "Status", "ImmediateDanger", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_cases_ReportId",
                schema: "testigo",
                table: "cases",
                column: "ReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classifications_CaseId",
                schema: "testigo",
                table: "classifications",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_classifications_OrganizationId_Dimension_Term_State",
                schema: "testigo",
                table: "classifications",
                columns: new[] { "OrganizationId", "Dimension", "Term", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_consents_ReportId",
                schema: "testigo",
                table: "consents",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_data_keys_OrganizationId_Purpose",
                schema: "testigo",
                table: "data_keys",
                columns: new[] { "OrganizationId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_redacted_copies_OrganizationId_CreatedAt",
                schema: "testigo",
                table: "redacted_copies",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_reports_AnalysisState_SubmittedAt",
                schema: "testigo",
                table: "reports",
                columns: new[] { "AnalysisState", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_reports_CaseCodeHash",
                schema: "testigo",
                table: "reports",
                column: "CaseCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reports_OrganizationId_SubmittedAt",
                schema: "testigo",
                table: "reports",
                columns: new[] { "OrganizationId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_status_changes_CaseId_At",
                schema: "testigo",
                table: "status_changes",
                columns: new[] { "CaseId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_counters",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "case_links",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "cases",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "classifications",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "consents",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "contacts",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "data_keys",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "redacted_copies",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "reports",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "staff_roles",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "status_changes",
                schema: "testigo");

            migrationBuilder.DropTable(
                name: "triage_cache",
                schema: "testigo");
        }
    }
}
