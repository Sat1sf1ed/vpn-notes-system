using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VpnNotes.Core.Models
{
    public class Note
    {
        public int Id { get; set; }
        public int? MachineId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string[]? Tags { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;  // ← новое
    }
}
