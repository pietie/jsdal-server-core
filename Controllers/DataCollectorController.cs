using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jsdal_server_core.Performance.DataCollector;
using jsdal_server_core.Settings;
using jsdal_server_core.Settings.ObjectModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace jsdal_server_core.Controllers
{

    [Authorize(Roles = "admin")]
    public class DataCollectorController : Controller
    {
        [HttpGet("/api/data-collector")]
        public ActionResult Test()
        {
            return Ok(DataCollectorThread.Instance.GetAllDataTmp());

        }

        [HttpGet("/api/data-collector/topN")]
        public ActionResult TopNResources([FromQuery(Name = "n")] int topN,
            [DateTimeModelBinder(DateFormat = "yyyyMMddHHmm"), FromQuery(Name = "from")] DateTime? fromDate,
            [DateTimeModelBinder(DateFormat = "yyyyMMddHHmm"), FromQuery(Name = "to")] DateTime? toDate,
            [FromQuery] string[] endpoints,
            [FromQuery] TopNResourceType type)
        {
            if (!fromDate.HasValue) return BadRequest("The parameter 'from' is mandatory");
            if (!toDate.HasValue) return BadRequest("The parameter 'to' is mandatory");


            return Ok(DataCollectorThread.Instance.GetTopNResource(topN, fromDate.Value, toDate.Value, endpoints, type));
        }

        [HttpGet("/api/data-collector/topN-list")]
        public ActionResult TopNAllStatsList([FromQuery(Name = "n")] int topN,
           [DateTimeModelBinder(DateFormat = "yyyyMMddHHmm"), FromQuery(Name = "from")] DateTime? fromDate,
           [DateTimeModelBinder(DateFormat = "yyyyMMddHHmm"), FromQuery(Name = "to")] DateTime? toDate,
           [FromQuery] string[] endpoints)
        {
            if (!fromDate.HasValue) return BadRequest("The parameter 'from' is mandatory");
            if (!toDate.HasValue) return BadRequest("The parameter 'to' is mandatory");

            return Ok(DataCollectorThread.Instance.AllStatsList(topN, fromDate.Value, toDate.Value, endpoints));
        }

        [HttpGet("/api/data-collector/routine-totals")]
        public ActionResult RoutineTotals([FromQuery(Name = "schema")] string schema, [FromQuery(Name = "routine")] string routine,
            [DateTimeModelBinder(DateFormat = "yyyyMMddHHmm"), FromQuery(Name = "from")] DateTime? fromDate,
            [DateTimeModelBinder(DateFormat = "yyyyMMddHHmm"), FromQuery(Name = "to")] DateTime? toDate,
            [FromQuery] string[] endpoints)
        {
            if (string.IsNullOrWhiteSpace(schema)) return BadRequest("The parameter 'schema' is mandatory");
            if (string.IsNullOrWhiteSpace(routine)) return BadRequest("The parameter 'routine' is mandatory");
            if (!fromDate.HasValue) return BadRequest("The parameter 'from' is mandatory");
            if (!toDate.HasValue) return BadRequest("The parameter 'to' is mandatory");

            return Ok(DataCollectorThread.Instance.GetRoutineAllStats(schema, routine, fromDate.Value, toDate.Value, endpoints));
        }

        [HttpGet("/api/data-collector/endpoints")]
        public ApiResponse GetAllEndpoints()
        {
            try
            {
                var q = SettingsInstance.Instance.ProjectList
                                  .SelectMany(p =>
                                          p.Applications.SelectMany(app => app.Endpoints.Select(ep => new
                                          {
                                              Endpoint = ep.Pedigree
                                          })))
                                .OrderBy(e => e.Endpoint)
                                ;

                return ApiResponse.Payload(q);
            }
            catch (Exception ex)
            {
                return ApiResponse.Exception(ex);
            }
        }

        [HttpDelete("/api/data-collector/executions")]
        public ActionResult ClearoutExecutions()
        {
            return Ok(DataCollectorThread.Instance.ClearExecutions());

        }

        [HttpPost("/api/data-collector/purge")]
        public ActionResult PurgeOld([FromQuery] int daysOld)
        {
            int deleteCnt = DataCollectorThread.Instance.PurgeOldAggregates(daysOld);
            return Ok(deleteCnt);
        }


        [HttpGet("/api/data-collector/stats/agg")]
        public ActionResult AggregateStats()
        {
            try
            {
                return Ok(DataCollectorThread.Instance.GetAggregateStats());
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }

        }

        [HttpPost("/api/data-collector/start")]
        public ActionResult RestartThread()
        {
            try
            {
                DataCollectorThread.Instance.Restart();
                return Ok(true);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }

        }

        [HttpPost("/api/data-collector/stop")]
        public ActionResult StopThread()
        {
            try
            {
                DataCollectorThread.Instance.Shutdown();
                return Ok(true);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }

        }

        [HttpGet("/api/data-collector/thread-status")]
        public ActionResult GetThreadStatus()
        {
            try
            {
                return Ok(new
                {
                    IsRunning = DataCollectorThread.Instance.IsRunning

                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }

        }


    }


    public class DateTimeModelBinder : IModelBinder
    {
        public static readonly Type[] SUPPORTED_TYPES = new Type[] { typeof(DateTime), typeof(DateTime?) };

        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            if (!SUPPORTED_TYPES.Contains(bindingContext.ModelType))
            {
                return Task.CompletedTask;
            }

            var modelName = GetModelName(bindingContext);

            var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);
            if (valueProviderResult == ValueProviderResult.None)
            {
                return Task.CompletedTask;
            }

            bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);

            var dateToParse = valueProviderResult.FirstValue;

            if (string.IsNullOrEmpty(dateToParse))
            {
                return Task.CompletedTask;
            }

            var dateTime = ParseDate(bindingContext, dateToParse);

            bindingContext.Result = ModelBindingResult.Success(dateTime);

            return Task.CompletedTask;
        }

        private DateTime? ParseDate(ModelBindingContext bindingContext, string dateToParse)
        {
            var attribute = GetDateTimeModelBinderAttribute(bindingContext);
            var dateFormat = attribute?.DateFormat;

            if (string.IsNullOrEmpty(dateFormat))
            {
                return DateTime.Parse(dateToParse);
                //return Helper.ParseDateTime(dateToParse);
            }

            if (DateTime.TryParseExact(dateToParse, dateFormat, null, System.Globalization.DateTimeStyles.None, out var res))
            {
                return res;
            }
            else
            {
                return null;
            }
            //return Helper.ParseDateTime(dateToParse, new string[] { dateFormat });
        }

        private DateTimeModelBinderAttribute GetDateTimeModelBinderAttribute(ModelBindingContext bindingContext)
        {
            var modelName = GetModelName(bindingContext);

            var paramDescriptor = bindingContext.ActionContext.ActionDescriptor.Parameters
                .Where(x => x.ParameterType == typeof(DateTime?))
                .Where((x) =>
                {
                    // See comment in GetModelName() on why we do this.
                    var paramModelName = x.BindingInfo?.BinderModelName ?? x.Name;
                    return paramModelName.Equals(modelName);
                })
                .FirstOrDefault();

            var ctrlParamDescriptor = paramDescriptor as ControllerParameterDescriptor;
            if (ctrlParamDescriptor == null)
            {
                return null;
            }

            var attribute = ctrlParamDescriptor.ParameterInfo
                .GetCustomAttributes(typeof(DateTimeModelBinderAttribute), false)
                .FirstOrDefault();

            return (DateTimeModelBinderAttribute)attribute;
        }

        private string GetModelName(ModelBindingContext bindingContext)
        {
            // The "Name" property of the ModelBinder attribute can be used to specify the
            // route parameter name when the action parameter name is different from the route parameter name.
            // For instance, when the route is /api/{birthDate} and the action parameter name is "date".
            // We can add this attribute with a Name property [DateTimeModelBinder(Name ="birthDate")]
            // Now bindingContext.BinderModelName will be "birthDate" and bindingContext.ModelName will be "date"
            if (!string.IsNullOrEmpty(bindingContext.BinderModelName))
            {
                return bindingContext.BinderModelName;
            }

            return bindingContext.ModelName;
        }
    }

    public class DateTimeModelBinderAttribute : ModelBinderAttribute
    {
        public string DateFormat { get; set; }

        public DateTimeModelBinderAttribute()
            : base(typeof(DateTimeModelBinder))
        {
        }
    }

}