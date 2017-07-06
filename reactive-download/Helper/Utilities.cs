using reactive_download.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace reactive_download.Helper
{
    public static class Utilities
    {
        public static Func<ClaimsPrincipal, string> GetHashToken = (userClaim) =>
        {
            var token = userClaim.Identity?.Name ?? "username";
            var tokenBytes = Encoding.UTF8.GetBytes(token);
            using (HashAlgorithm hash = SHA256.Create())
            {
                var hashed = hash.ComputeHash(tokenBytes);
                return Convert.ToBase64String(hashed);
            }
        };
        public static string GetIdentityToken(this ClaimsPrincipal identity)
        {
            var token = identity?.FindFirst((claim) => claim.Type == ClaimTypes.NameIdentifier);

            return token?.Value;
        }

        public static T GetHttpModel<T>(HttpResponseMessage response, out string httpMessage)
        {
            T t = default(T);
            httpMessage = string.Empty;
            var result = response.Content.ReadAsStringAsync().Result;
            // get generic api response
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    {
                        httpMessage = "Resource not found";
                        break;
                    }
                case HttpStatusCode.Unauthorized:
                    {
                        throw new UiException("Unauthorized");
                    }
                case HttpStatusCode.ServiceUnavailable:
                    {
                        throw new UiException("UiSystemMaintenance");
                    }
                case HttpStatusCode.BadGateway:
                    {
                        throw new UiException("GenericException");
                    }
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.InternalServerError:
                case HttpStatusCode.Accepted:
                case HttpStatusCode.OK:
                    {
                        var model = APIResponseFactory<T>.CreatePOCOModel(result);
                        httpMessage = "Ok";
                        t = model;
                    }
                    break;
                default:
                    {
                        httpMessage = $"{"GenericException"} Code ({502})"; ;
                        break;
                    }
            }
            return t;
        }

        public static Func<T> GetHttpModelAsCurry<T>(HttpResponseMessage response, Func<string, T> resolver, out string httpMessage)
        {
            T t = default(T);
            Func<T> returnFunc = () => { return t; };
            httpMessage = string.Empty;
            var result = response.Content.ReadAsStringAsync().Result;
            // get generic api response.
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    {
                        httpMessage = "Resource not found";
                        break;
                    }
                case HttpStatusCode.Unauthorized:
                    {
                        throw new UiException("Unauthorized");
                    }
                case HttpStatusCode.ServiceUnavailable:
                    {
                        throw new UiException("UiSystemMaintenance");
                    }
                case HttpStatusCode.BadGateway:
                    {
                        throw new UiException("GenericException");
                    }
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.InternalServerError:
                case HttpStatusCode.Accepted:
                case HttpStatusCode.OK:
                    {
                        returnFunc = () => { return resolver(result); };
                    }
                    break;
                default:
                    {
                        httpMessage = $"{"GenericException"} Code ({502})"; ;
                        break;
                    }
            }
            return returnFunc;
        }

    }
}