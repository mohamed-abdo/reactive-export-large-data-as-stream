using System;
using System.Collections.Generic;
using System.Linq;

namespace ExportAdapters
{
    public sealed class DocumentStructure
    {
        public IEnumerable<string> Headers { get; set; }
        public string username { get; set; }
        public string BusinessName { get; set; }
        public string ReportName { get; set; }
        public string GeneratedAt { get; set; }
        public string CopyRight { get; set; }
        public IEnumerable<string> Criteria { get; set; }
    }
}