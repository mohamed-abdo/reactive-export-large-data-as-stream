using DataStreamAPIGenerator.DataRepository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using System;
using System.Text;

namespace DataStreamAPIGenerator.Controllers
{
    [Route("api/[controller]")]
    public class DataStreamController : Controller
    {
        const string MIME_TYPE = "application/json";
        private readonly ILogger _logger;
        private readonly IDataGenerator _generator;
        public DataStreamController(ILogger<DataStreamController> logger, IDataGenerator generator)
        {
            _logger = logger;
            _generator = generator;
        }
        [HttpGet]
        public IActionResult Index()
        {
            return Json("Hello World;");
        }
        [Route("summary")]
        [HttpGet]
        public IActionResult Summary()
        {
            var data = JsonConvert.SerializeObject(_generator.BuildSummary());
            return Content(data, MIME_TYPE, Encoding.UTF8);
        }

        [Route("dataTable/{pageSize?}/{startFrom?}")]
        [HttpGet]
        public IActionResult GetDataTable([FromQuery]int pageSize=10,[FromQuery] int startFrom=0)
        {
            var data = JsonConvert.SerializeObject(_generator.BuildEmployeesCopy(pageSize, startFrom));
            return Content(data, MIME_TYPE, Encoding.UTF8);
        }
    }
}