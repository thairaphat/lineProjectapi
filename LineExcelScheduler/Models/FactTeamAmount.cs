using System.ComponentModel.DataAnnotations;

namespace LineExcelScheduler.Models
{
    public class FactTeamAmount
    {
        [Key]
        public int id { get; set; } //
        
        public int team_id { get; set; } //
        
        public int year { get; set; } //
        
        public int month { get; set; } //
        
        public decimal target_amount { get; set; } //
        
        public decimal actual_amount { get; set; } //
        
        public DateTime? created_at { get; set; } //
    }
}