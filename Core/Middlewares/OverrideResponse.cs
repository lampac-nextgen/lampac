using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis.Scripting;
using Shared;
using Shared.Models.Base;
using Shared.Models.CSharpGlobals;
using Shared.Services;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares
{
    public class OverrideResponse
    {
        #region static
        public readonly static ScriptOptions evalOptions = ScriptOptions.Default
            .AddReferences(typeof(Console).Assembly).AddImports("System")
            .AddReferences(typeof(Regex).Assembly).AddImports("System.Text.RegularExpressions");

        private readonly RequestDelegate _next;
        private readonly bool first;

        public OverrideResponse(RequestDelegate next, bool first)
        {
            _next = next;
            this.first = first;
        }
        #endregion

        public Task Invoke(HttpContext httpContext)
        {
            var requestInfo = httpContext.Features.Get<RequestModel>();
            if (requestInfo.IsLocalRequest)
                return _next(httpContext);

            foreach (var over in CoreInit.conf.overrideResponse)
            {
                if (over.firstEndpoint != first)
                    continue;

                if (Regex.IsMatch(httpContext.Request.Path.Value, over.pattern, RegexOptions.IgnoreCase))
                {
                    switch (over.action)
                    {
                        case "html":
                            {
                                httpContext.Response.ContentType = over.type;

                                if (over.val.Contains("{localhost}"))
                                    return httpContext.Response.WriteAsync(over.val.Replace("{localhost}", CoreInit.Host(httpContext)));

                                return httpContext.Response.WriteAsync(over.val);
                            }
                        case "file":
                            {
                                httpContext.Response.ContentType = over.type;

                                if (string.IsNullOrEmpty(over.val) || !File.Exists(over.val))
                                {
                                    httpContext.Response.StatusCode = 404;
                                    return Task.CompletedTask;
                                }

                                if (Regex.IsMatch(over.val, "\\.(html|txt|css|js|json|xml)$", RegexOptions.IgnoreCase))
                                {
                                    string val = FileCache.ReadAllText(over.val);
                                    return httpContext.Response.WriteAsync(val.Replace("{localhost}", CoreInit.Host(httpContext)));
                                }
                                else
                                {
                                    return httpContext.Response.SendFileAsync(over.val);
                                }
                            }
                        case "redirect":
                            {
                                httpContext.Response.Redirect(over.val);
                                return Task.CompletedTask;
                            }
                        case "eval":
                            {
                                string url = httpContext.Request.Path.Value + httpContext.Request.QueryString.Value;
                                bool _next = CSharpEval.BaseExecute<bool>(over.val, new OverrideResponseGlobals(url, httpContext.Request, requestInfo), evalOptions);
                                if (!_next)
                                    return Task.CompletedTask;
                                break;
                            }
                        default:
                            break;
                    }
                }
            }

            return _next(httpContext);
        }
    }
}
