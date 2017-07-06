using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;

namespace ExportAdapters
{
    public class ExcelAdapter : ExporterAdaptee
    {
        private const string CONTENT_TYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public override string GetContentType
        {
            get
            {
                return CONTENT_TYPE;
            }
        }

        static Func<IEnumerable<string>, Row> getHeaderRow = (columns) =>
        {
            var row = new Row();
            var cells = columns.Select(column =>
            {
                return new Cell
                {
                    DataType = CellValues.String,
                    CellValue = new CellValue(column)
                };
            });
            row.Append(cells);
            return row;
        };

        static Func<object, bool, Cell> createCell = (data, IsNumeric) =>
        {
            const string sanitizationPattern = "^[=+-]+";
            string strValue = data?.ToString();
            if (data != null)
            {
                var regex = new Regex(sanitizationPattern);
                if (regex.IsMatch(data.ToString()))
                    strValue = $"\r{data}";
            }
            return new Cell
            {
                DataType = IsNumeric ? CellValues.Number : CellValues.String,
                CellValue = new CellValue(strValue)
            };
        };

        static Func<object[], DataColumnCollection, IEnumerable<Cell>> getRowCells = (rowItems, columns) =>
        {
            return rowItems.Select((r, idx) =>
            {
                return createCell(r, !string.IsNullOrEmpty(r?.ToString()) && columns[idx].IsNumeric());
            });
        };

        static Func<IEnumerable<DataRow>, IEnumerable<Row>> getRows = (rows) =>
        {
            return rows.Select(r =>
            {
                return new Row(getRowCells(r?.ItemArray, r.Table.Columns));
            });
        };

        static Func<DataTable, IEnumerable<Row>> getTableRows = (table) =>
        {
            IList<Row> outRows = new List<Row>();
            getRows(table.Rows.Cast<DataRow>()).ToList().ForEach(row =>
            {
                outRows.Add(row);
            });
            return outRows;
        };

        static Func<IEnumerable<DataTable>, SheetData, SheetData> buildSheet = (tables, sheet) =>
        {
            tables.ToList().ForEach(table =>
            {
                sheet.Append(getTableRows(table));
            });
            return sheet;
        };


        public override Task<T> Export<T>(IEnumerable<DataTable> data, Func<byte[], Task<T>> callback)
        {
            if (data == null || data.FirstOrDefault() == null)
                throw new ArgumentException();
            const int SHEET_ID = 1;
            byte[] outputStream;
            using (var memoryStream = new MemoryStream())
            {
                using (var spreadsheetDocument = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook, true))
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    WorkbookPart workbookpart = spreadsheetDocument.AddWorkbookPart();
                    workbookpart.Workbook = new Workbook();
                    var worksheetPart = workbookpart.AddNewPart<WorksheetPart>();
                    var sheetData = new SheetData();
                    worksheetPart.Worksheet = new Worksheet(sheetData);
                    Sheets sheets;
                    sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());
                    var sheet = new Sheet()
                    {
                        Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart),
                        SheetId = SHEET_ID,
                        Name = $"Sheet {SHEET_ID}"
                    };
                    sheets.Append(sheet);
                    var headerRow = getHeaderRow(getHeader(data.FirstOrDefault()));
                    sheetData.AppendChild(headerRow);
                    data.ToList().ForEach(table =>
                    {
                        sheetData.Append(getTableRows(table));
                    });
                    workbookpart.Workbook.Save();
                    spreadsheetDocument.Close();
                }
                outputStream = memoryStream.ToArray();
            }
            return callback(outputStream);

        }

        public override Task Export<T>(IObservable<T> reactiveStream, Action<byte[]> callback, Action<long> onComplete)
        {

            const int SHEET_ID = 1;
            var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
            using (var memoryStream = new MemoryStream())
            {
                using (var spreadsheetDocument = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook, true))
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    WorkbookPart workbookpart = spreadsheetDocument.AddWorkbookPart();
                    workbookpart.Workbook = new Workbook();
                    var worksheetPart = workbookpart.AddNewPart<WorksheetPart>();
                    var sheetData = new SheetData();
                    worksheetPart.Worksheet = new Worksheet(sheetData);
                    Sheets sheets;
                    sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());
                    var sheet = new Sheet()
                    {
                        Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart),
                        SheetId = SHEET_ID,
                        Name = $"Sheet {SHEET_ID}"
                    };
                    sheets.Append(sheet);
                    bool headerExists = false;//temporary workaround
                    long dataLength = 0;
                    IObserver<T> dataObserver = Observer.Create<T>((data) =>
                    {
                        if (data == null)
                            throw new ArgumentException();
                        if (!headerExists)
                        {
                            var header = getHeader(data);
                            var headerRow = getHeaderRow(header);
                            sheetData.AppendChild(headerRow);
                            headerExists = true;
                        }
                        sheetData.Append(getTableRows(data));
                        callback(memoryStream.ToArray());// callback to caller stream
                        dataLength += memoryStream.Length;
                    }, (Exception ex) =>
                    {
                        // CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for xlsx format.");
                    }
                    , () =>
                    {
                        workbookpart.Workbook.Save();
                        spreadsheetDocument.Close();
                        callback(memoryStream.ToArray());
                        // on complete ....
                        onComplete(dataLength);
                        cancelToken.Cancel();
                    });
                    reactiveStream.Subscribe(dataObserver);
                }
            }
            return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);

        }

        public Task Export<T>(IObservable<T> reactiveStream, Stream writtingStream, Action<byte[]> callback, Action<long> onComplete) where T : DataTable
        {
            const int SHEET_ID = 1;
            var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
            using (var memoryStream = new MemoryStream())
            {
                using (var spreadsheetDocument = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook, true))
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    WorkbookPart workbookpart = spreadsheetDocument.AddWorkbookPart();
                    workbookpart.Workbook = new Workbook();
                    var worksheetPart = workbookpart.AddNewPart<WorksheetPart>();
                    var sheetData = new SheetData();
                    worksheetPart.Worksheet = new Worksheet(sheetData);
                    Sheets sheets;
                    sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());
                    var sheet = new Sheet()
                    {
                        Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart),
                        SheetId = SHEET_ID,
                        Name = $"Sheet {SHEET_ID}"
                    };
                    sheets.Append(sheet);
                    bool headerExists = false;//temporary workaround
                    long dataLength = 0;
                    IObserver<T> dataObserver = Observer.Create<T>((data) =>
                    {
                        if (data == null)
                            throw new ArgumentException();
                        if (!headerExists)
                        {
                            var header = getHeader(data);
                            var headerRow = getHeaderRow(header);
                            sheetData.AppendChild(headerRow);
                            headerExists = true;
                        }
                        sheetData.Append(getTableRows(data));
                        writtingStream.Write(memoryStream.ToArray(), 0, (int)memoryStream.Length);
                        writtingStream.Flush();
                        callback(null);// callback to caller stream
                        dataLength += memoryStream.Length;
                        memoryStream.Flush();
                        memoryStream.Seek(0, SeekOrigin.Begin);
                    }, (Exception ex) =>
                    {
                        //  CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for xlsx format.");
                    }
                    , () =>
                    {
                        workbookpart.Workbook.Save();
                        spreadsheetDocument.Close();
                        writtingStream.Write(memoryStream.ToArray(), 0, (int)memoryStream.Length);
                        writtingStream.Flush();
                        callback(null);
                        dataLength += memoryStream.Length;
                        memoryStream.Flush();
                        onComplete(dataLength);
                        cancelToken.Cancel();
                    });
                    reactiveStream.Subscribe(dataObserver);
                }
            }
            return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);

        }

        public override Task Export<T>(IObservable<T> reactiveStream, Stream writtingStream, Action callback, Action<long> onComplete)
        {
            const int SHEET_ID = 1;
            var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
            using (var memoryStream = new MemoryStream())
            {
                using (var spreadsheetDocument = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook, true))
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    WorkbookPart workbookpart = spreadsheetDocument.AddWorkbookPart();
                    workbookpart.Workbook = new Workbook();
                    var worksheetPart = workbookpart.AddNewPart<WorksheetPart>();
                    var sheetData = new SheetData();
                    worksheetPart.Worksheet = new Worksheet(sheetData);
                    Sheets sheets;
                    sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());
                    var sheet = new Sheet()
                    {
                        Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart),
                        SheetId = SHEET_ID,
                        Name = $"Sheet {SHEET_ID}"
                    };
                    sheets.Append(sheet);
                    bool headerExists = false;//temporary workaround
                    long dataLength = 0;
                    IObserver<T> dataObserver = null;
                    dataObserver = Observer.Create<T>((data) =>
                    {
                        if (data == null)
                            throw new ArgumentException();
                        if (!headerExists)
                        {
                            var header = getHeader(data.ReportDataTable);
                            var headerRow = getHeaderRow(header);
                            sheetData.AppendChild(headerRow);
                            headerExists = true;
                        }
                        sheetData.Append(getTableRows(data.ReportDataTable));
                        writtingStream.Write(memoryStream.ToArray(), 0, (int)memoryStream.Length);
                        writtingStream.Flush();
                        callback();// callback to caller stream
                        dataLength += memoryStream.Length;
                        memoryStream.Flush();
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        if (data.IsLast)
                            dataObserver.OnCompleted();
                    }, (Exception ex) =>
                    {
                        // CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for xlsx format.");
                    }
                    , () =>
                    {
                        workbookpart.Workbook.Save();
                        spreadsheetDocument.Close();
                        writtingStream.Write(memoryStream.ToArray(), 0, (int)memoryStream.Length);
                        writtingStream.Flush();
                        callback();
                        dataLength += memoryStream.Length;
                        memoryStream.Flush();
                        onComplete(dataLength);
                        cancelToken.Cancel();
                    });
                    reactiveStream.Subscribe(Observer.Synchronize(dataObserver));
                }
            }
            return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);

        }

        public override Task Export<T>(Func<T> reactiveFunction, Stream writtingStream, Action callback, Action<long> onComplete)
        {

            const int SHEET_ID = 1;
            var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
            using (var memoryStream = new MemoryStream())
            {
                using (var spreadsheetDocument = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook, true))
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    WorkbookPart workbookpart = spreadsheetDocument.AddWorkbookPart();
                    workbookpart.Workbook = new Workbook();
                    var worksheetPart = workbookpart.AddNewPart<WorksheetPart>();
                    var sheetData = new SheetData();
                    worksheetPart.Worksheet = new Worksheet(sheetData);
                    Sheets sheets;
                    sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());
                    var sheet = new Sheet()
                    {
                        Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart),
                        SheetId = SHEET_ID,
                        Name = $"Sheet {SHEET_ID}"
                    };
                    sheets.Append(sheet);
                    bool headerExists = false;//temporary workaround
                    long dataLength = 0;
                    T inputData = null;
                    do
                    {
                        inputData = reactiveFunction();
                        if (inputData == null)
                            throw new ArgumentException();
                        if (!headerExists)
                        {
                            var header = getHeader(inputData.ReportDataTable);
                            var headerRow = getHeaderRow(header);
                            sheetData.AppendChild(headerRow);
                            headerExists = true;
                        }
                        sheetData.Append(getTableRows(inputData.ReportDataTable));
                        writtingStream.Write(memoryStream.ToArray(), 0, (int)memoryStream.Length);
                        writtingStream.Flush();
                        callback();// callback to caller stream
                        dataLength += memoryStream.Length;
                        memoryStream.Flush();
                        memoryStream.Seek(0, SeekOrigin.Begin);
                    } while (!inputData.IsLast);
                    // on complete
                    workbookpart.Workbook.Save();
                    spreadsheetDocument.Close();
                    writtingStream.Write(memoryStream.ToArray(), 0, (int)memoryStream.Length);
                    writtingStream.Flush();
                    callback();
                    dataLength += memoryStream.Length;
                    memoryStream.Flush();
                    onComplete(dataLength);
                    cancelToken.Cancel();
                }
            }
            return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);

        }

        public override Task<T> Export<T>(IEnumerable<Task<DataTable>> reactiveStream, Action<byte[]> callback, Action<long> onComplete)
        {
            throw new NotImplementedException();
        }

    }
    public static class DataColumnExtension
    {
        public static bool IsNumeric(this DataColumn col)
        {
            if (col == null)
                return false;
            var numericTypes = new[] { typeof(Byte), typeof(Decimal), typeof(Double),
                                       typeof(Int16), typeof(Int32), typeof(Int64), typeof(SByte),
                                       typeof(Single), typeof(UInt16), typeof(UInt32), typeof(UInt64)};
            return numericTypes.Contains(col.DataType);
        }
    }
}