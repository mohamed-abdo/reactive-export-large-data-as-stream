using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using reactive_download.Helper;
using System.Net;
using System.Web.Mvc;
using ExportAdapters;
using System.Threading;
using System.Data;
using System.Web;
using Microsoft.AspNet.SignalR;
using System.Net.Http;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Web.Http;
using System.IO;
using reactive_download.Models;
namespace reactive_download.Controllers
{
    /// <summary>
    /// Developed By: Mohamed Abdo, Mohamad.abdo@gmail.com
    /// Modified  At: 2017-07-05
    /// </summary>
    public class DownloadController : Controller
    {
        const int MAX_RECORDS_TO_DOWNLOAD = int.MaxValue;// max signed int value
        const int START_INDEX_FROM = 0;
        const int WORKER_THREADS = 5;
        const int TIMEOUT_IN_HOURS = 3;
        const int NOTIFY_INTERVAL_IN_MS = 5000;
        readonly APIEndpoints _apiEndpoints = new APIEndpoints()
        {
            BaseUrl = "http://localhost:8008/api/",
            Data = "dataStream/dataTable",
            Statistics = "dataStream/summary"
        };
        public event EventHandler<ReportDataReadyEventArgs> OnReportDataReady;
        public event EventHandler<ReportResponseReadyEventArgs> OnReportResponseReady;
        private EventWaitHandle fetcheDataWorkerEvent, exportCompletedEvent, reactiveDataReadyEvent;
        struct initialDataInfo
        {
            public int startInfo;
            public int pageSize;
        }
        struct stagingDataInfo
        {
            public int startInfo;
            public int pageSize;
            public DataTable data;
        }

        // <summary>
        /// Ajax handler to fetch data for download.
        /// </summary>
        /// <param name="model">The model for download criteria</param>
        /// <returns>DataTables structure (Json)</returns>
        /// <remarks>
        /// Created By: Mohamed Abdo
        ///       Date: 18 Jan 2017
        /// </remarks>
        [HandleError]
        [System.Web.Http.HttpGet]
        public Task ReactiveDownloadReport([FromUri]string reportName, [FromUri] int recordsToDownload, [FromUri] string fileName, [FromUri] string target)
        {
            OnReportResponseReady += (object sender, ReportResponseReadyEventArgs args) =>
            {
                // future use for selective fetching data instead of scan fetch data.
            };
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromHours(TIMEOUT_IN_HOURS));
            //initiate events state to off.
            fetcheDataWorkerEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            exportCompletedEvent = new EventWaitHandle(false, EventResetMode.AutoReset);

            Mutex mutexLocker = new Mutex();
            List<initialDataInfo> initialDataCollection = new List<initialDataInfo>();
            List<stagingDataInfo> stagingDataCollection = new List<stagingDataInfo>();
            var user = HttpContext.GetOwinContext().Authentication.User;
            var connectionId = Utilities.GetHashToken(user);
            Action<object> notifyClientDownloadOnProgress = (args) =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient(connectionId);
            };
            Action notifyClientDownloadOnSuccess = () =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient_on_file_download_successfully(connectionId, fileName);
            };
            Action notifyClientDownloadOnStart = () =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient_on_file_download_start(connectionId, fileName);
            };
            Action<string> notifyClientDownloadOnFailed = (errorMessage) =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient_on_file_download_failed(connectionId, errorMessage);
            };
            if (string.IsNullOrEmpty(reportName) || recordsToDownload < 1)
            {
                var uiEx = new UiException("FailedToExportNoExportData");
                notifyClientDownloadOnFailed(uiEx.Message);
                return Task.FromResult<ActionResult>(Json(uiEx.Message, JsonRequestBehavior.AllowGet));
            }
            var report = new Report() { TotalRecords = recordsToDownload, ReportName = reportName };
            if (report.TotalRecords > MAX_RECORDS_TO_DOWNLOAD)
            {
                var uiEx = new UiException("FailedToExportTooManyRecords: " + MAX_RECORDS_TO_DOWNLOAD);
                notifyClientDownloadOnFailed(uiEx.Message);
                return Task.FromResult<ActionResult>(new EmptyResult());
            }
            ExporterAdaptee adapter = AdapterFactory.Create(target);
            Action prepareResponse = () =>
            {
                HttpContext.Response.Clear();
                HttpContext.Response.ContentType = adapter.GetContentType;
                HttpContext.Response.Buffer = false;
                HttpContext.Response.BufferOutput = false;
                HttpContext.Response.AddHeader("content-disposition", $"attachment; filename={fileName}");
                HttpContext.Response.AddHeader("content-type", adapter.GetContentType);
            };
            Func<string, string, IEnumerable<string>> ToBullet = (content, separator) =>
            {
                return content.Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries).Select(s =>
                         $"\u2022 {s?.Trim()}");
            };
            Func<DataTable, IEnumerable<string>> getHeader = (tableStructure) =>
            {
                return tableStructure.Columns.Cast<DataColumn>().Select(column => column.ColumnName);
            };
            Action<Exception> onFailed = (ex) =>
            {
                //CommonItems.Logger?.ErrorException(ex, "Error occurred while getting data for download report.");
                notifyClientDownloadOnFailed(ex.Message);
            };
            int? lastPayLoad = null;
            double? lastResponseTime = null;
            Func<int, initialDataInfo> getNextDownloadPacketInfo = (totalRecords) =>
            {
                mutexLocker.WaitOne();
                var nextPayLoad = Helper.ReportEngine.getDownloadPageSize(totalRecords, lastPayLoad, lastResponseTime);
                var maxStartFrom = START_INDEX_FROM;
                if (initialDataCollection.Count > 0)
                    maxStartFrom = initialDataCollection.Last().startInfo + initialDataCollection.Last().pageSize;
                var nextDownloadInfo = new initialDataInfo() { startInfo = maxStartFrom, pageSize = nextPayLoad };
                mutexLocker.ReleaseMutex();
                return nextDownloadInfo;
            };
            Action<stagingDataInfo> setDownloadPacket = (downloadInfo) =>
            {
                mutexLocker.WaitOne();
                stagingDataCollection.Add(downloadInfo);
                mutexLocker.ReleaseMutex();
            };
            Action<initialDataInfo> setInitialDownloadPacket = (downloadInfo) =>
            {
                mutexLocker.WaitOne();
                initialDataCollection.Add(downloadInfo);
                mutexLocker.ReleaseMutex();
            };
            Func<int, bool> IsResponseCompleted = (totalRecors) =>
            {
                mutexLocker.WaitOne();
                var maxStartInfo = initialDataCollection.Max(r => r.startInfo);
                var maxStartInfoPageSize = initialDataCollection.First(s => s.startInfo == maxStartInfo).pageSize;
                var result = (maxStartInfo + maxStartInfoPageSize) >= totalRecors;
                mutexLocker.ReleaseMutex();
                return (result || maxStartInfoPageSize >= totalRecors);// data ready on single packet case or waiting for last exporter callback
            };
            int nextStartFromStaging = 1;
            Action<int> fillReadyQueueWorker = (totalRecords) =>
            {
                //CommonItems.Logger.Trace($"fillReadyQueueWorker => Starting thread {Thread.CurrentThread.ManagedThreadId}, start processing signal ...");
                for (int i = 0; i < stagingDataCollection.Count; i++)
                {
                    var targetedPacketIdx = stagingDataCollection.FindIndex(s => s.startInfo == nextStartFromStaging);
                    if (targetedPacketIdx != -1)
                    {
                        mutexLocker.WaitOne();
                        nextStartFromStaging = stagingDataCollection[targetedPacketIdx].startInfo + stagingDataCollection[targetedPacketIdx].pageSize;
                        var isLast = (stagingDataCollection.Count == 1 && IsResponseCompleted(totalRecords));
                        OnReportDataReady(this, new ReportDataReadyEventArgs(stagingDataCollection[targetedPacketIdx].data, isLast));
                        stagingDataCollection.RemoveAt(targetedPacketIdx);
                        mutexLocker.ReleaseMutex();
                    }
                }
            };
            Action<HttpClient, Helper.ReportEngine> getDataWorker = (httpClient, helper) =>
            {
                if (!helper.getCurrentModelTotalRecords.HasValue && helper.getCurrentModelTotalRecords.Value > 0)
                    throw new ArgumentNullException("Report_MsgError_FailedToExportNoExportData");
                fetcheDataWorkerEvent.WaitOne();
                //CommonItems.Logger.Trace($"getDataWorker => Starting fetching data {Thread.CurrentThread.Name}, event signal fired...");
                var totalRecords = helper.getCurrentModelTotalRecords.Value;
                var nextDownloadInfo = getNextDownloadPacketInfo(totalRecords);
                while (nextDownloadInfo.startInfo < totalRecords)
                {
                    setInitialDownloadPacket(nextDownloadInfo);
                    var startExecutionAt = Stopwatch.StartNew();
                    var downloadTask = helper.GetDownloadDataAsync(httpClient, Request, nextDownloadInfo.startInfo, nextDownloadInfo.pageSize, dataPacket =>
                    {
                        startExecutionAt.Stop();
                        mutexLocker.WaitOne();
                        lastResponseTime = startExecutionAt.Elapsed.TotalSeconds;
                        mutexLocker.ReleaseMutex();
                        setDownloadPacket(new stagingDataInfo() { startInfo = nextDownloadInfo.startInfo, pageSize = nextDownloadInfo.pageSize, data = helper.GetDataTableFromResponse(dataPacket) });
                        fillReadyQueueWorker(totalRecords);
                        //CommonItems.Logger.Trace($"getDataWorker => Complete fetching data {Thread.CurrentThread.Name}, done...");
                    });
                    if (downloadTask.IsFaulted)
                    {
                        onFailed(downloadTask.Exception);
                        return;
                    }
                    downloadTask.Wait(cancellationToken.Token);
                    nextDownloadInfo = getNextDownloadPacketInfo(totalRecords);
                }
            };

            HttpContext.ApplicationInstance.Context.Request.TimedOutToken.Register((args) =>
            {
            }, HttpContext.ApplicationInstance.Context.Request.TimedOutToken.WaitHandle);
            try
            {
                var reportSummary = "Summary";
                var separator = ",";
                adapter.DocumentStructure = new DocumentStructure()
                {
                    ReportName = report.ReportName,
                    BusinessName = "UserOrganizationName",
                    CopyRight = $"{"Poweredby"} {"CompanyName"}{"\u2122"}",
                    GeneratedAt = $"{"Report_Template_Generated"} {DateTime.UtcNow.ToString()} (UTC)",
                    username = user.Identity.Name,
                    Criteria = ToBullet($"{"Report_Template_Criteria"}{separator}{reportSummary}", separator)
                };
                Helper.ReportEngine helper = new Helper.ReportEngine(report, _apiEndpoints);
                var reportStatsHttpClient = helper.GetReportStatsHttpClient(Request, recordsToDownload);
                prepareResponse();
                notifyClientDownloadOnStart();
                // report data observer
                var reportDataReadyObservable = Observable.FromEventPattern<ReportDataReadyEventArgs>(
                    addHander => OnReportDataReady += addHander,
                    removeHandler => OnReportDataReady -= removeHandler,
                    TaskPoolScheduler.Default
                    )
                    .ObserveOn(new EventLoopScheduler(sts => new Thread(sts)))
                    .Select(eventPattern =>
                    {
                        return eventPattern.EventArgs;
                    })
                    .Finally(() =>
                    {
                        onFailed(new ArgumentException());
                    });

                var exportTask = adapter.Export(
                reportDataReadyObservable,
                HttpContext.Response.OutputStream,
                () =>
                {
                    // currently the used overload uses direct response stream to write.
                    //HttpContext.Response.OutputStream.Write(stream, 0, stream.Length);
                    HttpContext.Response.Flush();
                },
                (streamLength) =>
                {
                    notifyClientDownloadOnSuccess();
                    HttpContext.Response.Flush();
                    exportCompletedEvent.Set();
                });

                // start workers threads ...
                for (int i = 0; i < WORKER_THREADS; i++)
                {
                    new Thread(new ThreadStart(() => getDataWorker(reportStatsHttpClient, helper))) { Name = $"dataWorker_{i}" }.Start();
                }
                WaitHandle.SignalAndWait(fetcheDataWorkerEvent, exportCompletedEvent);
                return Task.FromResult<ActionResult>(new HttpStatusCodeResult(HttpStatusCode.OK));
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                onFailed(ex);
                return new Task<ActionResult>(() => { return new HttpStatusCodeResult(HttpStatusCode.InternalServerError); });
            }
            finally
            {
                //cleanup if required!
                fetcheDataWorkerEvent.Dispose();
                exportCompletedEvent.Dispose();
            }
        }

        // <summary>
        /// Ajax handler to fetch data for download.
        /// </summary>
        /// <param name="model">The model for download criteria</param>
        /// <returns>DataTables structure (Json)</returns>
        /// <remarks>
        /// Created By: Mohamed Abdo
        ///       Date: 18 Jan 2017
        /// </remarks>
        [HandleError]
        [System.Web.Http.HttpGet]
        public Task DownloadReportData([FromUri]string reportName, [FromUri] int recordsToDownload, [FromUri] string fileName, [FromUri] string target)
        {
            OnReportResponseReady += (object sender, ReportResponseReadyEventArgs args) =>
            {
                // future use for selective fetching data instead of scan fetch data.
            };
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromHours(TIMEOUT_IN_HOURS));
            //initiate events state to off.
            fetcheDataWorkerEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            exportCompletedEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            reactiveDataReadyEvent = new EventWaitHandle(false, EventResetMode.AutoReset);

            Mutex mutexLocker = new Mutex();
            List<initialDataInfo> initialDataCollection = new List<initialDataInfo>();
            List<stagingDataInfo> stagingDataCollection = new List<stagingDataInfo>();
            ConcurrentQueue<ReportDataReadyEventArgs> reactiveDataQueue = new ConcurrentQueue<ReportDataReadyEventArgs>();
            var user = HttpContext.GetOwinContext().Authentication.User;
            var connectionId = Utilities.GetHashToken(user);
            Action<object> notifyClientDownloadOnProgress = (args) =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient(connectionId);
            };
            Action notifyClientDownloadOnSuccess = () =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient_on_file_download_successfully(connectionId, fileName);
            };
            Action notifyClientDownloadOnStart = () =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient_on_file_download_start(connectionId, fileName);
            };
            Action<string> notifyClientDownloadOnFailed = (errorMessage) =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient_on_file_download_failed(connectionId, errorMessage);
            };
            if (string.IsNullOrEmpty(reportName) || recordsToDownload < 1)
            {
                var uiEx = new UiException("FailedToExportNoExportData");
                notifyClientDownloadOnFailed(uiEx.Message);
                return Task.FromResult<ActionResult>(Json(uiEx.Message, JsonRequestBehavior.AllowGet));
            }
            var report = new Report() { TotalRecords = recordsToDownload, ReportName = reportName };
            if (report == null)
            {
                var uiEx = new UiException("FailedToExport");
                notifyClientDownloadOnFailed(uiEx.Message);
                return Task.FromResult<ActionResult>(new EmptyResult());
            }
            if (report.TotalRecords > MAX_RECORDS_TO_DOWNLOAD)
            {
                var uiEx = new UiException("FailedToExportTooManyRecords: " + MAX_RECORDS_TO_DOWNLOAD);
                notifyClientDownloadOnFailed(uiEx.Message);
                return Task.FromResult<ActionResult>(new EmptyResult());
            }
            ExporterAdaptee adapter = AdapterFactory.Create(target);
            Action prepareResponse = () =>
            {
                HttpContext.Response.Clear();
                HttpContext.Response.ContentType = adapter.GetContentType;
                HttpContext.Response.Buffer = false;
                HttpContext.Response.BufferOutput = false;
                HttpContext.Response.AddHeader("content-disposition", $"attachment; filename={fileName}");
                HttpContext.Response.AddHeader("content-type", adapter.GetContentType);
            };
            Func<string, string, IEnumerable<string>> ToBullet = (content, separator) =>
            {
                return content.Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries).Select(s =>
                         $"\u2022 {s?.Trim()}");
            };
            Func<DataTable, IEnumerable<string>> getHeader = (tableStructure) =>
            {
                return tableStructure.Columns.Cast<DataColumn>().Select(column => column.ColumnName);
            };
            Action<Exception> onFailed = (ex) =>
            {
                //CommonItems.Logger?.ErrorException(ex, "Error occurred while getting data for download report.");
                notifyClientDownloadOnFailed(ex.Message);
            };
            int? lastPayLoad = null;
            double? lastResponseTime = null;
            Func<int, initialDataInfo> getNextDownloadPacketInfo = (totalRecords) =>
            {
                mutexLocker.WaitOne();
                var nextPayLoad = Helper.ReportEngine.getDownloadPageSize(totalRecords, lastPayLoad, lastResponseTime);
                var maxStartFrom = START_INDEX_FROM;
                if (initialDataCollection.Count > 0)
                    maxStartFrom = initialDataCollection.Last().startInfo + initialDataCollection.Last().pageSize;
                var nextDownloadInfo = new initialDataInfo() { startInfo = maxStartFrom, pageSize = nextPayLoad };
                mutexLocker.ReleaseMutex();
                return nextDownloadInfo;
            };
            Action<stagingDataInfo> setDownloadPacket = (downloadInfo) =>
            {
                mutexLocker.WaitOne();
                stagingDataCollection.Add(downloadInfo);
                mutexLocker.ReleaseMutex();
            };
            Action<initialDataInfo> setInitialDownloadPacket = (downloadInfo) =>
            {
                mutexLocker.WaitOne();
                initialDataCollection.Add(downloadInfo);
                mutexLocker.ReleaseMutex();
            };
            Func<int, bool> IsResponseCompleted = (totalRecors) =>
            {
                mutexLocker.WaitOne();
                var maxStartInfo = initialDataCollection.Max(r => r.startInfo);
                var maxStartInfoPageSize = initialDataCollection.First(s => s.startInfo == maxStartInfo).pageSize;
                var result = (maxStartInfo + maxStartInfoPageSize) >= totalRecors;
                mutexLocker.ReleaseMutex();
                return (result || maxStartInfoPageSize >= totalRecors);// data ready on single packet case or waiting for last exporter callback
            };
            int nextStartFromStaging = 0;
            Action<int> fillReadyQueueWorker = (totalRecords) =>
            {
                //CommonItems.Logger.Trace($"fillReadyQueueWorker => Starting thread {Thread.CurrentThread.ManagedThreadId}, start processing signal ...");
                for (int i = 0; i < stagingDataCollection.Count; i++)
                {
                    var targetedPacketIdx = stagingDataCollection.FindIndex(s => s.startInfo == nextStartFromStaging);
                    if (targetedPacketIdx != -1)
                    {
                        mutexLocker.WaitOne();
                        nextStartFromStaging = stagingDataCollection[targetedPacketIdx].startInfo + stagingDataCollection[targetedPacketIdx].pageSize;
                        var isLast = (stagingDataCollection.Count == 1 && IsResponseCompleted(totalRecords));
                        reactiveDataQueue.Enqueue(new ReportDataReadyEventArgs(stagingDataCollection[targetedPacketIdx].data, isLast));
                        reactiveDataReadyEvent.Set();
                        stagingDataCollection.RemoveAt(targetedPacketIdx);
                        mutexLocker.ReleaseMutex();
                    }
                }
            };
            Action<HttpClient, Helper.ReportEngine> getDataWorker = (httpClient, helper) =>
             {
                 if (!helper.getCurrentModelTotalRecords.HasValue && helper.getCurrentModelTotalRecords.Value > 0)
                     throw new ArgumentNullException("Report_MsgError_FailedToExportNoExportData");
                 fetcheDataWorkerEvent.WaitOne();
                 //CommonItems.Logger.Trace($"getDataWorker => Starting fetching data {Thread.CurrentThread.Name}, event signal fired...");
                 var totalRecords = helper.getCurrentModelTotalRecords.Value;
                 var nextDownloadInfo = getNextDownloadPacketInfo(totalRecords);
                 while (nextDownloadInfo.startInfo < totalRecords)
                 {
                     setInitialDownloadPacket(nextDownloadInfo);
                     var startExecutionAt = Stopwatch.StartNew();
                     var downloadTask = helper.GetDownloadDataAsync(httpClient, Request, nextDownloadInfo.startInfo, nextDownloadInfo.pageSize, dataPacket =>
                     {
                         startExecutionAt.Stop();
                         mutexLocker.WaitOne();
                         lastResponseTime = startExecutionAt.Elapsed.TotalSeconds;
                         mutexLocker.ReleaseMutex();
                         setDownloadPacket(new stagingDataInfo() { startInfo = nextDownloadInfo.startInfo, pageSize = nextDownloadInfo.pageSize, data = helper.GetDataTableFromResponse(dataPacket) });
                         fillReadyQueueWorker(totalRecords);
                         //CommonItems.Logger.Trace($"getDataWorker => Complete fetching data {Thread.CurrentThread.Name}, done...");
                     });
                     if (downloadTask.IsFaulted)
                     {
                         onFailed(downloadTask.Exception);
                         return;
                     }
                     downloadTask.Wait(cancellationToken.Token);
                     nextDownloadInfo = getNextDownloadPacketInfo(totalRecords);
                 }
             };
            Action ManuallyTrySetTheEvent = () =>
            {
                if (reactiveDataQueue.Count > 0)
                    reactiveDataReadyEvent.Set();
            };
            HttpContext.ApplicationInstance.Context.Request.TimedOutToken.Register((args) =>
            {
                // after applying reactive no need to show timeout.
                // onFailed(new TaskCanceledException("request has been canceled due timeout!"));
            }, HttpContext.ApplicationInstance.Context.Request.TimedOutToken.WaitHandle);
            try
            {
                var reportSummary = "Summary";
                var separator = ",";
                adapter.DocumentStructure = new DocumentStructure()
                {
                    ReportName = report.ReportName,
                    BusinessName = "UserOrganizationName",
                    CopyRight = $"{"Poweredby"} {"CompanyName"}{"\u2122"}",
                    GeneratedAt = $"{"Report_Template_Generated"} {DateTime.UtcNow.ToString()} (UTC)",
                    username = user.Identity.Name,
                    Criteria = ToBullet($"{"Report_Template_Criteria"}{separator}{reportSummary}", separator)
                };
                ReportEngine helper = new ReportEngine(report, _apiEndpoints);
                var reportStatsHttpClient = helper.GetReportStatsHttpClient(Request, recordsToDownload);
                prepareResponse();
                notifyClientDownloadOnStart();
                // report data observer

                new Thread(new ThreadStart(() =>
                {
                    adapter.Export(
                                    () =>
                                    {
                                        //CommonItems.Logger.Trace($"reactiveData worker => a waiting for event to start ... {Thread.CurrentThread.Name}, awaiting...");
                                        ManuallyTrySetTheEvent();
                                        reactiveDataReadyEvent.WaitOne();
                                        //CommonItems.Logger.Trace($"reactiveData worker => received event to start export data... {Thread.CurrentThread.Name}, processing...");
                                        ReportDataReadyEventArgs outData;
                                        if (reactiveDataQueue.TryDequeue(out outData))
                                        {
                                            if (outData.IsLast)
                                                reactiveDataReadyEvent.Reset();
                                            // CommonItems.Logger.Trace($"reactiveData worker => Complete fetching data {Thread.CurrentThread.Name}, done...");
                                            return outData;
                                        }
                                        else
                                        {
                                            return null;
                                        }
                                    },
                                    HttpContext.Response.OutputStream,
                                    () =>
                                    {
                                        HttpContext.Response.Flush();
                                    },
                                    (streamLength) =>
                                    {
                                        notifyClientDownloadOnSuccess();
                                        HttpContext.Response.Flush();
                                        exportCompletedEvent.Set();
                                    });
                }))
                { Name = $"ExportWorker" }.Start();
                // start workers threads ...
                for (int i = 0; i < WORKER_THREADS; i++)//
                {

                    new Thread(new ThreadStart(() =>
                    {
                        getDataWorker(reportStatsHttpClient, helper);
                    }))
                    { Name = $"dataWorker_{i}" }.Start();
                }
                WaitHandle.SignalAndWait(fetcheDataWorkerEvent, exportCompletedEvent);
                return Task.FromResult<ActionResult>(new HttpStatusCodeResult(HttpStatusCode.OK));
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                onFailed(ex);
                return new Task<ActionResult>(() => { return new HttpStatusCodeResult(HttpStatusCode.InternalServerError); });
            }
            finally
            {
                //cleanup if required!
                fetcheDataWorkerEvent.Dispose();
                exportCompletedEvent.Dispose();
                reactiveDataReadyEvent.Dispose();
            }
        }

        [System.Web.Http.HttpGet]
        public Task<ActionResult> DownloadReport([FromUri]string reportName, [FromUri] int recordsToDownload, [FromUri] string fileName, [FromUri] string target)
        {
            var user = HttpContext.GetOwinContext().Authentication.User;
            var connectionId = Utilities.GetHashToken(user);
            Action<object> notifyClientDownloadOnProgress = (args) =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient(connectionId);
            };
            Action notifyClientDownloadOnSuccess = () =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient_on_file_download_successfully(connectionId, fileName);
            };
            Action<string> notifyClientDownloadOnFailed = (errorMessage) =>
            {
                var caller = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>().Clients.User(user.Identity.Name);
                if (caller != null)
                    caller.notifyClient_on_file_download_failed(connectionId, errorMessage);
            };
            if (string.IsNullOrEmpty(reportName) || recordsToDownload < 1)
            {
                var uiEx = new UiException("FailedToExportNoExportData");
                notifyClientDownloadOnFailed(uiEx.Message);
                return Task.FromResult<ActionResult>(Json(uiEx.Message, JsonRequestBehavior.AllowGet));
            }
            var report = new Report() { TotalRecords = recordsToDownload, ReportName = reportName };
            if (report.TotalRecords > MAX_RECORDS_TO_DOWNLOAD)
            {
                var uiEx = new UiException("FailedToExportTooManyRecords: " + MAX_RECORDS_TO_DOWNLOAD.ToString());
                notifyClientDownloadOnFailed(uiEx.Message);
                return Task.FromResult<ActionResult>(new EmptyResult());
            }
            ExporterAdaptee adapter = AdapterFactory.Create(target);
            Action<long> prepareResponse = (bytesLength) =>
            {
                HttpContext.Response.AddHeader("content-disposition", $"attachment; filename={fileName}");
                HttpContext.Response.AddHeader("content-length", bytesLength.ToString());
                HttpContext.Response.AddHeader("content-type", adapter.GetContentType);
            };
            Func<string, string, IEnumerable<string>> ToBullet = (content, separator) =>
            {
                return content.Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries).Select(s =>
                         $"\u2022 {s?.Trim()}");
            };
            var progressTimer = new Timer((args) => { notifyClientDownloadOnProgress(args); }, connectionId, 1000, NOTIFY_INTERVAL_IN_MS);
            HttpContext.ApplicationInstance.Context.Request.TimedOutToken.Register((args) =>
            {
                notifyClientDownloadOnFailed("request has been canceled due timeout!");
                progressTimer.Dispose();
            }, HttpContext.ApplicationInstance.Context.Request.TimedOutToken.WaitHandle);
            try
            {
                var reportSummary = "Summary";
                var separator = ",";
                adapter.DocumentStructure = new DocumentStructure()
                {
                    ReportName = report.ReportName,
                    BusinessName = "UserOrganizationName",
                    CopyRight = $"{"Poweredby"} {"CompanyName"}{"\u2122"}",
                    GeneratedAt = $"{"Report_Template_Generated"} {DateTime.UtcNow.ToString()} (UTC)",
                    username = user.Identity.Name,
                    Criteria = ToBullet($"{"Report_Template_Criteria"}{separator}{reportSummary}", separator)
                };
                ReportEngine helper = new ReportEngine(report, _apiEndpoints);
                return helper.GetDownloadData(Request, recordsToDownload).ContinueWith((dataTask) =>
                {
                    if (dataTask.IsFaulted)
                        throw new Exception("Report_MsgError_FailedToExportNoExportData");
                    var dataCollection = dataTask.Result;
                    if (dataCollection == null || dataCollection.Count == 0)
                    {
                        notifyClientDownloadOnFailed("Report_MsgError_FailedToExportNoExportData");
                        throw new ArgumentException("Report_MsgError_FailedToExportNoExportData");
                    }
                    return adapter.Export(dataCollection, (stream) =>
                    {
                        return Task.Factory.StartNew<ActionResult>(() =>
                        {
                            notifyClientDownloadOnSuccess();
                            progressTimer.Dispose();
                            prepareResponse(stream.Length);
                            return new FileStreamResult(new MemoryStream(stream), adapter.GetContentType);
                        });
                    }).Result;
                });
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                //CommonItems.Logger?.ErrorException(ex, "Error occurred while getting data for download report.");
                var uiEx = new UiException("FailedToExport");
                notifyClientDownloadOnFailed(uiEx.Message);
                return new Task<ActionResult>(() => { return new HttpStatusCodeResult(HttpStatusCode.InternalServerError); });
            }
            finally
            {
                //cleanup if required!
            }
        }

    }
}
