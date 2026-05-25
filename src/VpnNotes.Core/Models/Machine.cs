using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VpnNotes.Core.Models
{
    public class Machine
    {
        public int Id { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public DateTime? LastSeen { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
