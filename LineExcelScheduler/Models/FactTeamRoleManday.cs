using System.ComponentModel.DataAnnotations;

namespace LineExcelScheduler.Models
{
    public class FactTeamRoleManday
    {
        [Key]
        public int id { get; set; } //
        
        public string company_code { get; set; } //
        
        public int team_id { get; set; } //
        
        public string role_code { get; set; } //
        
        public int year { get; set; } //
        
        public int month { get; set; } //
        
        public decimal manday { get; set; } //
        
        public DateTime? created_at { get; set; } //
    }
}