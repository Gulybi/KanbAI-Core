using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbAI_Core.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardColumnAndKanbanTaskEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoardColumns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ColorCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ColumnOrder = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardColumns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardColumns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KanbanTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TaskOrder = table.Column<int>(type: "int", nullable: false),
                    ColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KanbanTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KanbanTasks_BoardColumns_ColumnId",
                        column: x => x.ColumnId,
                        principalTable: "BoardColumns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KanbanTasks_Users_AssignedId",
                        column: x => x.AssignedId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoardColumns_ProjectId",
                table: "BoardColumns",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_KanbanTasks_AssignedId",
                table: "KanbanTasks",
                column: "AssignedId");

            migrationBuilder.CreateIndex(
                name: "IX_KanbanTasks_ColumnId",
                table: "KanbanTasks",
                column: "ColumnId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KanbanTasks");

            migrationBuilder.DropTable(
                name: "BoardColumns");
        }
    }
}
