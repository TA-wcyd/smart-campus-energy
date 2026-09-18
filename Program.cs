using System.IO;

// Switch working directory to MyApp.Api directory so static files, views, and controllers resolve properly
var candidatePath = Path.Combine(Directory.GetCurrentDirectory(), "MyApp.Api");
if (Directory.Exists(candidatePath))
{
    Directory.SetCurrentDirectory(candidatePath);
}

// Forward execution to MyApp.Api
var apiAssembly = typeof(MyApp.Api.Middleware.GlobalExceptionHandler).Assembly;
var entryPoint = apiAssembly.EntryPoint;

if (entryPoint != null)
{
    var result = entryPoint.Invoke(null, new object?[] { args });
    if (result is Task task)
    {
        await task;
    }
}
