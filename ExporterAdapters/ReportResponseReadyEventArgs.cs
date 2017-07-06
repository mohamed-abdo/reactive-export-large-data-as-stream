using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ExportAdapters
{
    public class ReportResponseReadyEventArgs : EventArgs
    {
        public HttpResponseMessage ReportDataTable { get; protected set; }
        public Tuple<int, int> dataInfo { get; protected set; }
        public ReportResponseReadyEventArgs(Tuple<int, int> dataInfo, HttpResponseMessage data)
        {
            this.dataInfo = dataInfo;
            this.ReportDataTable = data;
        }
    }
}
