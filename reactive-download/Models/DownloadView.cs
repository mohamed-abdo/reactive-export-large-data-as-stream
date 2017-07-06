using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace reactive_download.Models
{
    public class DownloadView
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string Mobile { get; set; }
        public DateTime Bithdate { get; set; }
        public string Gender { get; set; }
        public string EmployeId { get; set; }
        public string Organization { get; set; }
        public string WorkingCountry { get; set; }
        public string WorkingAddress { get; set; }
    }
}