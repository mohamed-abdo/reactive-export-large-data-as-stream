using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExportAdapters
{
    public interface IExportAdpter
    {
        Task<T> Export<T>(IEnumerable<DataTable> data, Func<byte[], Task<T>> callback);
        Task<T> Export<T>(IEnumerable<Task<DataTable>> reactiveStream, Action<byte[]> callback, Action<long> onComplete);
        Task Export<T>(IObservable<T> reactiveStream, Action<byte[]> callback, Action<long> onComplete) where T : DataTable;
        Task Export<T>(IObservable<T> reactiveStream, Stream writtingStream, Action callback, Action<long> onComplete) where T : ReportDataReadyEventArgs;
        Task Export<T>(Func<T> reactiveFunction, Stream writtingStream, Action callback, Action<long> onComplete) where T : ReportDataReadyEventArgs;
    }
    public interface IExportTableAdpter
    {
        Task<T> Export<T>(DataTable data, bool isHeader, Func<byte[], Task<T>> callback);
    }
    public static class AdapterFactory
    {
        const string TXT_FORMAT = "txt";
        const string XSL_FORMAT = "xlsx";
        const string PDF_FORMAT = "pdf";
        public static ExporterAdaptee Create(string TargetFormat)
        {
            ExporterAdaptee adapter;
            if (string.IsNullOrEmpty(TargetFormat))
                throw new ArgumentNullException();
            switch (TargetFormat.ToLower())
            {
                case TXT_FORMAT:
                    {
                        adapter = new CSVAdapter();
                        break;
                    }
                case XSL_FORMAT:
                    {
                        adapter = new ExcelAdapter();
                        break;
                    }
                case PDF_FORMAT:
                    {
                        adapter = new PDFAdapter();
                        break;
                    }
                default:
                    {
                        throw new Exception("FailedToExportOutOfRangeTargetFormat");
                    }
            }
            return adapter;
        }
    }
    public abstract class ExporterAdaptee : IExportAdpter
    {
        protected const int EXPORT_TIMEOUT_HR = 3;
        private AutoResetEvent dataSentCompletedEvent;
        protected AutoResetEvent exportCompletedEvent;
        //#region utilities
        protected static string ToTitleCase(string word)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(word);
        }
        protected static Func<string, byte[]> getStreamFromString = (strValue) =>
        {
            return Encoding.UTF8.GetBytes(strValue);
        };
        protected static Func<DataTable, IEnumerable<string>> getHeader = (tableStructure) =>
        {
            return tableStructure.Columns.Cast<DataColumn>().Select(column => ToTitleCase(column.ColumnName));
        };
        protected static Func<MemoryStream, string> getStringFromStream = (stream) =>
        {
            return Encoding.UTF8.GetString(stream.ToArray());
        };
        //#endregion
        public virtual DocumentStructure DocumentStructure { get; set; }
        public abstract string GetContentType { get; }

        protected AutoResetEvent DataSentCompletedEvent
        {
            get
            {
                return dataSentCompletedEvent;
            }

            set
            {
                dataSentCompletedEvent = value;
            }
        }

        public abstract Task<T> Export<T>(IEnumerable<DataTable> reactiveStream, Func<byte[], Task<T>> callback);
        public abstract Task<T> Export<T>(IEnumerable<Task<DataTable>> reactiveStream, Action<byte[]> callback, Action<long> onComplete);
        public abstract Task Export<T>(IObservable<T> reactiveStream, Action<byte[]> callback, Action<long> onComplete) where T : DataTable;
        public abstract Task Export<T>(IObservable<T> reactiveStream, Stream writtingStream, Action callback, Action<long> onComplete) where T : ReportDataReadyEventArgs;
        public abstract Task Export<T>(Func<T> reactiveFunction, Stream writtingStream, Action callback, Action<long> onComplete) where T : ReportDataReadyEventArgs;
    }
}