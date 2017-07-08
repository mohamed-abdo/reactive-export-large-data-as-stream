using Newtonsoft.Json;
using reactive_download.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Linq;

namespace reactive_download.Helper
{
    internal class ReportEngine
    {
        public ReportEngine(Report Report, APIEndpoints apiEndPoint)
        {

            _apiEndPoint = apiEndPoint;
            this._report = Report;
        }

        private Report _report;
        private const string CONTENT_TYPE = "application/json";
        private readonly APIEndpoints _apiEndPoint;
        const int INITIAL_PAYLOAD = 100;//minimum number of records to get from api.
        const int MAX_PAYLOAD = 1000;
        const int THREESHOLD = 30;//seconds
        private const int START_INDEX_FROM = 0;
        private const int DOWNLOAD_PAGE_SIZE = 1000;
        internal int? getCurrentModelTotalRecords => _report.TotalRecords;

        /// <summary>
        /// Get data for specified reports (calls report API).
        /// </summary>
        /// <returns>DatTable structure (to be serialized in Json format before returning from Ajax call to DataTables plugin)</returns>
        /// <remarks>
        /// Created By: Mohamed Abdo
        ///       Date: 08 Jan 2017
        /// </remarks>
        public async Task<List<DataTable>> GetDownloadData(HttpRequestBase httpRequest, int? limitDownloadRecords = null)
        {
            string httpMessage = string.Empty;
            Func<int, int> getDownloadPageSize = (totalRecords) =>
            {
                return DOWNLOAD_PAGE_SIZE;
            };
            int pagesize = getDownloadPageSize(_report.TotalRecords);
            try
            {
                // Call API...
                var reportApiClient = GetReportStatsHttpClient(httpRequest, limitDownloadRecords);
                int startFrom = START_INDEX_FROM;
                List<DataTable> dataset = new List<DataTable>();
                while (startFrom < _report.TotalRecords)
                {
                    var reportApiresponse = await reportApiClient.GetAsync(
                    $"{_apiEndPoint.Data}?pagesize={pagesize}&startFrom={startFrom}");
                    var responseModel = Utilities.GetHttpModel<IEnumerable<Employee>>(reportApiresponse, out httpMessage);
                    var dataTable = BuildDataTable(responseModel
                       .Select(TransformToDownloadView)
                       .Select(TransformToRow));
                    dataset.Add(dataTable);
                    startFrom += pagesize;
                }
                return await Task.FromResult(dataset);
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                //CommonItems.Logger?.ErrorException(ex, $"Error occurred while getting data for download report, {httpMessage}");
                throw new UiException("GenericException");
            }
        }

        public IEnumerable<Task<DataTable>> GetDownloadDataAsync(HttpClient reportStatusCleint, HttpRequestBase httpRequest)
        {
            const int TIME_OUT = 3;
            double? responseInSeconds = null;
            int currentpagesize = 0;
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromHours(TIME_OUT));
            string httpMessage = string.Empty;
            int startFrom = START_INDEX_FROM;
            int idx = 0;
            while (startFrom < _report.TotalRecords)
            {
                currentpagesize = getDownloadPageSize(_report.TotalRecords, currentpagesize, responseInSeconds);
#if DEBUG
                System.Diagnostics.Debug.Print("Begin Call Report API => index => {0}, startFrom =>{1}, pagesize=>{2}, url=>{3}, snapshot=> {4}, at=> {5}",
                    idx++, startFrom, currentpagesize, $"{_apiEndPoint.Data}?startFrom={startFrom}&pagesize={currentpagesize}", _report.Criteria, DateTime.Now.ToString());
#endif
                var startTime = Stopwatch.StartNew();
                yield return reportStatusCleint.GetAsync(
                $"{_apiEndPoint.Data}?startFrom={startFrom}&pagesize={currentpagesize}", cancellationToken.Token).ContinueWith(responseTask =>
                    {
                        startTime.Stop();
                        responseInSeconds = startTime.Elapsed.TotalSeconds;
#if DEBUG
                        System.Diagnostics.Debug.Print("End Call Report API => index => {0}, duration in sec. => {1}, startFrom =>{2}, pagesize=>{3}, url=>{4}, snapshot=> {5}, at=> {6}",
                                          idx++, responseInSeconds, startFrom, currentpagesize, $"{_apiEndPoint.Data}?startFrom={startFrom}&pagesize={currentpagesize}", _report.Criteria, DateTime.Now.ToString());
#endif
                        startFrom += currentpagesize;
                        return GetDataTableFromResponseAsync(responseTask.Result, cancellationToken.Token).ContinueWith(transformationTask =>
                        {
                            return transformationTask.Result;
                        }).Result;
                    });
            }
        }

        public Task GetDownloadDataAsync(HttpClient reportStatusCleint, HttpRequestBase httpRequest, int startFrom, int pagesize, Action<HttpResponseMessage> responseCallBack)
        {
            const int TIME_OUT = 3;
            double? responseInSeconds = null;
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromHours(TIME_OUT));
            string httpMessage = string.Empty;
#if DEBUG
            System.Diagnostics.Debug.Print("Begin Call Report API => startFrom =>{0}, pagesize=>{1}, url=>{2}, snapshot=> {3}, at=> {4}",
                 startFrom, pagesize, $"{_apiEndPoint.Data}?pagesize={pagesize}&startFrom={startFrom}", _report.Criteria, DateTime.Now.ToString());
#endif
            var startTime = Stopwatch.StartNew();
            return reportStatusCleint.GetAsync(
            $"{_apiEndPoint.Data}?pagesize={pagesize}&startFrom={startFrom}", cancellationToken.Token).ContinueWith(responsetask =>
                {
                    startTime.Stop();
                    responseInSeconds = startTime.Elapsed.TotalSeconds;
#if DEBUG
                    Debug.Print("End Call Report API =>  duration in sec. => {0}, startFrom =>{1}, pagesize=>{2}, url=>{3}, snapshot=> {4}, at=> {5}",
                                     responseInSeconds, startFrom, pagesize, $"{_apiEndPoint.Data}?pagesize={pagesize}&startFrom={startFrom}", _report.Criteria, DateTime.Now.ToString());
#endif
                    responseCallBack(responsetask.Result);
                });

        }

        #region Helpers

        public Func<Employee, DownloadView> TransformToDownloadView = (emplpyee) =>
        {
            if (emplpyee == null)
                return new DownloadView();
            return new DownloadView()
            {
                Id = emplpyee.Id,
                EmployeId = emplpyee.EmployeeId,
                Bithdate = emplpyee.Bithdate,
                City = emplpyee.City,
                Country = emplpyee.Country,
                Gender = Enum.GetName(typeof(Gender), emplpyee.Gender),
                Mobile = emplpyee.Mobile,
                Name = emplpyee.Name,
                Organization = emplpyee.Organization?.Name,
                WorkingCountry = emplpyee.Organization?.Country,
                WorkingAddress = emplpyee.Organization?.Address
            };
        };

        public Func<DownloadView, object[]> TransformToRow = (dView) =>
        {
            if (dView == null)
                return null;
            return dView.GetType().GetProperties().Select(prop =>
            {
                return prop.GetValue(dView);
            }).ToArray();
        };

        public Func<IEnumerable<object[]>, DataTable> BuildDataTable = (rows) =>
        {
            DataTable dt = new DataTable();
            var colNum = rows.FirstOrDefault()?.Count() ?? 0;
            if (colNum == 0)
                return dt;
            Enumerable.Range(0, colNum).ToList().ForEach(ixd =>
            {
                dt.Columns.Add();
            });
            foreach (var row in rows)
            {
                if (row == null)
                    continue;
                dt.Rows.Add(row);
            }
            return dt;
        };

        public Task<DataTable> GetDataTableFromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string httpMessage;
            var responseModel = Utilities.GetHttpModel<IEnumerable<Employee>>(response, out httpMessage);
            var dataTable = BuildDataTable(responseModel
               .Select(TransformToDownloadView)
               .Select(TransformToRow));
            return Task.Factory.StartNew(() => { return dataTable; }, cancellationToken);
        }

        public DataTable GetDataTableFromResponse(HttpResponseMessage response)
        {
            string httpMessage;
            var responseModel = Utilities.GetHttpModel<IEnumerable<Employee>>(response, out httpMessage);
            var dataTable = BuildDataTable(responseModel
               .Select(TransformToDownloadView)
               .Select(TransformToRow));
            return dataTable;
        }

        public HttpClient CreateClient(string baseUrl)
        {
            var httpClient = new HttpClient() { BaseAddress = new Uri(baseUrl) };
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(CONTENT_TYPE));
            return httpClient;
        }

        public HttpClient GetReportStatsHttpClient(HttpRequestBase httpRequest, int? limitDownloadRecords = null)
        {
            // Getting API client and check identity
            var userIdentity = HttpContext.Current.GetOwinContext().Authentication.User;
            var reportApiClient = CreateClient(_apiEndPoint.BaseUrl);

            if (string.IsNullOrEmpty(_report.Criteria)) // New request
            {
                // Submit request
                HttpResponseMessage reportApiresponse = reportApiClient.GetAsync(
                    _apiEndPoint.Statistics).Result;
                if (!reportApiresponse.IsSuccessStatusCode)
                {
                    reportApiresponse.EnsureSuccessStatusCode();
                }
                string httpMessage;
                var responseModel = Utilities.GetHttpModel<ResultBreif>(reportApiresponse, out httpMessage);
                // since the total number of records is just for demo, i belive you in real usage your backend api should control how many records available for your call.
                //TODO: remove this condition in real api calls
                if (limitDownloadRecords.HasValue)
                {
                    if (limitDownloadRecords.Value > INITIAL_PAYLOAD)
                        _report.TotalRecords = limitDownloadRecords.Value - INITIAL_PAYLOAD;
                    else
                        _report.TotalRecords = limitDownloadRecords.Value;
                }
                else
                    _report.TotalRecords = responseModel.TotalRecords;
                _report.ReportName = responseModel.ReportName;
                _report.Criteria = responseModel.Criteria;
            }
            return reportApiClient;
        }
        public static Func<int, int?, double?, int> getDownloadPageSize = (totalRecords, lastPayLoad, responseTime) =>
        {
            var locker = new object();
            lock (locker)
            {
                int proposdPayLoad = (int)((THREESHOLD / (responseTime ?? THREESHOLD)) * (lastPayLoad ?? INITIAL_PAYLOAD));
                var minProposedPayLoad = Math.Min(proposdPayLoad, MAX_PAYLOAD);
                if (totalRecords <= INITIAL_PAYLOAD)
                    return Math.Min(proposdPayLoad, totalRecords);
                return Math.Min(minProposedPayLoad, (totalRecords - INITIAL_PAYLOAD));
            }
        };

        public T getModelFromString<T>(string modelAsStr)
        {
            var nameValue = HttpUtility.ParseQueryString(modelAsStr);
            var modelAsJson = JsonConvert.SerializeObject(nameValue?.AllKeys.ToDictionary(k => k, k => nameValue[k]));
            return JsonConvert.DeserializeObject<T>(modelAsJson);
        }

        #endregion
    }
}
