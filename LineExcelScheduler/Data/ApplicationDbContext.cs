using Microsoft.EntityFrameworkCore;
using LineExcelScheduler.Models;

namespace LineExcelScheduler.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        // ต้องสะกดว่า teams (t ตัวเล็ก) เพื่อให้ตรงกับ ExcelService
        public DbSet<Team> teams { get; set; } 
        public DbSet<FactTeamAmount> fact_team_amounts { get; set; }
        public DbSet<FactTeamRoleManday> fact_team_role_mandays { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema("Line_projrct"); //

            modelBuilder.Entity<Team>().ToTable("teams");
            modelBuilder.Entity<FactTeamAmount>().ToTable("fact_team_amounts");
            modelBuilder.Entity<FactTeamRoleManday>().ToTable("fact_team_role_mandays");
        }
    }
}