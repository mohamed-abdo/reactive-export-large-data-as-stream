using System;
using System.Data;

namespace ExportAdapters
{
    public class ReportDataReadyEventArgs : EventArgs
    {
        public DataTable ReportDataTable { get; protected set; }
        public bool IsLast { get; protected set; }
        public ReportDataReadyEventArgs(DataTable data, bool isLast)
        {
            this.ReportDataTable = data;
            this.IsLast = isLast;
        }
    }
}
