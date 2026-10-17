using System.Collections.Generic;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using V3 = Whisparr.Api.V3.System.Backup;
using V5 = Whisparr.Api.V5.System.Backup;

namespace NzbDrone.Host.OpenApi
{
    // UploadAndRestore reads Request.Form.Files rather than binding a parameter, so ApiExplorer sees
    // no body. It takes the first file whatever the field is called; "restore" is what the UI sends
    public class BackupRestoreUploadOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var declaringType = context.MethodInfo.DeclaringType;

            if ((declaringType != typeof(V3.BackupController) && declaringType != typeof(V5.BackupController)) ||
                context.MethodInfo.Name != nameof(V3.BackupController.UploadAndRestore))
            {
                return;
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["restore"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                            },
                            Required = new HashSet<string> { "restore" }
                        }
                    }
                }
            };
        }
    }
}
