using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlR.Web.Server.Data.Migrations;

[DbContext(typeof(AppDb))]
[Migration("20260730120000_Add_AssistanceAuthorizations")]
public class Add_AssistanceAuthorizations : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.CreateTable(
      name: "AssistanceAuthorizations",
      columns: table => new
      {
        Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
        Capability = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
        ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
        CloseReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
        ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
        ConnectorInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
        ConnectorSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
        DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
        EnableGeneration = table.Column<long>(type: "bigint", nullable: false),
        EndpointId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
        ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
        SessionCorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
        Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
        TenantId = table.Column<Guid>(type: "uuid", nullable: false),
        UserCorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
        UserDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
        ViewerConnectionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
      },
      constraints: table =>
      {
        table.PrimaryKey("PK_AssistanceAuthorizations", x => x.Id);
        table.ForeignKey(
          name: "FK_AssistanceAuthorizations_Devices_DeviceId",
          column: x => x.DeviceId,
          principalTable: "Devices",
          principalColumn: "Id",
          onDelete: ReferentialAction.Cascade);
        table.ForeignKey(
          name: "FK_AssistanceAuthorizations_Tenants_TenantId",
          column: x => x.TenantId,
          principalTable: "Tenants",
          principalColumn: "Id",
          onDelete: ReferentialAction.Cascade);
      });

    migrationBuilder.CreateIndex(
      name: "IX_AssistanceAuthorizations_DeviceId_ConnectorInstanceId_EnableGeneration",
      table: "AssistanceAuthorizations",
      columns: ["DeviceId", "ConnectorInstanceId", "EnableGeneration"]);

    migrationBuilder.CreateIndex(
      name: "IX_AssistanceAuthorizations_EndpointId",
      table: "AssistanceAuthorizations",
      column: "EndpointId",
      unique: true,
      filter: "\"Status\" IN ('Pending', 'Connected')");

    migrationBuilder.CreateIndex(
      name: "IX_AssistanceAuthorizations_EndpointId_Status",
      table: "AssistanceAuthorizations",
      columns: ["EndpointId", "Status"]);

    migrationBuilder.CreateIndex(
      name: "IX_AssistanceAuthorizations_TenantId",
      table: "AssistanceAuthorizations",
      column: "TenantId");
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.DropTable(name: "AssistanceAuthorizations");
  }
}
