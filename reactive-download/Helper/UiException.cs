using System;

namespace reactive_download.Helper
{
    internal class UiException : Exception
    {
        public UiException(string type) : base(type)
        {

        }
    }
}