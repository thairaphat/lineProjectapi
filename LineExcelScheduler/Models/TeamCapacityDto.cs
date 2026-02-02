using System.Collections.Generic;

namespace LineExcelScheduler.Models
{
    public class TeamDataRow
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; }
        public string RoleCode { get; set; }
        public decimal Manday { get; set; }
        public string Month { get; set; }
        public string Year { get; set; }
        public decimal TargetAmount { get; set; }
        public decimal ActualAmount { get; set; }
    }

    public class GroupedTeamData
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; }
        public string MonthYear { get; set; }
        public List<TeamDataRow> Roles { get; set; } = new List<TeamDataRow>();
    }
}