using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LineExcelScheduler.Models
{
    public class Team
    {
        [Key]
        public int id { get; set; } //
        
        public string team_code { get; set; } //
        
        public string team_name { get; set; } //
        
        public int? sort_order { get; set; } //
        
        public DateTime? created_at { get; set; } //
    }
}