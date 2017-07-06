using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using reactive_download.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace reactive_download.Helper
{
    internal class APIResponseFactory<T>
    {
        // NB When deserializing from Json to DataTable the DateTime type becomes string if the first item array value is null.
        // Here is applied the workaround described at:
        // http://stackoverflow.com/questions/37109154/datetime-column-type-becomes-string-type-after-deserializing-datatable

        public static JsonSerializerSettings settings = new JsonSerializerSettings { Converters = new[] { new TypeInferringDataTableConverter() } };

        public static string SanitizeContent(string content)
        {
            //TODO: Sanitize content (e.g. mask organization keys, or anything sensitive which can be returned)
            return content;
        }

        public static Func<string, bool> IsValidJson = (jsonStr) =>
        {
            try
            {
                return JObject.Parse(jsonStr) != null;
            }
            catch
            {
                return false;
            }
        };
        public static APIBaseResponse CreateBaseResponseModel(string content)
        {
            try
            {
                if (!IsValidJson(content))
                    throw new UiException("FailedToDeserializeResponse");
                var model = JsonConvert.DeserializeObject<APIBaseResponse>(content, settings);
                if (model == null)
                    throw new UiException("FailedToDeserializeResponse");
                return model;
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                //CommonItems.Logger?.ErrorException(ex, "Error occurred while creating model from json");
                return default(APIBaseResponse);
            }
        }
        public static APIResponse<T> CreateResponseModel(string content)
        {
            try
            {
                if (!IsValidJson(content))
                    throw new UiException("FailedToDeserializeResponse");
                APIResponse<T> model = new APIResponse<T>();
                model = JsonConvert.DeserializeObject<APIResponse<T>>(content, settings);
                if (model == null)
                    throw new UiException("FailedToDeserializeResponse");
                return model;
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                //CommonItems.Logger?.ErrorException(ex, "Error occurred while creating model from json");
                return default(APIResponse<T>);
            }
        }

        public static T CreateModel(string content)
        {
            try
            {
                APIResponse<T> model = new APIResponse<T>();
                model = JsonConvert.DeserializeObject<APIResponse<T>>(content, settings);
                if (model == null)
                    throw new UiException("FailedToDeserializeResponse");
                return model.result;
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                //CommonItems.Logger?.ErrorException(ex, "Error occurred while creating model from json");
                return default(T);
            }
        }

        public static T CreatePOCOModel(string content)
        {
            try
            {
                T model = default(T);
                model = JsonConvert.DeserializeObject<T>(content, settings);
                if (model == null)
                    throw new UiException("FailedToDeserializeResponse");
                return model;
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                //CommonItems.Logger?.ErrorException(ex, "Error occurred while creating model from json");
                return default(T);
            }
        }

        public static Tuple<APIBaseResponse, T> CreateModelAsTuple(string content)
        {
            try
            {
                APIResponse<T> model = new APIResponse<T>();
                model = JsonConvert.DeserializeObject<APIResponse<T>>(content, settings);
                if (model == null)
                    throw new UiException("FailedToDeserializeResponse");
                return new Tuple<APIBaseResponse, T>(model, model.result);
            }
            catch (Exception ex) when (!(ex is UiException))
            {
                // CommonItems.Logger?.ErrorException(ex, "Error occurred while creating model from json");
                return default(Tuple<APIBaseResponse, T>);
            }
        }
    }
}