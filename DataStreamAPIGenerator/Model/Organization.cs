using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DataStreamAPIGenerator.Model
{
    public class Organization
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Country { get; set; }
        public string Address { get; set; }
        public Uri WebSite { get; set; }
    }
}
