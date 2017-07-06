using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reactive.Linq;
using System.Reactive;
using System.Net.Http;
using System.Threading;

namespace ExportAdapters
{
    public class PDFAdapter : ExporterAdaptee
    {
        private const string CONTENT_TYPE = "application/octet-stream";
        public override string GetContentType
        {
            get
            {
                return CONTENT_TYPE;
            }
        }

        public override DocumentStructure DocumentStructure
        {
            get;
            set;
        }

        static Func<DataRow, IEnumerable<PdfPCell>> getRowCells = (row) =>
        {
            return row.ItemArray.Select((r, idx) =>
            {
                return new PdfPCell(new Phrase(r?.ToString(), baseFont))
                { HorizontalAlignment = !string.IsNullOrEmpty(r?.ToString()) && isNumericColumn(row, idx) ? Element.ALIGN_RIGHT : Element.ALIGN_LEFT };
            });
        };

        static Func<DataRow, int, bool> isNumericColumn = (row, columnIdx) =>
        {
            return row.Table.Columns.Count > columnIdx && row.Table.Columns[columnIdx].IsNumeric();
        };

        static Func<IEnumerable<DataRow>, IEnumerable<PdfPRow>> getRows = (rows) =>
        {
            return rows.Select((r, idx) =>
            {
                return new PdfPRow(getRowCells(r).ToArray());
            });
        };

        static Func<IEnumerable<DataTable>, IEnumerable<PdfPRow>> getDataAsRows = (tables) =>
        {
            var outRows = new List<PdfPRow>();
            tables.ToList().ForEach(table =>
            {
                getRows(table.Rows.Cast<DataRow>()).ToList().ForEach(row =>
                {
                    outRows.Add(row);
                });
            });
            return outRows;
        };

        static Func<DataTable, IEnumerable<PdfPRow>> getDataTableAsRows = (table) =>
        {
            var outRows = new List<PdfPRow>();
            getRows(table.Rows.Cast<DataRow>()).ToList().ForEach(row =>
            {
                outRows.Add(row);
            });
            return outRows;
        };
        static Func<IEnumerable<string>, PdfPRow> getHeaderRow = (columns) =>
        {
            return new PdfPRow(columns.Select(column =>
                {
                    return new PdfPCell(new Phrase(ToTitleCase(column), tableHeaderFont));
                }).ToArray());
        };
        static Func<Document> createpdfDcoument = () => { return new Document(new Rectangle(PageSize.A4.Rotate()), 30, 30, 50, 55); }
        ;

        readonly static Font baseFont = FontFactory.GetFont(BaseFont.HELVETICA, 8f);
        readonly static Font tableHeaderFont = FontFactory.GetFont(BaseFont.HELVETICA_BOLD, 8f);

        public override Task<T> Export<T>(IEnumerable<DataTable> data, Func<byte[], Task<T>> callback)
        {
            Action<Document, string> addSummary = (doc, str) =>
            {
                if (string.IsNullOrEmpty(str))
                    return;
                var font = FontFactory.GetFont(BaseFont.HELVETICA, 9f, BaseColor.BLACK);
                var paragrapgh = new Paragraph(new Chunk($"{str.Trim()}\n", font));
                paragrapgh.Alignment = Element.ALIGN_LEFT;
                doc.Add(paragrapgh);
            };
                if (data == null || data.FirstOrDefault() == null)
                    throw new ArgumentException();
                byte[] outputStream;
                using (var memoryStream = new MemoryStream())
                {
                    using (var doc = createpdfDcoument())
                    {
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        var writer = PdfWriter.GetInstance(doc, memoryStream);
                        PageEventHelper pageEventHelper = new PageEventHelper(DocumentStructure);
                        writer.PageEvent = pageEventHelper;
                        doc.Open();
                        DocumentStructure.Criteria.ToList().ForEach(contnet => addSummary(doc, contnet));
                        doc.Add(new Paragraph("\n"));
                        int columnsCount = data.FirstOrDefault().Columns.Count;
                        var pdfTable = new PdfPTable(columnsCount);
                        pdfTable.WidthPercentage = 100;
                        var tableHeader = getHeaderRow(DocumentStructure.Headers);
                        pdfTable.Rows.Add(tableHeader);
                        getDataAsRows(data).ToList().ForEach(row =>
                        {
                            pdfTable.Rows.Add(row);
                        });
                        doc.Add(pdfTable);
                        doc.Add(new Paragraph("\n"));
                        doc.Close();
                        writer.Close();
                    }
                    outputStream = memoryStream.ToArray();
                }
                return callback(outputStream);
        }

        public override Task Export<T>(IObservable<T> reactiveStream, Action<byte[]> callback, Action<long> onComplete)
        {
            Action<Document, string> addSummary = (doc, str) =>
            {
                if (string.IsNullOrEmpty(str))
                    return;
                var font = FontFactory.GetFont(BaseFont.HELVETICA, 9f, BaseColor.BLACK);
                var paragrapgh = new Paragraph(new Chunk($"{str.Trim()}\n", font));
                paragrapgh.Alignment = Element.ALIGN_LEFT;
                doc.Add(paragrapgh);
            };
           
                var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
                using (var memoryStream = new MemoryStream())
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    using (var doc = createpdfDcoument())
                    {
                        var writer = PdfWriter.GetInstance(doc, memoryStream);
                        PageEventHelper pageEventHelper = new PageEventHelper(DocumentStructure);
                        writer.PageEvent = pageEventHelper;
                        doc.Open();
                        PdfPTable pdfTable = null;
                        bool headerExists = false;//temporary workaround
                        long dataLength = 0;
                        IObserver<T> dataObserver = Observer.Create<T>((data) =>
                        {
                            if (data == null)
                                throw new ArgumentException();
                            var header = getHeader(data);
                            if (!headerExists)
                            {
                                DocumentStructure.Criteria.ToList().ForEach(contnet => addSummary(doc, contnet));
                                doc.Add(new Paragraph("\n"));
                                pdfTable = new PdfPTable(header.Count());
                                pdfTable.WidthPercentage = 100;
                                var tableHeader = getHeaderRow(header);
                                pdfTable.Rows.Add(tableHeader);
                                headerExists = true;
                            }
                            else
                            {
                                pdfTable = new PdfPTable(header.Count());
                                pdfTable.WidthPercentage = 100;
                            }
                            getDataTableAsRows(data).ToList().ForEach(row =>
                            {
                                pdfTable.Rows.Add(row);
                            });
                            doc.Add(pdfTable);
                            writer.Flush();
                            callback(memoryStream.ToArray());
                            dataLength += memoryStream.Length;
                            memoryStream.Flush();
                            memoryStream.Seek(0, SeekOrigin.Begin);
                        }, (Exception ex) =>
                    {
                        //CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for pdf format.");
                    }
                    , () =>
                    {
                        doc.Add(new Paragraph("\n"));
                        doc.Close();
                        writer.Close();
                        callback(memoryStream.ToArray());
                        memoryStream.Flush();
                        //
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
            Action<Document, string> addSummary = (doc, str) =>
            {
                if (string.IsNullOrEmpty(str))
                    return;
                var font = FontFactory.GetFont(BaseFont.HELVETICA, 9f, BaseColor.BLACK);
                var paragrapgh = new Paragraph(new Chunk($"{str.Trim()}\n", font));
                paragrapgh.Alignment = Element.ALIGN_LEFT;
                doc.Add(paragrapgh);
            };

                var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
                using (var doc = createpdfDcoument())
                {
                    var writer = PdfWriter.GetInstance(doc, writtingStream);
                    PageEventHelper pageEventHelper = new PageEventHelper(DocumentStructure);
                    writer.PageEvent = pageEventHelper;
                    doc.Open();
                    PdfPTable pdfTable = null;
                    bool headerExists = false;//temporary workaround
                    long dataLength = 0;
                    IObserver<T> dataObserver = Observer.Create<T>((data) =>
                    {
                        if (data == null)
                            throw new ArgumentException();
                        var header = getHeader(data);
                        if (!headerExists)
                        {
                            DocumentStructure.Criteria.ToList().ForEach(contnet => addSummary(doc, contnet));
                            doc.Add(new Paragraph("\n"));
                            pdfTable = new PdfPTable(header.Count());
                            pdfTable.WidthPercentage = 100;
                            var tableHeader = getHeaderRow(header);
                            pdfTable.Rows.Add(tableHeader);
                            headerExists = true;
                        }
                        else
                        {
                            pdfTable = new PdfPTable(header.Count());
                            pdfTable.WidthPercentage = 100;
                        }
                        getDataTableAsRows(data).ToList().ForEach(row =>
                        {
                            pdfTable.Rows.Add(row);
                        });
                        doc.Add(pdfTable);
                        writer.Flush();
                        callback(null);
                        writtingStream.Flush();
                    }, (Exception ex) =>
                    {
                      //  CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for pdf format.");
                    }
                , () =>
                {
                    doc.Close();
                    writer.Close();
                    callback(null);
                    writtingStream.Flush();
                    //
                    onComplete(dataLength);
                    cancelToken.Cancel();
                });
                    reactiveStream.Subscribe(dataObserver);
                }
                return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);
          
        }

        public override Task Export<T>(IObservable<T> reactiveStream, Stream writtingStream, Action callback, Action<long> onComplete)
        {
            Action<Document, string> addSummary = (doc, str) =>
            {
                if (string.IsNullOrEmpty(str))
                    return;
                var font = FontFactory.GetFont(BaseFont.HELVETICA, 9f, BaseColor.BLACK);
                var paragrapgh = new Paragraph(new Chunk($"{str.Trim()}\n", font));
                paragrapgh.Alignment = Element.ALIGN_LEFT;
                doc.Add(paragrapgh);
            };

            var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
            using (var doc = createpdfDcoument())
            {
                var writer = PdfWriter.GetInstance(doc, writtingStream);
                PageEventHelper pageEventHelper = new PageEventHelper(DocumentStructure);
                writer.PageEvent = pageEventHelper;
                doc.Open();
                PdfPTable pdfTable = null;
                bool headerExists = false;//temporary workaround
                long dataLength = 0;
                IObserver<T> dataObserver = null;
                dataObserver = Observer.Create<T>((data) =>
                 {
                     if (data == null)
                         throw new ArgumentException();
                     var header = getHeader(data.ReportDataTable);
                     if (!headerExists)
                     {
                         DocumentStructure.Criteria.ToList().ForEach(contnet => addSummary(doc, contnet));
                         doc.Add(new Paragraph("\n"));
                         pdfTable = new PdfPTable(header.Count());
                         pdfTable.WidthPercentage = 100;
                         var tableHeader = getHeaderRow(header);
                         pdfTable.Rows.Add(tableHeader);
                         headerExists = true;
                     }
                     else
                     {
                         pdfTable = new PdfPTable(header.Count());
                         pdfTable.WidthPercentage = 100;
                     }
                     getDataTableAsRows(data.ReportDataTable).ToList().ForEach(row =>
                     {
                         pdfTable.Rows.Add(row);
                     });
                     doc.Add(pdfTable);
                     writer.Flush();
                     callback();
                     writtingStream.Flush();
                     if (data.IsLast)
                         dataObserver.OnCompleted();
                 }, (Exception ex) =>
                 {
                         //CommonItems.Logger?.ErrorException(ex, "Error occurred while exporting data for pdf format.");
                     }
            , () =>
            {
                doc.Close();
                writer.Close();
                callback();
                writtingStream.Flush();
                    //
                    onComplete(dataLength);
                cancelToken.Cancel();
            });
                reactiveStream.Subscribe(Observer.Synchronize(dataObserver));
            }
            return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);

        }

        public override Task Export<T>(Func<T> reactiveFunction, Stream writtingStream, Action callback, Action<long> onComplete)
        {
            Action<Document, string> addSummary = (doc, str) =>
            {
                if (string.IsNullOrEmpty(str))
                    return;
                var font = FontFactory.GetFont(BaseFont.HELVETICA, 9f, BaseColor.BLACK);
                var paragrapgh = new Paragraph(new Chunk($"{str.Trim()}\n", font));
                paragrapgh.Alignment = Element.ALIGN_LEFT;
                doc.Add(paragrapgh);
            };

                var cancelToken = new CancellationTokenSource(TimeSpan.FromHours(EXPORT_TIMEOUT_HR));
                using (var doc = createpdfDcoument())
                {
                    var writer = PdfWriter.GetInstance(doc, writtingStream);
                    PageEventHelper pageEventHelper = new PageEventHelper(DocumentStructure);
                    writer.PageEvent = pageEventHelper;
                    doc.Open();
                    PdfPTable pdfTable = null;
                    bool headerExists = false;//temporary workaround
                    long dataLength = 0;
                    T inputData = null;
                    do
                    {
                        inputData = reactiveFunction();
                        if (inputData == null)
                            throw new ArgumentException();
                        var header = getHeader(inputData.ReportDataTable);
                        if (!headerExists)
                        {
                            DocumentStructure.Criteria.ToList().ForEach(contnet => addSummary(doc, contnet));
                            doc.Add(new Paragraph("\n"));
                            pdfTable = new PdfPTable(header.Count());
                            pdfTable.WidthPercentage = 100;
                            var tableHeader = getHeaderRow(header);
                            pdfTable.Rows.Add(tableHeader);
                            headerExists = true;
                        }
                        else
                        {
                            pdfTable = new PdfPTable(header.Count());
                            pdfTable.WidthPercentage = 100;
                        }
                        getDataTableAsRows(inputData.ReportDataTable).ToList().ForEach(row =>
                        {
                            pdfTable.Rows.Add(row);
                        });
                        doc.Add(pdfTable);
                        writer.Flush();
                        callback();
                        writtingStream.Flush();

                    } while (!inputData.IsLast);
                    //on complete
                    doc.Close();
                    writer.Close();
                    callback();
                    writtingStream.Flush();
                    onComplete(dataLength);
                    cancelToken.Cancel();
                }
                return Task.Delay(TimeSpan.FromHours(EXPORT_TIMEOUT_HR), cancelToken.Token);
            
        }

        public override Task<T> Export<T>(IEnumerable<Task<DataTable>> reactiveStream, Action<byte[]> callback, Action<long> onComplete)
        {
            throw new NotImplementedException();
        }

        public class PageEventHelper : PdfPageEventHelper
        {
            public PageEventHelper(DocumentStructure documentStructure)
            {
                this.documentStructure = documentStructure;
                footerFont = FontFactory.GetFont(BaseFont.HELVETICA, 8f);
                headerFont = FontFactory.GetFont(BaseFont.HELVETICA_BOLD, 9f, BaseColor.BLACK);
            }
            DocumentStructure documentStructure;
            PdfContentByte cb;
            PdfTemplate template;
            Font headerFont, footerFont;
            const int HEADER_CELLS = 3;
            public override void OnOpenDocument(PdfWriter writer, Document document)
            {
                cb = writer.DirectContent;
                template = cb.CreateTemplate(50, 50);
            }
            public override void OnStartPage(PdfWriter writer, Document document)
            {
                document.Add(new Paragraph("\n"));
                base.OnStartPage(writer, document);
                cb.SetRGBColorFill(100, 100, 100);
                PdfPTable headerTable = new PdfPTable(HEADER_CELLS);
                headerTable.TotalWidth = document.Right - document.Left;
                headerTable.WidthPercentage = 100;
                PdfPCell leftCell = new PdfPCell(new Phrase(new Chunk(documentStructure.BusinessName, headerFont)));
                leftCell.HorizontalAlignment = Element.ALIGN_LEFT;
                leftCell.VerticalAlignment = Element.ALIGN_TOP;
                leftCell.Border = 0;

                PdfPCell centerCell = new PdfPCell(new Phrase(new Chunk(documentStructure.ReportName, headerFont)));
                centerCell.HorizontalAlignment = Element.ALIGN_CENTER;
                centerCell.VerticalAlignment = Element.ALIGN_TOP;
                centerCell.Border = 0;

                PdfPCell rightCell = new PdfPCell(new Phrase(new Chunk(documentStructure.username, headerFont)));
                rightCell.HorizontalAlignment = Element.ALIGN_RIGHT;
                rightCell.VerticalAlignment = Element.ALIGN_TOP;
                rightCell.Border = 0;

                new List<PdfPCell> { leftCell, centerCell, rightCell }.ForEach(cell => headerTable.AddCell(cell));
                headerTable.WriteSelectedRows(0, 1, document.LeftMargin, document.PageSize.GetTop(document.TopMargin), writer.DirectContent);
            }
            public override void OnEndPage(PdfWriter writer, Document document)
            {
                base.OnEndPage(writer, document);
                cb.SetRGBColorFill(100, 100, 100);

                PdfPTable headerTable = new PdfPTable(HEADER_CELLS);
                headerTable.TotalWidth = document.Right - document.Left;
                headerTable.WidthPercentage = 100;
                PdfPCell leftCell = new PdfPCell(new Phrase(new Chunk(string.Empty)));
                leftCell.HorizontalAlignment = Element.ALIGN_LEFT;
                leftCell.VerticalAlignment = Element.ALIGN_TOP;
                leftCell.Border = 0;

                PdfPCell centerCell = new PdfPCell(new Phrase(new Chunk(documentStructure.CopyRight, footerFont)));
                centerCell.HorizontalAlignment = Element.ALIGN_CENTER;
                centerCell.VerticalAlignment = Element.ALIGN_TOP;
                centerCell.Border = 0;

                PdfPCell rightCell = new PdfPCell(new Phrase(new Chunk(documentStructure.GeneratedAt, footerFont)));
                rightCell.HorizontalAlignment = Element.ALIGN_RIGHT;
                rightCell.VerticalAlignment = Element.ALIGN_TOP;
                rightCell.Border = 0;

                new List<PdfPCell> { leftCell, centerCell, rightCell }.ForEach(cell => headerTable.AddCell(cell));
                headerTable.WriteSelectedRows(0, 1, document.LeftMargin, document.PageSize.GetBottom(document.BottomMargin), writer.DirectContent);

                Rectangle pageSize = document.PageSize;
                var pageN = writer.PageNumber;
                var text = $"Page {pageN} of ";
                var len = footerFont.BaseFont.GetWidthPoint(text, footerFont.Size);

                const int adjustPosition = 7;
                cb.BeginText();
                cb.SetFontAndSize(footerFont.BaseFont, footerFont.Size);
                cb.SetTextMatrix(document.RightMargin, document.PageSize.GetBottom(document.BottomMargin) - adjustPosition);
                cb.ShowText(text);

                cb.EndText();

                cb.AddTemplate(template, (document.RightMargin + len), document.PageSize.GetBottom(document.BottomMargin) - adjustPosition);
            }

            public override void OnCloseDocument(PdfWriter writer, Document document)
            {
                base.OnCloseDocument(writer, document);
                template.BeginText();
                template.SetFontAndSize(footerFont.BaseFont, footerFont.Size);
                template.SetTextMatrix(0, 0);
                template.ShowText($"{(writer.PageNumber)}");
                template.EndText();
            }
        }
    }
}