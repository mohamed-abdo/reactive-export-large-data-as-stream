using System;

namespace reactive_download.Models
{
    internal class APIBaseResponse
    {
        public int resultCode { get; set; }
        public bool isSuccessCode => resultCode == 0;
        public string message { get; set; }
        public int resultClass { get; set; }
        public string actionHint { get; set; }
        public Guid requestReference { get; set; }
    }
    internal class APIResponse<T> : APIBaseResponse
    {
        public T result { get; set; }
    }

}