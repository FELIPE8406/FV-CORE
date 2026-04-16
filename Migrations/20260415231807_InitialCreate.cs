using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FvCore.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FotoUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titulo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RutaArchivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Duracion = table.Column<TimeSpan>(type: "time", nullable: true),
                    Genero = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Calidad = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false),
                    AlbumName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CoverPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ArtistId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItems_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlaylistMediaItems",
                columns: table => new
                {
                    PlaylistId = table.Column<int>(type: "int", nullable: false),
                    MediaItemId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistMediaItems", x => new { x.PlaylistId, x.MediaItemId });
                    table.ForeignKey(
                        name: "FK_PlaylistMediaItems_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistMediaItems_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Nombre",
                table: "Artists",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ArtistId",
                table: "MediaItems",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_IsFavorite",
                table: "MediaItems",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_RutaArchivo",
                table: "MediaItems",
                column: "RutaArchivo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_Tipo",
                table: "MediaItems",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistMediaItems_MediaItemId",
                table: "PlaylistMediaItems",
                column: "MediaItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistMediaItems");

            migrationBuilder.DropTable(
                name: "MediaItems");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropTable(
                name: "Artists");
        }
    }
}
