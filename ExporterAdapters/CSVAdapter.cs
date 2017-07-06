using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.Threading.Tasks;
using System.Reactive;
using System.Threading;
using System.IO;
using System.Reactive.Linq;
namespace ExportAdapters
{
    public class CSVAdapter : ExporterAdaptee, IExportTableAdpter
    {
        private const string CONTENT_TYPE = "text/txt";
        //#region Helpers
        static Func<DataTable, StringBuilder> transformDataTableToCSV = (dataTable) =>
        {
            StringBuilder dataAsCSV = new StringBuilder();
            dataTable.Rows.Cast<DataRow>().ToList().ForEach(row =>
            {
                IEnumerable<string> fields = row.ItemArray.Select(field => field?.ToString().Replace("\"", "\"\""));
                dataAsCSV.AppendLine(string.Join("\t", fields));
            });
            return dataAsCSV;
        };

        static Func<DataTable, string> getColumnNamesForCSV = (HeaderRow) =>
        {
            return string.Join("\t", getHeader(HeaderRow));
        };

        static Func<IEnumerable<DataTable>, string, string> buildCSVDataFromList = (data, columns) =>
        {
            StringBuilder csvData = new StringBuilder();
            csvData.AppendLine(columns);
            data.Select(d => { return transformDataTableToCSV(d); }).ToList().ForEach(input =>
            {
                csvData.Append(input?.ToString());
            });
            return csvData?.ToString();
        };

        static Func<IEnumerable<DataTable>, string> buildCSVDataWithoutHeader = (data) =>
        {
            StringBuilder csvData = new StringBuilder();
            data.Select(d => { return transformDataTableToCSV(d); }).ToList().ForEach(input =>
            {
                csvData.Append(input?.ToString());
            });
            return csvData?.ToString();
        };

        static Func<DataTable, string, string> buildCSVDataWithHeader = (data, headerRow) =>
        {
            StringBuilder csvData = new StringBuilder();
            csvData.AppendLine(headerRow);
            return csvData.Append(transformDataTableToCSV(data)).ToString();
        };

        public override string GetContentType
        {
            get
            {
                return CONTENT_TYPE;
            }
        }

        //#endregion
        public override Task<T> Export<T>(IEnumerable<DataTable> data, Func<byte[], Task<T>> callback)
        {
                if (data == null || data.FirstOrDefault() == null)
                    throw new Exception("FailedToExportNoExportData");
                var dataAsString = buildCSVDataFromList(data, getColumnNamesForCSV(data.FirstOrDefault()));
                var response = getStreamFromString(dataAsString);
                return callback(response);

        }
        public Task<T> Export<T>(DataTable data, bool hasHeader, Func<byte[], Task<T>> callback)
        {
            
                if (data == null)
                    throw new Exception("FailedToExportNoExportData");
                string dataAsString;
                if (hasHeader)
                    dataAsString = buildCSVDataWithHeader(data, getColumnNamesForCSV(data));
                else
                    dataAsString = transformDataTableToCSV(data).ToString();
                var response = getStreamFromString(dataAsString);
                return callback(response);
        }

        public Task Export(IObservable<DataTable> reactiveStream, Action<byte[]> callback, Action<long> onComplete)
        {
           
                bool headerExists = false;//temporary workaround
                long dataLength = 0;
                IObserver<DataTable> dataObserver = Observer.Create<DataTable>((data) =>
                {
                    if (data == null)
                        throw new ArgumentException();
                    string dataAsString;
                    if (!headerExists)
                    {
                        dataAsString = buildCSVDataWithHeader(data, getColumnNamesForCSV(data));
                        headerExists = true;
                    }
                    else
                    {
                        dataAsString = transformDataTableToCSV(data).ToString();
                    }
                    var response = getStreamFromString(dataAsString);
                    callback(response);// callback to caller stream
                    dataLength += response.Length;
                }, (Exception ex) =>
                {
                   // CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for text format.");
                }
                , () =>
                {
                    //n/a action
                    onComplete(dataLength);
                });
                reactiveStream.Subscribe(dataObserver);
                return Task.FromResult(0);
        }

        public override Task<T> Export<T>(IEnumerable<Task<DataTable>> reactiveStream, Action<byte[]> callback, Action<long> onComplete)
        {
           
                bool headerExists = false;//temporary workaround
                long dataLength = 0;
                List<Task> tasksBag = new List<Task>();
                reactiveStream.ToList().ForEach(httpResponseTask =>
                {
                    tasksBag.Add(httpResponseTask);
                    if (httpResponseTask.IsFaulted)
                        throw httpResponseTask.Exception;
                    var data = httpResponseTask.Result;
                    if (data == null)
                        throw new ArgumentException();
                    string dataAsString;
                    if (!headerExists)
                    {
                        dataAsString = buildCSVDataWithHeader(data, getColumnNamesForCSV(data));
                        headerExists = true;
                    }
                    else
                    {
                        dataAsString = transformDataTableToCSV(data).ToString();
                    }
                    var response = getStreamFromString(dataAsString);
                    dataLength += response.Length;
                    callback(response);// callback to caller stream and get stream length of what was sent.
                });
                return Task.WhenAll(tasksBag).ContinueWith(taskCompletetion =>
                {
                    onComplete(dataLength);
                    return default(T);
                });
        }

        public override Task Export<T>(IObservable<T> reactiveStream, Action<byte[]> callback, Action<long> onComplete)
        {
                bool headerExists = false;//temporary workaround
                long dataLength = 0;
                var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
                IObserver<T> dataObserver = Observer.Create<T>((data) =>
                {
                    if (data == null)
                        throw new ArgumentException();
                    string dataAsString;
                    if (!headerExists)
                    {
                        dataAsString = buildCSVDataWithHeader(data, getColumnNamesForCSV(data));
                        headerExists = true;
                    }
                    else
                    {
                        dataAsString = transformDataTableToCSV(data).ToString();
                    }
                    var response = getStreamFromString(dataAsString);
                    callback(response);// callback to caller stream
                    dataLength += response.Length;
                }, (Exception ex) =>
                {
                    //CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for text format.");
                }
                , () =>
                {
                    onComplete(dataLength);
                    cancelToken.Cancel();
                });
                reactiveStream.Subscribe(dataObserver);
                return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);
        }
        public Task Export<T>(IObservable<T> reactiveStream, Stream writtingStream, Action<byte[]> callback, Action<long> onComplete) where T : DataTable
        {
                bool headerExists = false;//temporary workaround
                long dataLength = 0;
                var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
                IObserver<T> dataObserver = Observer.Create<T>((data) =>
                {
                    if (data == null)
                        throw new ArgumentException();
                    string dataAsString;
                    if (!headerExists)
                    {
                        dataAsString = buildCSVDataWithHeader(data, getColumnNamesForCSV(data));
                        headerExists = true;
                    }
                    else
                    {
                        dataAsString = transformDataTableToCSV(data).ToString();
                    }
                    var response = getStreamFromString(dataAsString);
                    writtingStream.Write(response, 0, response.Length);
                    writtingStream.Flush();
                    callback(null);// callback to caller stream
                    dataLength += response.Length;
                }, (Exception ex) =>
                {
                    //CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for text format.");
                }
                , () =>
                {
                    onComplete(dataLength);
                    cancelToken.Cancel();
                });
                reactiveStream.Subscribe(dataObserver);
                return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);
        }

        public override Task Export<T>(IObservable<T> reactiveStream, Stream writtingStream, Action callback, Action<long> onComplete)
        {
           
                bool headerExists = false;//temporary workaround
                long dataLength = 0;
                var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
                IObserver<T> dataObserver = null;
                dataObserver = Observer.Create<T>((data) =>
                  {
                      if (data == null)
                          throw new ArgumentException();
                      string dataAsString;
                      if (!headerExists)
                      {
                          dataAsString = buildCSVDataWithHeader(data.ReportDataTable, getColumnNamesForCSV(data.ReportDataTable));
                          headerExists = true;
                      }
                      else
                      {
                          dataAsString = transformDataTableToCSV(data.ReportDataTable).ToString();
                      }
                      var response = getStreamFromString(dataAsString);
                      writtingStream.Write(response, 0, response.Length);
                      writtingStream.Flush();
                      callback();// callback to caller stream
                      dataLength += response.Length;
                      if (data.IsLast)
                          dataObserver.OnCompleted();
                  }, (Exception ex) =>
                  {
                      //CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for text format.");
                  }
                , () =>
                {
                    onComplete(dataLength);
                    cancelToken.Cancel();
                });
                reactiveStream.Subscribe(Observer.Synchronize(dataObserver));
                return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);
        }

        public override Task Export<T>(Func<T> reactiveFunction, Stream writtingStream, Action callback, Action<long> onComplete)
        {
           
                bool headerExists = false;//temporary workaround
                long dataLength = 0;
                var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
                T inputData = null;
                do
                {
                    inputData = reactiveFunction();
                    if (inputData == null)
                        throw new ArgumentException();
                    string dataAsString;
                    if (!headerExists)
                    {
                        dataAsString = buildCSVDataWithHeader(inputData.ReportDataTable, getColumnNamesForCSV(inputData.ReportDataTable));
                        headerExists = true;
                    }
                    else
                    {
                        dataAsString = transformDataTableToCSV(inputData.ReportDataTable).ToString();
                    }
                    var response = getStreamFromString(dataAsString);
                    writtingStream.Write(response, 0, response.Length);
                    writtingStream.Flush();
                    callback();// callback to caller stream
                    dataLength += response.Length;
                } while (!inputData.IsLast);
                // on complete
                onComplete(dataLength);
                cancelToken.Cancel();
                return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);
        }
    }
}